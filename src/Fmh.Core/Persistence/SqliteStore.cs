using System.Globalization;
using System.Text.Json;
using Fmh.Core.Data;
using Fmh.Core.Model;
using Microsoft.Data.Sqlite;

namespace Fmh.Core.Persistence;

/// <summary>
/// Spielstand als SQLite-Datenbank (Kapitel 12). Jede Entität hat eine eigene Tabelle, Spielerattribute
/// stehen als Spalten, damit Editor und Auswertungen direkt per SQL arbeiten können. Verschachtelte
/// Konfigurationen (Ligaformat, Taktik, Profil) liegen als JSON-Spalten daneben.
/// </summary>
public static class SqliteStore
{
    /// <summary>Aktuelle Schemaversion. Ältere Spielstände werden beim Laden mit <see cref="Migrations"/> angehoben.</summary>
    public const int SchemaVersion = 4;

    /// <summary>Migrationsschritte: Schlüssel = Ausgangsversion; das SQL hebt die Datenbank um eine Version an.</summary>
    private static readonly Dictionary<int, string> Migrations = new()
    {
        // Phase 2: Pokal, Relegation, Transfers, Verträge, Training, Scouting, Jugend.
        [1] = """
            ALTER TABLE fixtures RENAME COLUMN league_id TO competition_id;
            ALTER TABLE fixtures ADD COLUMN kind TEXT NOT NULL DEFAULT 'League';
            ALTER TABLE fixtures ADD COLUMN tie_id INTEGER;
            ALTER TABLE fixtures ADD COLUMN leg INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fixtures ADD COLUMN extra_time INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fixtures ADD COLUMN pens_home INTEGER;
            ALTER TABLE fixtures ADD COLUMN pens_away INTEGER;
            ALTER TABLE clubs ADD COLUMN balance INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE clubs ADD COLUMN transfer_budget INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE clubs ADD COLUMN wage_budget INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE clubs ADD COLUMN coach_quality INTEGER NOT NULL DEFAULT 10;
            ALTER TABLE clubs ADD COLUMN youth_facilities INTEGER NOT NULL DEFAULT 10;
            ALTER TABLE clubs ADD COLUMN youth_recruitment INTEGER NOT NULL DEFAULT 10;
            ALTER TABLE clubs ADD COLUMN saved_tactics TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN familiarity TEXT NOT NULL DEFAULT '{}';
            ALTER TABLE clubs ADD COLUMN training TEXT;
            ALTER TABLE clubs ADD COLUMN camp TEXT;
            ALTER TABLE clubs ADD COLUMN youth_ids TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN shortlist TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN player_ids TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE players ADD COLUMN contract TEXT;
            ALTER TABLE players ADD COLUMN is_youth INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE players ADD COLUMN retired INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE players ADD COLUMN transfer_listed INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE players ADD COLUMN asking_price INTEGER;
            ALTER TABLE players ADD COLUMN transfer TEXT;
            ALTER TABLE players ADD COLUMN loan TEXT;
            ALTER TABLE players ADD COLUMN training TEXT;
            ALTER TABLE players ADD COLUMN pre_contract TEXT;
            ALTER TABLE messages ADD COLUMN action TEXT NOT NULL DEFAULT 'None';
            ALTER TABLE messages ADD COLUMN action_ref INTEGER;
            ALTER TABLE messages ADD COLUMN required INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE messages ADD COLUMN resolved INTEGER NOT NULL DEFAULT 0;
            CREATE TABLE cups (id TEXT PRIMARY KEY, ord INTEGER NOT NULL, name TEXT NOT NULL, country TEXT NOT NULL, kind TEXT NOT NULL,
                season_year INTEGER NOT NULL, winner_id INTEGER, data TEXT NOT NULL);
            CREATE TABLE transfers (ord INTEGER NOT NULL, date TEXT NOT NULL, player_id INTEGER NOT NULL, from_club INTEGER, to_club INTEGER,
                fee INTEGER NOT NULL, loan INTEGER NOT NULL, season_year INTEGER NOT NULL);
            CREATE INDEX transfers_player ON transfers (player_id);
            CREATE TABLE transactions (ord INTEGER NOT NULL, date TEXT NOT NULL, club_id INTEGER NOT NULL, type TEXT NOT NULL,
                amount INTEGER NOT NULL, text TEXT NOT NULL);
            CREATE INDEX transactions_club ON transactions (club_id);
            CREATE TABLE installments (ord INTEGER NOT NULL, from_club INTEGER NOT NULL, to_club INTEGER NOT NULL, due TEXT NOT NULL,
                amount INTEGER NOT NULL, text TEXT NOT NULL);
            CREATE TABLE negotiations (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL, buyer_id INTEGER NOT NULL, seller_id INTEGER,
                type TEXT NOT NULL, stage TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE negotiation_locks (key TEXT PRIMARY KEY, until TEXT NOT NULL);
            CREATE TABLE scouts (id INTEGER PRIMARY KEY, club_id INTEGER NOT NULL, name TEXT NOT NULL, judging_ability INTEGER NOT NULL,
                judging_potential INTEGER NOT NULL, region_knowledge INTEGER NOT NULL, region TEXT NOT NULL);
            CREATE TABLE scout_assignments (id INTEGER PRIMARY KEY, scout_id INTEGER NOT NULL, task TEXT NOT NULL, player_id INTEGER,
                league_id TEXT, position TEXT, max_age INTEGER NOT NULL, start_date TEXT NOT NULL, end_date TEXT NOT NULL);
            CREATE TABLE scout_reports (player_id INTEGER PRIMARY KEY, date TEXT NOT NULL, strength REAL NOT NULL, potential REAL NOT NULL,
                recommendation TEXT NOT NULL, summary TEXT NOT NULL);
            CREATE TABLE knowledge (player_id INTEGER PRIMARY KEY, value INTEGER NOT NULL);
            """,
        // Phase 3, Mehrsprachigkeit: Texte als sprachneutrale Verweise (Schlüssel + Argumente, JSON).
        [2] = """
            ALTER TABLE messages ADD COLUMN sender_key TEXT;
            ALTER TABLE messages ADD COLUMN subject_ref TEXT;
            ALTER TABLE messages ADD COLUMN body_ref TEXT;
            ALTER TABLE transactions ADD COLUMN text_ref TEXT;
            ALTER TABLE installments ADD COLUMN text_ref TEXT;
            ALTER TABLE scout_reports ADD COLUMN summary_ref TEXT;
            """,
        // Phase 3: Finanzen, Stadion, Gelände, Sponsoren, Vorstand, Fans, Personal, Ereignisse, Managerkarriere.
        [3] = """
            ALTER TABLE clubs ADD COLUMN finance TEXT;
            ALTER TABLE clubs ADD COLUMN stadium_data TEXT;
            ALTER TABLE clubs ADD COLUMN grounds TEXT;
            ALTER TABLE clubs ADD COLUMN sponsors TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN sponsor_offers TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN board TEXT;
            ALTER TABLE clubs ADD COLUMN fans TEXT;
            ALTER TABLE clubs ADD COLUMN rivals TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE managers ADD COLUMN career TEXT;
            ALTER TABLE leagues ADD COLUMN deductions TEXT NOT NULL DEFAULT '{}';
            CREATE TABLE staff (id INTEGER PRIMARY KEY, club_id INTEGER, name TEXT NOT NULL, role TEXT NOT NULL, ability INTEGER NOT NULL,
                data TEXT NOT NULL);
            CREATE TABLE events (id INTEGER PRIMARY KEY, event_id TEXT NOT NULL, date TEXT NOT NULL, chosen TEXT, data TEXT NOT NULL);
            CREATE TABLE event_history (event_id TEXT PRIMARY KEY, date TEXT NOT NULL);
            """,
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string AttrCol(Attr a) => "a_" + a;

    private static readonly string[] HiddenCols =
    {
        "current_ability", "potential_ability", "ambition", "professionalism", "loyalty", "pressure", "temperament",
        "sportsmanship", "adaptability", "controversy", "consistency", "injury_proneness", "important_matches",
    };

    private static string V1Schema() => $"""
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE countries (code TEXT PRIMARY KEY, config TEXT NOT NULL);
        CREATE TABLE referees (id INTEGER PRIMARY KEY, name TEXT NOT NULL, strictness INTEGER NOT NULL);
        CREATE TABLE managers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, human INTEGER NOT NULL, club_id INTEGER,
            reputation INTEGER NOT NULL, profile TEXT NOT NULL);
        CREATE TABLE clubs (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, name TEXT NOT NULL, short_name TEXT NOT NULL,
            city TEXT NOT NULL, country TEXT NOT NULL, reputation INTEGER NOT NULL, primary_color TEXT NOT NULL,
            secondary_color TEXT NOT NULL, stadium TEXT NOT NULL, capacity INTEGER NOT NULL, league_id TEXT NOT NULL,
            manager_id INTEGER, tactic TEXT NOT NULL);
        CREATE TABLE players (id INTEGER PRIMARY KEY, first_name TEXT NOT NULL, last_name TEXT NOT NULL, birth_date TEXT NOT NULL,
            nationalities TEXT NOT NULL, height INTEGER NOT NULL, weight INTEGER NOT NULL, left_foot INTEGER NOT NULL,
            right_foot INTEGER NOT NULL, main_position TEXT NOT NULL, club_id INTEGER, reputation INTEGER NOT NULL,
            contract_wage INTEGER, contract_until TEXT,
            {string.Join(", ", AttrInfo.All.Select(a => AttrCol(a) + " INTEGER NOT NULL"))},
            {string.Join(", ", HiddenCols.Select(c => c + " INTEGER NOT NULL"))},
            condition REAL NOT NULL, match_practice REAL NOT NULL, morale TEXT NOT NULL, recent_grades TEXT NOT NULL,
            injury_type TEXT, injury_days INTEGER, injury_total INTEGER, suspended INTEGER NOT NULL, yellow_acc INTEGER NOT NULL);
        CREATE INDEX players_club ON players (club_id);
        CREATE TABLE player_positions (player_id INTEGER NOT NULL, position TEXT NOT NULL, suitability TEXT NOT NULL,
            PRIMARY KEY (player_id, position));
        CREATE TABLE player_seasons (player_id INTEGER NOT NULL, current INTEGER NOT NULL, ord INTEGER NOT NULL,
            season_year INTEGER NOT NULL, club_id INTEGER, apps INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL,
            assists INTEGER NOT NULL, yellow INTEGER NOT NULL, red INTEGER NOT NULL, graded INTEGER NOT NULL, grade_sum REAL NOT NULL);
        CREATE INDEX player_seasons_player ON player_seasons (player_id);
        CREATE TABLE leagues (id TEXT PRIMARY KEY, ord INTEGER NOT NULL, name TEXT NOT NULL, country TEXT NOT NULL,
            level INTEGER NOT NULL, format TEXT NOT NULL);
        CREATE TABLE league_clubs (league_id TEXT NOT NULL, club_id INTEGER NOT NULL, ord INTEGER NOT NULL);
        CREATE TABLE fixtures (id INTEGER PRIMARY KEY, league_id TEXT NOT NULL, matchday INTEGER NOT NULL, date TEXT NOT NULL,
            home_id INTEGER NOT NULL, away_id INTEGER NOT NULL, referee_id INTEGER NOT NULL,
            played INTEGER NOT NULL, home_goals INTEGER, away_goals INTEGER, ht_home INTEGER, ht_away INTEGER, attendance INTEGER,
            weather TEXT, home_xg REAL, away_xg REAL, home_possession INTEGER, home_shots INTEGER, away_shots INTEGER, potm INTEGER);
        CREATE INDEX fixtures_league ON fixtures (league_id, matchday);
        CREATE TABLE goals (fixture_id INTEGER NOT NULL, ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
            scorer_id INTEGER NOT NULL, assist_id INTEGER, penalty INTEGER NOT NULL, own_goal INTEGER NOT NULL);
        CREATE TABLE cards (fixture_id INTEGER NOT NULL, ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
            player_id INTEGER NOT NULL, red INTEGER NOT NULL);
        CREATE TABLE messages (id INTEGER PRIMARY KEY, manager_id INTEGER NOT NULL, date TEXT NOT NULL, type TEXT NOT NULL,
            sender TEXT NOT NULL, subject TEXT NOT NULL, body TEXT NOT NULL, read INTEGER NOT NULL);
        CREATE TABLE history (ord INTEGER NOT NULL, season_year INTEGER NOT NULL, league_id TEXT NOT NULL, league_name TEXT NOT NULL,
            champion_id INTEGER NOT NULL, champion_name TEXT NOT NULL, final_order TEXT NOT NULL);
        """;

    /// <summary>Neues Schema = Version 1 plus alle Migrationen; so sind neue und migrierte Spielstände identisch aufgebaut.</summary>
    private static string CreateSchema() =>
        V1Schema() + string.Concat(Enumerable.Range(1, SchemaVersion - 1).Select(v => Migrations[v]));

    // ------------------------------------------------------------------ Speichern

    public static void Save(World world, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var con = Open(tmp))
        {
            using var tx = con.BeginTransaction();
            Exec(con, tx, CreateSchema());
            WriteMeta(con, tx, world);
            WriteAll(con, tx, world);
            tx.Commit();
        }
        SqliteConnection.ClearAllPools();
        File.Move(tmp, path, overwrite: true);
    }

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var con = new SqliteConnection(cs);
        con.Open();
        return con;
    }

    private static void Exec(SqliteConnection con, SqliteTransaction? tx, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void WriteMeta(SqliteConnection con, SqliteTransaction tx, World w)
    {
        var club = w.Managers.FirstOrDefault(m => m.Human) is { ClubId: { } cid } && w.Clubs.TryGetValue(cid, out var c) ? c : null;
        var meta = new Dictionary<string, string>
        {
            ["schema_version"] = SchemaVersion.ToString(Inv),
            ["save_version"] = w.SaveVersion.ToString(Inv),
            ["seed"] = w.Seed.ToString(Inv),
            ["rng_state"] = JsonSerializer.Serialize(w.RngState),
            ["date"] = D(w.Date),
            ["season_year"] = w.SeasonYear.ToString(Inv),
            ["season_finished"] = w.SeasonFinished ? "1" : "0",
            ["autosave_counter"] = w.AutosaveCounter.ToString(Inv),
            ["next_player_id"] = w.NextPlayerId.ToString(Inv),
            ["next_fixture_id"] = w.NextFixtureId.ToString(Inv),
            ["next_message_id"] = w.NextMessageId.ToString(Inv),
            ["next_tie_id"] = w.NextTieId.ToString(Inv),
            ["next_negotiation_id"] = w.NextNegotiationId.ToString(Inv),
            ["next_scout_id"] = w.NextScoutId.ToString(Inv),
            ["next_assignment_id"] = w.NextAssignmentId.ToString(Inv),
            ["next_staff_id"] = w.NextStaffId.ToString(Inv),
            ["next_building_id"] = w.NextBuildingId.ToString(Inv),
            ["next_project_id"] = w.NextProjectId.ToString(Inv),
            ["next_sponsor_id"] = w.NextSponsorId.ToString(Inv),
            ["next_loan_id"] = w.NextLoanId.ToString(Inv),
            ["next_event_id"] = w.NextEventId.ToString(Inv),
            ["pending_press"] = w.PendingPress is null ? "" : J(w.PendingPress),
            ["rules"] = JsonSerializer.Serialize(w.Rules, Json.Compact),
            ["packages"] = JsonSerializer.Serialize(w.Packages),
            ["saved_at"] = DateTime.Now.ToString("O", Inv),
            ["manager_name"] = w.Managers.FirstOrDefault(m => m.Human)?.Name ?? "",
            ["club_name"] = club?.Name ?? "",
            ["club_key"] = club?.Key ?? "",
        };
        using var cmd = Cmd(con, tx, "INSERT INTO meta (key, value) VALUES ($k, $v)", "$k", "$v");
        foreach (var (k, v) in meta) Run(cmd, k, v);
    }

    private static void WriteAll(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Cmd(con, tx, "INSERT INTO countries VALUES ($a, $b)", "$a", "$b"))
            foreach (var c in w.Countries) Run(cmd, c.Code, JsonSerializer.Serialize(c, Json.Compact));

        using (var cmd = Cmd(con, tx, "INSERT INTO referees VALUES ($a, $b, $c)", "$a", "$b", "$c"))
            foreach (var r in w.Referees) Run(cmd, r.Id, r.Name, r.Strictness);

        using (var cmd = Cmd(con, tx, "INSERT INTO managers VALUES ($a, $b, $c, $d, $e, $f, $g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            foreach (var m in w.Managers) Run(cmd, m.Id, m.Name, m.Human, m.ClubId, m.Reputation, JsonSerializer.Serialize(m.Profile, Json.Compact), J(m.Career));

        var clubParams = Enumerable.Range(0, 35).Select(i => "$c" + i).ToArray();
        using (var cmd = Cmd(con, tx, $"INSERT INTO clubs VALUES ({string.Join(",", clubParams)})", clubParams))
            foreach (var c in w.Clubs.Values)
                Run(cmd, c.Id, c.Key, c.Name, c.ShortName, c.City, c.CountryCode, c.Reputation, c.PrimaryColor, c.SecondaryColor,
                    c.StadiumName, c.StadiumCapacity, c.LeagueId, c.ManagerId, J(c.Tactic),
                    c.Budget.Balance, c.Budget.TransferBudget, c.Budget.WageBudget, c.CoachQuality, c.YouthFacilities, c.YouthRecruitment,
                    J(c.SavedTactics), J(c.Familiarity), J(c.Training), c.Camp is null ? null : J(c.Camp), J(c.YouthIds), J(c.Shortlist), J(c.PlayerIds),
                    J(c.Finance), J(c.Stadium), J(c.Grounds), J(c.Sponsors), J(c.SponsorOffers), J(c.Board), J(c.Fans), J(c.Rivals));

        WritePlayers(con, tx, w);

        using (var cmd = Cmd(con, tx, "INSERT INTO leagues VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            for (var i = 0; i < w.Leagues.Count; i++)
            {
                var l = w.Leagues[i];
                Run(cmd, l.Id, i, l.Name, l.CountryCode, l.Level, JsonSerializer.Serialize(l.Format, Json.Compact), J(l.Deductions));
            }
        using (var cmd = Cmd(con, tx, "INSERT INTO league_clubs VALUES ($a,$b,$c)", "$a", "$b", "$c"))
            foreach (var l in w.Leagues)
                for (var i = 0; i < l.ClubIds.Count; i++) Run(cmd, l.Id, l.ClubIds[i], i);

        var fxParams = Enumerable.Range(0, 26).Select(i => "$f" + i).ToArray();
        using (var fx = Cmd(con, tx, $"INSERT INTO fixtures VALUES ({string.Join(",", fxParams)})", fxParams))
        using (var gl = Cmd(con, tx, "INSERT INTO goals VALUES ($a,$b,$c,$d,$e,$f,$g,$h)", "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h"))
        using (var cd = Cmd(con, tx, "INSERT INTO cards VALUES ($a,$b,$c,$d,$e,$f)", "$a", "$b", "$c", "$d", "$e", "$f"))
        {
            foreach (var f in w.AllFixtures)
            {
                var r = f.Result;
                Run(fx, f.Id, f.CompetitionId, f.Matchday, D(f.Date), f.HomeId, f.AwayId, f.RefereeId, r is not null,
                    r?.HomeGoals, r?.AwayGoals, r?.HalfTimeHome, r?.HalfTimeAway, r?.Attendance, r?.Weather.ToString(),
                    r?.HomeXg, r?.AwayXg, r?.HomePossession, r?.HomeShots, r?.AwayShots, r?.PlayerOfTheMatchId,
                    f.Kind.ToString(), f.TieId, f.Leg, r?.ExtraTime ?? false, r?.PenaltiesHome, r?.PenaltiesAway);
                if (r is null) continue;
                for (var i = 0; i < r.Goals.Count; i++)
                {
                    var g = r.Goals[i];
                    Run(gl, f.Id, i, g.Minute, g.Home, g.ScorerId, g.AssistId, g.Penalty, g.OwnGoal);
                }
                for (var i = 0; i < r.Cards.Count; i++)
                {
                    var c = r.Cards[i];
                    Run(cd, f.Id, i, c.Minute, c.Home, c.PlayerId, c.Red);
                }
            }
        }

        var msgParams = Enumerable.Range(0, 15).Select(i => "$m" + i).ToArray();
        using (var cmd = Cmd(con, tx, $"INSERT INTO messages VALUES ({string.Join(",", msgParams)})", msgParams))
            foreach (var m in w.Messages)
                Run(cmd, m.Id, m.ManagerId, D(m.Date), m.Type.ToString(), m.Sender, m.Subject, m.Body, m.Read,
                    m.Action.ToString(), m.ActionRefId, m.Required, m.Resolved,
                    m.SenderKey, Ref(m.SubjectRef), Ref(m.BodyRef));

        WriteCareer(con, tx, w);

        using (var cmd = Cmd(con, tx, "INSERT INTO history VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            for (var i = 0; i < w.History.Count; i++)
            {
                var h = w.History[i];
                Run(cmd, i, h.SeasonYear, h.LeagueId, h.LeagueName, h.ChampionId, h.ChampionName, JsonSerializer.Serialize(h.FinalOrder));
            }
    }

    private static void WritePlayers(SqliteConnection con, SqliteTransaction tx, World w)
    {
        var cols = new List<string>
        {
            "id", "first_name", "last_name", "birth_date", "nationalities", "height", "weight", "left_foot", "right_foot",
            "main_position", "club_id", "reputation", "contract_wage", "contract_until",
        };
        cols.AddRange(AttrInfo.All.Select(AttrCol));
        cols.AddRange(HiddenCols);
        cols.AddRange(new[] { "condition", "match_practice", "morale", "recent_grades", "injury_type", "injury_days", "injury_total", "suspended", "yellow_acc" });
        cols.AddRange(new[] { "contract", "is_youth", "retired", "transfer_listed", "asking_price", "transfer", "loan", "training", "pre_contract" });
        var names = cols.Select((_, i) => "$p" + i).ToArray();
        using var cmd = Cmd(con, tx, $"INSERT INTO players ({string.Join(",", cols)}) VALUES ({string.Join(",", names)})", names);
        using var pos = Cmd(con, tx, "INSERT INTO player_positions VALUES ($a,$b,$c)", "$a", "$b", "$c");
        using var sea = Cmd(con, tx, "INSERT INTO player_seasons VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m)",
            "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h", "$i", "$j", "$k", "$l", "$m");

        foreach (var p in w.Players.Values)
        {
            var h = p.Hidden;
            var st = p.State;
            var values = new List<object?>
            {
                p.Id, p.FirstName, p.LastName, D(p.BirthDate), string.Join(",", p.Nationalities), p.Height, p.Weight, p.LeftFoot, p.RightFoot,
                p.MainPosition.ToString(), p.ClubId, p.Reputation, p.Contract?.WeeklyWage, p.Contract is { } c ? D(c.Until) : null,
            };
            values.AddRange(AttrInfo.All.Select(a => (object?)p.Attributes[a]));
            values.AddRange(new object?[]
            {
                h.CurrentAbility, h.PotentialAbility, h.Ambition, h.Professionalism, h.Loyalty, h.Pressure, h.Temperament,
                h.Sportsmanship, h.Adaptability, h.Controversy, h.Consistency, h.InjuryProneness, h.ImportantMatches,
                st.Condition, st.MatchPractice, st.Morale.ToString(), JsonSerializer.Serialize(st.RecentGrades),
                st.Injury?.Type, st.Injury?.DaysRemaining, st.Injury?.TotalDays, st.SuspendedMatches, st.YellowCardsAccumulated,
                p.Contract is null ? null : J(p.Contract), p.IsYouth, p.Retired, p.Transfer.Listed, p.Transfer.AskingPrice,
                J(p.Transfer), p.Loan is null ? null : J(p.Loan), J(p.Training), p.PreContract is null ? null : J(p.PreContract),
            });
            Run(cmd, values.ToArray());

            foreach (var (position, suit) in p.Positions) Run(pos, p.Id, position.ToString(), suit.ToString());

            void Season(PlayerSeasonStats s, bool current, int ord) =>
                Run(sea, p.Id, current, ord, s.SeasonYear, s.ClubId, s.Appearances, s.Minutes, s.Goals, s.Assists,
                    s.YellowCards, s.RedCards, s.GradedMatches, s.GradeSum);
            Season(p.SeasonStats, true, 0);
            for (var i = 0; i < p.History.Count; i++) Season(p.History[i], false, i);
        }
    }

    // ------------------------------------------------------------------ Laden

    public static World Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Spielstand nicht gefunden.", path);
        using (var con = Open(path))
        {
            Migrate(con);
        }
        using var db = Open(path, readOnly: true);
        var w = ReadMeta(db);
        ReadAll(db, w);
        SqliteConnection.ClearAllPools();
        return w;
    }

    /// <summary>Hebt einen Spielstand schrittweise auf die aktuelle Schemaversion an.</summary>
    public static void Migrate(SqliteConnection con)
    {
        var version = ReadSchemaVersion(con);
        if (version > SchemaVersion)
            throw new InvalidDataException($"Spielstand-Version {version} ist neuer als dieses Spiel ({SchemaVersion}).");
        while (version < SchemaVersion)
        {
            if (!Migrations.TryGetValue(version, out var sql))
                throw new InvalidDataException($"Keine Migration von Spielstand-Version {version}.");
            using var tx = con.BeginTransaction();
            Exec(con, tx, sql);
            version++;
            Exec(con, tx, $"UPDATE meta SET value = '{version}' WHERE key = 'schema_version'");
            tx.Commit();
        }
    }

    private static int ReadSchemaVersion(SqliteConnection con)
    {
        try
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
            return cmd.ExecuteScalar() is string s ? int.Parse(s, Inv) : 0;
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("Datei ist kein FMH-Spielstand.", ex);
        }
    }

    public static Dictionary<string, string> ReadMetaTable(string path)
    {
        using var con = Open(path, readOnly: true);
        var meta = new Dictionary<string, string>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM meta";
        using var r = cmd.ExecuteReader();
        while (r.Read()) meta[r.GetString(0)] = r.GetString(1);
        SqliteConnection.ClearAllPools();
        return meta;
    }

    private static World ReadMeta(SqliteConnection con)
    {
        var meta = new Dictionary<string, string>();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM meta";
            using var r = cmd.ExecuteReader();
            while (r.Read()) meta[r.GetString(0)] = r.GetString(1);
        }
        int I(string k) => int.Parse(meta[k], Inv);
        return new World
        {
            SaveVersion = I("save_version"),
            Seed = ulong.Parse(meta["seed"], Inv),
            RngState = JsonSerializer.Deserialize<ulong[]>(meta["rng_state"]) ?? Array.Empty<ulong>(),
            Date = PD(meta["date"]),
            SeasonYear = I("season_year"),
            SeasonFinished = meta["season_finished"] == "1",
            AutosaveCounter = I("autosave_counter"),
            NextPlayerId = I("next_player_id"),
            NextFixtureId = I("next_fixture_id"),
            NextMessageId = I("next_message_id"),
            NextTieId = meta.ContainsKey("next_tie_id") ? I("next_tie_id") : 1,
            NextNegotiationId = meta.ContainsKey("next_negotiation_id") ? I("next_negotiation_id") : 1,
            NextScoutId = meta.ContainsKey("next_scout_id") ? I("next_scout_id") : 1,
            NextAssignmentId = meta.ContainsKey("next_assignment_id") ? I("next_assignment_id") : 1,
            NextStaffId = meta.ContainsKey("next_staff_id") ? I("next_staff_id") : 1,
            NextBuildingId = meta.ContainsKey("next_building_id") ? I("next_building_id") : 1,
            NextProjectId = meta.ContainsKey("next_project_id") ? I("next_project_id") : 1,
            NextSponsorId = meta.ContainsKey("next_sponsor_id") ? I("next_sponsor_id") : 1,
            NextLoanId = meta.ContainsKey("next_loan_id") ? I("next_loan_id") : 1,
            NextEventId = meta.ContainsKey("next_event_id") ? I("next_event_id") : 1,
            PendingPress = meta.TryGetValue("pending_press", out var press) && press.Length > 0 ? PJ<PressConference>(press) : null,
            Rules = JsonSerializer.Deserialize<WorldRules>(meta["rules"], Json.Compact) ?? new WorldRules(),
            Packages = JsonSerializer.Deserialize<List<string>>(meta["packages"]) ?? new List<string>(),
        };
    }

    private static void ReadAll(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT config FROM countries ORDER BY code"))
            w.Countries.Add(JsonSerializer.Deserialize<CountryConfig>(r.GetString(0), Json.Compact)!);

        foreach (var r in Rows(con, "SELECT id, name, strictness FROM referees ORDER BY id"))
            w.Referees.Add(new Referee { Id = r.GetInt32(0), Name = r.GetString(1), Strictness = r.GetInt32(2) });

        foreach (var r in Rows(con, "SELECT id, name, human, club_id, reputation, profile, career FROM managers ORDER BY id"))
            w.Managers.Add(new Manager
            {
                Id = r.GetInt32(0), Name = r.GetString(1), Human = r.GetInt32(2) == 1, ClubId = NInt(r, 3), Reputation = r.GetInt32(4),
                Profile = JsonSerializer.Deserialize<ManagerProfile>(r.GetString(5), Json.Compact) ?? new ManagerProfile(),
                Career = r.IsDBNull(6) ? new ManagerCareer() : PJ<ManagerCareer>(r.GetString(6)) ?? new(),
            });

        foreach (var r in Rows(con, "SELECT * FROM clubs ORDER BY id"))
        {
            var c = new Club
            {
                Id = r.GetInt32(0), Key = r.GetString(1), Name = r.GetString(2), ShortName = r.GetString(3), City = r.GetString(4),
                CountryCode = r.GetString(5), Reputation = r.GetInt32(6), PrimaryColor = r.GetString(7), SecondaryColor = r.GetString(8),
                StadiumName = r.GetString(9), StadiumCapacity = r.GetInt32(10), LeagueId = r.GetString(11), ManagerId = NInt(r, 12),
                Tactic = JsonSerializer.Deserialize<Tactic>(r.GetString(13), Json.Compact) ?? new Tactic(),
                Budget = new ClubBudget { Balance = r.GetInt64(14), TransferBudget = r.GetInt64(15), WageBudget = r.GetInt64(16) },
                CoachQuality = r.GetInt32(17), YouthFacilities = r.GetInt32(18), YouthRecruitment = r.GetInt32(19),
                SavedTactics = PJ<List<Tactic>>(r.GetString(20)) ?? new(),
                Familiarity = PJ<Dictionary<string, double>>(r.GetString(21)) ?? new(),
                Training = r.IsDBNull(22) ? new TrainingPlan() : PJ<TrainingPlan>(r.GetString(22)) ?? new(),
                Camp = r.IsDBNull(23) ? null : PJ<TrainingCamp>(r.GetString(23)),
                YouthIds = PJ<List<int>>(r.GetString(24)) ?? new(),
                Shortlist = PJ<List<int>>(r.GetString(25)) ?? new(),
                PlayerIds = PJ<List<int>>(r.GetString(26)) ?? new(),
                Finance = Opt<FinanceState>(r, 27) ?? new(),
                Stadium = Opt<Stadium>(r, 28) ?? new(),
                Grounds = Opt<Grounds>(r, 29) ?? new(),
                Sponsors = Opt<List<SponsorDeal>>(r, 30) ?? new(),
                SponsorOffers = Opt<List<SponsorDeal>>(r, 31) ?? new(),
                Board = Opt<BoardState>(r, 32) ?? new(),
                Fans = Opt<FanState>(r, 33) ?? new(),
                Rivals = Opt<List<int>>(r, 34) ?? new(),
            };
            w.Clubs[c.Id] = c;
        }

        ReadPlayers(con, w);
        // Spielstände aus Version 1 kennen die Kaderreihenfolge nicht: aus der Spieler-Id rekonstruieren.
        foreach (var club in w.Clubs.Values.Where(c => c.PlayerIds.Count == 0))
            club.PlayerIds = w.Players.Values.Where(p => p.ClubId == club.Id && !p.IsYouth).Select(p => p.Id).Order().ToList();

        foreach (var r in Rows(con, "SELECT id, name, country, level, format, deductions FROM leagues ORDER BY ord"))
            w.Leagues.Add(new League
            {
                Id = r.GetString(0), Name = r.GetString(1), CountryCode = r.GetString(2), Level = r.GetInt32(3),
                Format = JsonSerializer.Deserialize<DivisionConfig>(r.GetString(4), Json.Compact)!,
                Deductions = PJ<Dictionary<int, int>>(r.GetString(5)) ?? new(),
            });
        var leagues = w.Leagues.ToDictionary(l => l.Id);
        foreach (var r in Rows(con, "SELECT league_id, club_id FROM league_clubs ORDER BY league_id, ord"))
            leagues[r.GetString(0)].ClubIds.Add(r.GetInt32(1));

        foreach (var r in Rows(con, "SELECT data FROM cups ORDER BY ord"))
            w.Cups.Add(PJ<Cup>(r.GetString(0))!);
        var cups = w.Cups.ToDictionary(c => c.Id);

        var fixtures = new Dictionary<int, Fixture>();
        foreach (var r in Rows(con, "SELECT * FROM fixtures ORDER BY id"))
        {
            var f = new Fixture
            {
                Id = r.GetInt32(0), CompetitionId = r.GetString(1), Matchday = r.GetInt32(2), Date = PD(r.GetString(3)),
                HomeId = r.GetInt32(4), AwayId = r.GetInt32(5), RefereeId = r.GetInt32(6),
                Kind = Enum.Parse<CompetitionKind>(r.GetString(20)), TieId = NInt(r, 21), Leg = r.GetInt32(22),
            };
            if (r.GetInt32(7) == 1)
            {
                f.Result = new MatchResult
                {
                    HomeGoals = r.GetInt32(8), AwayGoals = r.GetInt32(9), HalfTimeHome = r.GetInt32(10), HalfTimeAway = r.GetInt32(11),
                    Attendance = r.GetInt32(12), Weather = Enum.Parse<Weather>(r.GetString(13)), HomeXg = r.GetDouble(14),
                    AwayXg = r.GetDouble(15), HomePossession = r.GetInt32(16), HomeShots = r.GetInt32(17), AwayShots = r.GetInt32(18),
                    PlayerOfTheMatchId = NInt(r, 19),
                    ExtraTime = r.GetInt32(23) == 1, PenaltiesHome = NInt(r, 24), PenaltiesAway = NInt(r, 25),
                };
            }
            fixtures[f.Id] = f;
            if (f.Kind == CompetitionKind.League) leagues[f.CompetitionId].Fixtures.Add(f);
            else cups[f.CompetitionId].Fixtures.Add(f);
        }
        foreach (var r in Rows(con, "SELECT fixture_id, minute, home, scorer_id, assist_id, penalty, own_goal FROM goals ORDER BY fixture_id, ord"))
            fixtures[r.GetInt32(0)].Result!.Goals.Add(new GoalRecord
            {
                Minute = r.GetInt32(1), Home = r.GetInt32(2) == 1, ScorerId = r.GetInt32(3), AssistId = NInt(r, 4),
                Penalty = r.GetInt32(5) == 1, OwnGoal = r.GetInt32(6) == 1,
            });
        foreach (var r in Rows(con, "SELECT fixture_id, minute, home, player_id, red FROM cards ORDER BY fixture_id, ord"))
            fixtures[r.GetInt32(0)].Result!.Cards.Add(new CardRecord
            {
                Minute = r.GetInt32(1), Home = r.GetInt32(2) == 1, PlayerId = r.GetInt32(3), Red = r.GetInt32(4) == 1,
            });

        foreach (var r in Rows(con, "SELECT * FROM messages ORDER BY id"))
            w.Messages.Add(new Message
            {
                Id = r.GetInt32(0), ManagerId = r.GetInt32(1), Date = PD(r.GetString(2)), Type = Enum.Parse<MessageType>(r.GetString(3)),
                Sender = r.GetString(4), Subject = r.GetString(5), Body = r.GetString(6), Read = r.GetInt32(7) == 1,
                Action = Enum.Parse<MessageAction>(r.GetString(8)), ActionRefId = NInt(r, 9),
                Required = r.GetInt32(10) == 1, Resolved = r.GetInt32(11) == 1,
                SenderKey = NStr(r, 12), SubjectRef = PRef(r, 13), BodyRef = PRef(r, 14),
            });

        ReadCareer(con, w);

        foreach (var r in Rows(con, "SELECT season_year, league_id, league_name, champion_id, champion_name, final_order FROM history ORDER BY ord"))
            w.History.Add(new SeasonRecord
            {
                SeasonYear = r.GetInt32(0), LeagueId = r.GetString(1), LeagueName = r.GetString(2), ChampionId = r.GetInt32(3),
                ChampionName = r.GetString(4), FinalOrder = JsonSerializer.Deserialize<List<int>>(r.GetString(5)) ?? new List<int>(),
            });
    }

    private static void ReadPlayers(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT * FROM players ORDER BY id"))
        {
            var col = 0;
            int Next() => r.GetInt32(col++);
            var p = new Player
            {
                Id = Next(), FirstName = r.GetString(col++), LastName = r.GetString(col++), BirthDate = PD(r.GetString(col++)),
                Nationalities = r.GetString(col++).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                Height = Next(), Weight = Next(), LeftFoot = Next(), RightFoot = Next(),
                MainPosition = Enum.Parse<Position>(r.GetString(col++)),
            };
            p.ClubId = NInt(r, col++);
            p.Reputation = Next();
            var wage = NInt(r, col++);
            var until = r.IsDBNull(col) ? null : r.GetString(col);
            col++;
            if (wage is { } wv && until is not null) p.Contract = new Contract { WeeklyWage = wv, Until = PD(until) };
            foreach (var a in AttrInfo.All) p.Attributes[a] = Next();
            var h = p.Hidden;
            h.CurrentAbility = Next(); h.PotentialAbility = Next(); h.Ambition = Next(); h.Professionalism = Next();
            h.Loyalty = Next(); h.Pressure = Next(); h.Temperament = Next(); h.Sportsmanship = Next(); h.Adaptability = Next();
            h.Controversy = Next(); h.Consistency = Next(); h.InjuryProneness = Next(); h.ImportantMatches = Next();
            var st = p.State;
            st.Condition = r.GetDouble(col++);
            st.MatchPractice = r.GetDouble(col++);
            st.Morale = Enum.Parse<Morale>(r.GetString(col++));
            st.RecentGrades = JsonSerializer.Deserialize<List<double>>(r.GetString(col++)) ?? new List<double>();
            var injType = r.IsDBNull(col) ? null : r.GetString(col);
            col++;
            var injDays = NInt(r, col++);
            var injTotal = NInt(r, col++);
            if (injType is not null) st.Injury = new Injury { Type = injType, DaysRemaining = injDays ?? 0, TotalDays = injTotal ?? 0 };
            st.SuspendedMatches = Next();
            st.YellowCardsAccumulated = Next();
            if (!r.IsDBNull(col) && PJ<Contract>(r.GetString(col)) is { } full) p.Contract = full;
            col++;
            p.IsYouth = Next() == 1;
            p.Retired = Next() == 1;
            col += 2; // transfer_listed, asking_price: Abfragespalten, Inhalt steht in "transfer"
            p.Transfer = r.IsDBNull(col) ? new TransferStatus() : PJ<TransferStatus>(r.GetString(col)) ?? new();
            col++;
            p.Loan = r.IsDBNull(col) ? null : PJ<Loan>(r.GetString(col));
            col++;
            p.Training = r.IsDBNull(col) ? new PlayerTraining() : PJ<PlayerTraining>(r.GetString(col)) ?? new();
            col++;
            p.PreContract = r.IsDBNull(col) ? null : PJ<PreContract>(r.GetString(col));
            w.Players[p.Id] = p;
        }

        foreach (var r in Rows(con, "SELECT player_id, position, suitability FROM player_positions"))
            w.Players[r.GetInt32(0)].Positions[Enum.Parse<Position>(r.GetString(1))] = Enum.Parse<Suitability>(r.GetString(2));

        foreach (var r in Rows(con, "SELECT * FROM player_seasons ORDER BY player_id, current, ord"))
        {
            var s = new PlayerSeasonStats
            {
                SeasonYear = r.GetInt32(3), ClubId = NInt(r, 4), Appearances = r.GetInt32(5), Minutes = r.GetInt32(6),
                Goals = r.GetInt32(7), Assists = r.GetInt32(8), YellowCards = r.GetInt32(9), RedCards = r.GetInt32(10),
                GradedMatches = r.GetInt32(11), GradeSum = r.GetDouble(12),
            };
            var p = w.Players[r.GetInt32(0)];
            if (r.GetInt32(1) == 1) p.SeasonStats = s;
            else p.History.Add(s);
        }
    }

    // ------------------------------------------------------------------ Karriere (Phase 2)

    private static void WriteCareer(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Cmd(con, tx, "INSERT INTO cups VALUES ($a,$b,$c,$d,$e,$f,$g,$h)", "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h"))
            for (var i = 0; i < w.Cups.Count; i++)
            {
                var c = w.Cups[i];
                // Spiele stehen in der Tabelle "fixtures"; im JSON nur Runden und Duelle.
                var fixtures = c.Fixtures;
                c.Fixtures = new List<Fixture>();
                try { Run(cmd, c.Id, i, c.Name, c.CountryCode, c.Kind.ToString(), c.SeasonYear, c.WinnerId, J(c)); }
                finally { c.Fixtures = fixtures; }
            }
        using (var cmd = Cmd(con, tx, "INSERT INTO transfers VALUES ($a,$b,$c,$d,$e,$f,$g,$h)", "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h"))
            for (var i = 0; i < w.Transfers.Count; i++)
            {
                var t = w.Transfers[i];
                Run(cmd, i, D(t.Date), t.PlayerId, t.FromClubId, t.ToClubId, t.Fee, t.Loan, t.SeasonYear);
            }
        using (var cmd = Cmd(con, tx, "INSERT INTO transactions VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            for (var i = 0; i < w.Transactions.Count; i++)
            {
                var t = w.Transactions[i];
                Run(cmd, i, D(t.Date), t.ClubId, t.Type.ToString(), t.Amount, t.TextRef?.ToString() ?? t.Text, Ref(t.TextRef));
            }
        using (var cmd = Cmd(con, tx, "INSERT INTO installments VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            for (var i = 0; i < w.Installments.Count; i++)
            {
                var t = w.Installments[i];
                Run(cmd, i, t.FromClubId, t.ToClubId, D(t.Due), t.Amount, t.TextRef?.ToString() ?? t.Text, Ref(t.TextRef));
            }
        using (var cmd = Cmd(con, tx, "INSERT INTO negotiations VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            foreach (var n in w.Negotiations) Run(cmd, n.Id, n.PlayerId, n.BuyerId, n.SellerId, n.Type.ToString(), n.Stage.ToString(), J(n));
        using (var cmd = Cmd(con, tx, "INSERT INTO negotiation_locks VALUES ($a,$b)", "$a", "$b"))
            foreach (var (k, until) in w.NegotiationLocks) Run(cmd, k, D(until));
        using (var cmd = Cmd(con, tx, "INSERT INTO scouts VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            foreach (var s in w.Scouts) Run(cmd, s.Id, s.ClubId, s.Name, s.JudgingAbility, s.JudgingPotential, s.RegionKnowledge, s.Region);
        using (var cmd = Cmd(con, tx, "INSERT INTO scout_assignments VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i)", "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h", "$i"))
            foreach (var a in w.ScoutAssignments)
                Run(cmd, a.Id, a.ScoutId, a.Task.ToString(), a.PlayerId, a.LeagueId, a.Position?.ToString(), a.MaxAge, D(a.Start), D(a.End));
        using (var cmd = Cmd(con, tx, "INSERT INTO scout_reports VALUES ($a,$b,$c,$d,$e,$f,$g)", "$a", "$b", "$c", "$d", "$e", "$f", "$g"))
            foreach (var r in w.ScoutReports)
                Run(cmd, r.PlayerId, D(r.Date), r.StrengthStars, r.PotentialStars, r.Recommendation.ToString(), r.SummaryRef?.ToString() ?? r.Summary, Ref(r.SummaryRef));
        using (var cmd = Cmd(con, tx, "INSERT INTO knowledge VALUES ($a,$b)", "$a", "$b"))
            foreach (var (id, v) in w.Knowledge) Run(cmd, id, v);
        using (var cmd = Cmd(con, tx, "INSERT INTO staff VALUES ($a,$b,$c,$d,$e,$f)", "$a", "$b", "$c", "$d", "$e", "$f"))
            foreach (var s in w.Staff) Run(cmd, s.Id, s.ClubId, s.Name, s.Role.ToString(), s.Ability, J(s));
        using (var cmd = Cmd(con, tx, "INSERT INTO events VALUES ($a,$b,$c,$d,$e)", "$a", "$b", "$c", "$d", "$e"))
            foreach (var e in w.Events) Run(cmd, e.Id, e.EventId, D(e.Date), e.Chosen, J(e));
        using (var cmd = Cmd(con, tx, "INSERT INTO event_history VALUES ($a,$b)", "$a", "$b"))
            foreach (var (id, d) in w.EventHistory) Run(cmd, id, D(d));
    }

    private static void ReadCareer(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT date, player_id, from_club, to_club, fee, loan, season_year FROM transfers ORDER BY ord"))
            w.Transfers.Add(new TransferRecord
            {
                Date = PD(r.GetString(0)), PlayerId = r.GetInt32(1), FromClubId = NInt(r, 2), ToClubId = NInt(r, 3),
                Fee = r.GetInt64(4), Loan = r.GetInt32(5) == 1, SeasonYear = r.GetInt32(6),
            });
        foreach (var r in Rows(con, "SELECT date, club_id, type, amount, text, text_ref FROM transactions ORDER BY ord"))
            w.Transactions.Add(new Transaction
            {
                Date = PD(r.GetString(0)), ClubId = r.GetInt32(1), Type = Enum.Parse<TransactionType>(r.GetString(2)),
                Amount = r.GetInt64(3), Text = r.GetString(4), TextRef = PRef(r, 5),
            });
        foreach (var r in Rows(con, "SELECT from_club, to_club, due, amount, text, text_ref FROM installments ORDER BY ord"))
            w.Installments.Add(new Installment
            {
                FromClubId = r.GetInt32(0), ToClubId = r.GetInt32(1), Due = PD(r.GetString(2)), Amount = r.GetInt64(3), Text = r.GetString(4), TextRef = PRef(r, 5),
            });
        foreach (var r in Rows(con, "SELECT data FROM negotiations ORDER BY id"))
            w.Negotiations.Add(PJ<Negotiation>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT key, until FROM negotiation_locks"))
            w.NegotiationLocks[r.GetString(0)] = PD(r.GetString(1));
        foreach (var r in Rows(con, "SELECT * FROM scouts ORDER BY id"))
            w.Scouts.Add(new Scout
            {
                Id = r.GetInt32(0), ClubId = r.GetInt32(1), Name = r.GetString(2), JudgingAbility = r.GetInt32(3),
                JudgingPotential = r.GetInt32(4), RegionKnowledge = r.GetInt32(5), Region = r.GetString(6),
            });
        foreach (var r in Rows(con, "SELECT * FROM scout_assignments ORDER BY id"))
            w.ScoutAssignments.Add(new ScoutAssignment
            {
                Id = r.GetInt32(0), ScoutId = r.GetInt32(1), Task = Enum.Parse<ScoutTask>(r.GetString(2)), PlayerId = NInt(r, 3),
                LeagueId = r.IsDBNull(4) ? null : r.GetString(4), Position = r.IsDBNull(5) ? null : Enum.Parse<Position>(r.GetString(5)),
                MaxAge = r.GetInt32(6), Start = PD(r.GetString(7)), End = PD(r.GetString(8)),
            });
        foreach (var r in Rows(con, "SELECT * FROM scout_reports"))
            w.ScoutReports.Add(new ScoutReport
            {
                PlayerId = r.GetInt32(0), Date = PD(r.GetString(1)), StrengthStars = r.GetDouble(2), PotentialStars = r.GetDouble(3),
                Recommendation = r.GetString(4)[0], Summary = r.GetString(5), SummaryRef = PRef(r, 6),
            });
        foreach (var r in Rows(con, "SELECT player_id, value FROM knowledge"))
            w.Knowledge[r.GetInt32(0)] = r.GetInt32(1);
        foreach (var r in Rows(con, "SELECT data FROM staff ORDER BY id"))
            w.Staff.Add(PJ<StaffMember>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT data FROM events ORDER BY id"))
            w.Events.Add(PJ<PendingEvent>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT event_id, date FROM event_history"))
            w.EventHistory[r.GetString(0)] = PD(r.GetString(1));
    }

    // ------------------------------------------------------------------ Hilfen

    private static string J<T>(T value) => JsonSerializer.Serialize(value, Json.Compact);
    private static string? Ref(TextRef? r) => r is null ? null : J(r);
    private static TextRef? PRef(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : PJ<TextRef>(r.GetString(i));
    private static string? NStr(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static T? PJ<T>(string s) => JsonSerializer.Deserialize<T>(s, Json.Compact);
    private static T? Opt<T>(SqliteDataReader r, int i) where T : class => r.IsDBNull(i) ? null : PJ<T>(r.GetString(i));

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    private static DateOnly PD(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", Inv);
    private static int? NInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static SqliteCommand Cmd(SqliteConnection con, SqliteTransaction tx, string sql, params string[] parameters)
    {
        var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter { ParameterName = p });
        cmd.Prepare();
        return cmd;
    }

    private static void Run(SqliteCommand cmd, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            cmd.Parameters[i].Value = values[i] switch
            {
                null => DBNull.Value,
                bool b => b ? 1 : 0,
                var v => v,
            };
        }
        cmd.ExecuteNonQuery();
    }

    private static IEnumerable<SqliteDataReader> Rows(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return r;
    }
}
