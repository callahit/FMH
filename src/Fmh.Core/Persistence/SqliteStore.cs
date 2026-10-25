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
    public const int SchemaVersion = 1;

    /// <summary>Migrationsschritte: Schlüssel = Ausgangsversion; das SQL hebt die Datenbank um eine Version an.</summary>
    private static readonly Dictionary<int, string> Migrations = new();

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string AttrCol(Attr a) => "a_" + a;

    private static readonly string[] HiddenCols =
    {
        "current_ability", "potential_ability", "ambition", "professionalism", "loyalty", "pressure", "temperament",
        "sportsmanship", "adaptability", "controversy", "consistency", "injury_proneness", "important_matches",
    };

    private static string CreateSchema() => $"""
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

        using (var cmd = Cmd(con, tx, "INSERT INTO managers VALUES ($a, $b, $c, $d, $e, $f)", "$a", "$b", "$c", "$d", "$e", "$f"))
            foreach (var m in w.Managers) Run(cmd, m.Id, m.Name, m.Human, m.ClubId, m.Reputation, JsonSerializer.Serialize(m.Profile, Json.Compact));

        using (var cmd = Cmd(con, tx, "INSERT INTO clubs VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n)",
                   "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h", "$i", "$j", "$k", "$l", "$m", "$n"))
            foreach (var c in w.Clubs.Values)
                Run(cmd, c.Id, c.Key, c.Name, c.ShortName, c.City, c.CountryCode, c.Reputation, c.PrimaryColor, c.SecondaryColor,
                    c.StadiumName, c.StadiumCapacity, c.LeagueId, c.ManagerId, JsonSerializer.Serialize(c.Tactic, Json.Compact));

        WritePlayers(con, tx, w);

        using (var cmd = Cmd(con, tx, "INSERT INTO leagues VALUES ($a,$b,$c,$d,$e,$f)", "$a", "$b", "$c", "$d", "$e", "$f"))
            for (var i = 0; i < w.Leagues.Count; i++)
            {
                var l = w.Leagues[i];
                Run(cmd, l.Id, i, l.Name, l.CountryCode, l.Level, JsonSerializer.Serialize(l.Format, Json.Compact));
            }
        using (var cmd = Cmd(con, tx, "INSERT INTO league_clubs VALUES ($a,$b,$c)", "$a", "$b", "$c"))
            foreach (var l in w.Leagues)
                for (var i = 0; i < l.ClubIds.Count; i++) Run(cmd, l.Id, l.ClubIds[i], i);

        using (var fx = Cmd(con, tx, "INSERT INTO fixtures VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n,$o,$p,$q,$r,$s,$t)",
                   "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h", "$i", "$j", "$k", "$l", "$m", "$n", "$o", "$p", "$q", "$r", "$s", "$t"))
        using (var gl = Cmd(con, tx, "INSERT INTO goals VALUES ($a,$b,$c,$d,$e,$f,$g,$h)", "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h"))
        using (var cd = Cmd(con, tx, "INSERT INTO cards VALUES ($a,$b,$c,$d,$e,$f)", "$a", "$b", "$c", "$d", "$e", "$f"))
        {
            foreach (var f in w.Leagues.SelectMany(l => l.Fixtures))
            {
                var r = f.Result;
                Run(fx, f.Id, f.LeagueId, f.Matchday, D(f.Date), f.HomeId, f.AwayId, f.RefereeId, r is not null,
                    r?.HomeGoals, r?.AwayGoals, r?.HalfTimeHome, r?.HalfTimeAway, r?.Attendance, r?.Weather.ToString(),
                    r?.HomeXg, r?.AwayXg, r?.HomePossession, r?.HomeShots, r?.AwayShots, r?.PlayerOfTheMatchId);
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

        using (var cmd = Cmd(con, tx, "INSERT INTO messages VALUES ($a,$b,$c,$d,$e,$f,$g,$h)", "$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h"))
            foreach (var m in w.Messages) Run(cmd, m.Id, m.ManagerId, D(m.Date), m.Type.ToString(), m.Sender, m.Subject, m.Body, m.Read);

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

        foreach (var r in Rows(con, "SELECT id, name, human, club_id, reputation, profile FROM managers ORDER BY id"))
            w.Managers.Add(new Manager
            {
                Id = r.GetInt32(0), Name = r.GetString(1), Human = r.GetInt32(2) == 1, ClubId = NInt(r, 3), Reputation = r.GetInt32(4),
                Profile = JsonSerializer.Deserialize<ManagerProfile>(r.GetString(5), Json.Compact) ?? new ManagerProfile(),
            });

        foreach (var r in Rows(con, "SELECT * FROM clubs ORDER BY id"))
        {
            var c = new Club
            {
                Id = r.GetInt32(0), Key = r.GetString(1), Name = r.GetString(2), ShortName = r.GetString(3), City = r.GetString(4),
                CountryCode = r.GetString(5), Reputation = r.GetInt32(6), PrimaryColor = r.GetString(7), SecondaryColor = r.GetString(8),
                StadiumName = r.GetString(9), StadiumCapacity = r.GetInt32(10), LeagueId = r.GetString(11), ManagerId = NInt(r, 12),
                Tactic = JsonSerializer.Deserialize<Tactic>(r.GetString(13), Json.Compact) ?? new Tactic(),
            };
            w.Clubs[c.Id] = c;
        }

        ReadPlayers(con, w);
        foreach (var club in w.Clubs.Values)
            club.PlayerIds = w.Players.Values.Where(p => p.ClubId == club.Id).Select(p => p.Id).ToList();
        // Kaderreihenfolge aus der Spieler-Id rekonstruieren (Spieler werden in Kaderreihenfolge vergeben).
        foreach (var club in w.Clubs.Values) club.PlayerIds.Sort();

        foreach (var r in Rows(con, "SELECT id, name, country, level, format FROM leagues ORDER BY ord"))
            w.Leagues.Add(new League
            {
                Id = r.GetString(0), Name = r.GetString(1), CountryCode = r.GetString(2), Level = r.GetInt32(3),
                Format = JsonSerializer.Deserialize<DivisionConfig>(r.GetString(4), Json.Compact)!,
            });
        var leagues = w.Leagues.ToDictionary(l => l.Id);
        foreach (var r in Rows(con, "SELECT league_id, club_id FROM league_clubs ORDER BY league_id, ord"))
            leagues[r.GetString(0)].ClubIds.Add(r.GetInt32(1));

        var fixtures = new Dictionary<int, Fixture>();
        foreach (var r in Rows(con, "SELECT * FROM fixtures ORDER BY id"))
        {
            var f = new Fixture
            {
                Id = r.GetInt32(0), LeagueId = r.GetString(1), Matchday = r.GetInt32(2), Date = PD(r.GetString(3)),
                HomeId = r.GetInt32(4), AwayId = r.GetInt32(5), RefereeId = r.GetInt32(6),
            };
            if (r.GetInt32(7) == 1)
            {
                f.Result = new MatchResult
                {
                    HomeGoals = r.GetInt32(8), AwayGoals = r.GetInt32(9), HalfTimeHome = r.GetInt32(10), HalfTimeAway = r.GetInt32(11),
                    Attendance = r.GetInt32(12), Weather = Enum.Parse<Weather>(r.GetString(13)), HomeXg = r.GetDouble(14),
                    AwayXg = r.GetDouble(15), HomePossession = r.GetInt32(16), HomeShots = r.GetInt32(17), AwayShots = r.GetInt32(18),
                    PlayerOfTheMatchId = NInt(r, 19),
                };
            }
            fixtures[f.Id] = f;
            leagues[f.LeagueId].Fixtures.Add(f);
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
            });

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

    // ------------------------------------------------------------------ Hilfen

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
