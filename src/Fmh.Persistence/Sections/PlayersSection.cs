using System.Text.Json;
using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Kern: Spieler mit Attributen als Spalten (für Editor und SQL-Auswertungen), Positionen und Saisonstatistik.</summary>
public sealed class PlayersSection : SaveSection
{
    public override string ModuleId => "players";

    /// <summary>Version 6: Meldeliste. Version 7: FK Positionen/Saison. Version 8: Nationalitäten-Tabelle (E6-S3).</summary>
    public override int Version => 8;

    /// <summary>
    /// 5 → 6: Meldeliste. 6 → 7: FK Kindtabellen. 7 → 8: <c>player_nationalities</c> aus der Kommaliste.
    /// </summary>
    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, "ALTER TABLE players ADD COLUMN registration TEXT");
        if (fromVersion <= 6)
        {
            Exec(c, tx, """
                CREATE TABLE player_positions_v7 (player_id INTEGER NOT NULL REFERENCES players(id), position TEXT NOT NULL, suitability TEXT NOT NULL,
                    PRIMARY KEY (player_id, position));
                INSERT INTO player_positions_v7 SELECT player_id, position, suitability FROM player_positions;
                DROP TABLE player_positions;
                ALTER TABLE player_positions_v7 RENAME TO player_positions;
                CREATE TABLE player_seasons_v7 (player_id INTEGER NOT NULL REFERENCES players(id), current INTEGER NOT NULL, ord INTEGER NOT NULL,
                    season_year INTEGER NOT NULL, club_id INTEGER, apps INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL,
                    assists INTEGER NOT NULL, yellow INTEGER NOT NULL, red INTEGER NOT NULL, graded INTEGER NOT NULL, grade_sum REAL NOT NULL);
                INSERT INTO player_seasons_v7 SELECT player_id, current, ord, season_year, club_id, apps, minutes, goals, assists, yellow, red, graded, grade_sum FROM player_seasons;
                DROP TABLE player_seasons;
                ALTER TABLE player_seasons_v7 RENAME TO player_seasons;
                CREATE INDEX player_seasons_player ON player_seasons (player_id);
                """);
        }
        if (fromVersion <= 7)
        {
            // IF NOT EXISTS: Altspeicher-Tests setzen die Schemaversion zurück, ohne Kindtabellen zu löschen.
            Exec(c, tx, """
                CREATE TABLE IF NOT EXISTS player_nationalities (player_id INTEGER NOT NULL REFERENCES players(id), ord INTEGER NOT NULL, code TEXT NOT NULL,
                    PRIMARY KEY (player_id, ord));
                """);
            var empty = true;
            foreach (var _ in Rows(c, "SELECT 1 FROM player_nationalities LIMIT 1")) { empty = false; break; }
            if (empty)
            {
                foreach (var r in Rows(c, "SELECT id, nationalities FROM players"))
                {
                    var id = r.GetInt32(0);
                    var codes = r.IsDBNull(1) ? Array.Empty<string>() : r.GetString(1).Split(',', StringSplitOptions.RemoveEmptyEntries);
                    for (var i = 0; i < codes.Length; i++)
                        Exec(c, tx, "INSERT INTO player_nationalities (player_id, ord, code) VALUES (" + id + ", " + i + ", '" + codes[i].Replace("'", "''") + "')");
                }
            }
        }
    }

    internal static string AttrCol(Attr a) => "a_" + a;

    internal static readonly string[] HiddenCols =
    {
        "current_ability", "potential_ability", "ambition", "professionalism", "loyalty", "pressure", "temperament",
        "sportsmanship", "adaptability", "controversy", "consistency", "injury_proneness", "important_matches",
    };

    protected override string Schema => $"""
        CREATE TABLE players (id INTEGER PRIMARY KEY, first_name TEXT NOT NULL, last_name TEXT NOT NULL, birth_date TEXT NOT NULL,
            nationalities TEXT NOT NULL, height INTEGER NOT NULL, weight INTEGER NOT NULL, left_foot INTEGER NOT NULL,
            right_foot INTEGER NOT NULL, main_position TEXT NOT NULL, club_id INTEGER, reputation INTEGER NOT NULL,
            contract_wage INTEGER, contract_until TEXT,
            {string.Join(", ", AttrInfo.All.Select(a => AttrCol(a) + " INTEGER NOT NULL"))},
            {string.Join(", ", HiddenCols.Select(c => c + " INTEGER NOT NULL"))},
            condition REAL NOT NULL, match_practice REAL NOT NULL, morale TEXT NOT NULL, recent_grades TEXT NOT NULL,
            injury_type TEXT, injury_days INTEGER, injury_total INTEGER, suspended INTEGER NOT NULL, yellow_acc INTEGER NOT NULL,
            contract TEXT, is_youth INTEGER NOT NULL DEFAULT 0, retired INTEGER NOT NULL DEFAULT 0, transfer_listed INTEGER NOT NULL DEFAULT 0,
            asking_price INTEGER, transfer TEXT, loan TEXT, training TEXT, pre_contract TEXT, registration TEXT);
        CREATE INDEX players_club ON players (club_id);
        CREATE TABLE player_positions (player_id INTEGER NOT NULL REFERENCES players(id), position TEXT NOT NULL, suitability TEXT NOT NULL,
            PRIMARY KEY (player_id, position));
        CREATE TABLE player_seasons (player_id INTEGER NOT NULL REFERENCES players(id), current INTEGER NOT NULL, ord INTEGER NOT NULL,
            season_year INTEGER NOT NULL, club_id INTEGER, apps INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL,
            assists INTEGER NOT NULL, yellow INTEGER NOT NULL, red INTEGER NOT NULL, graded INTEGER NOT NULL, grade_sum REAL NOT NULL);
        CREATE INDEX player_seasons_player ON player_seasons (player_id);
        CREATE TABLE player_nationalities (player_id INTEGER NOT NULL REFERENCES players(id), ord INTEGER NOT NULL, code TEXT NOT NULL,
            PRIMARY KEY (player_id, ord));
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        var cols = new List<string>
        {
            "id", "first_name", "last_name", "birth_date", "nationalities", "height", "weight", "left_foot", "right_foot",
            "main_position", "club_id", "reputation", "contract_wage", "contract_until",
        };
        cols.AddRange(AttrInfo.All.Select(AttrCol));
        cols.AddRange(HiddenCols);
        cols.AddRange(new[] { "condition", "match_practice", "morale", "recent_grades", "injury_type", "injury_days", "injury_total", "suspended", "yellow_acc" });
        cols.AddRange(new[] { "contract", "is_youth", "retired", "transfer_listed", "asking_price", "transfer", "loan", "training", "pre_contract", "registration" });
        var names = cols.Select((_, i) => "$p" + i).ToArray();
        using var cmd = Cmd(con, tx, $"INSERT INTO players ({string.Join(",", cols)}) VALUES ({string.Join(",", names)})", names);
        using var pos = Insert(con, tx, "player_positions", 3);
        using var sea = Insert(con, tx, "player_seasons", 13);
        using var nat = Insert(con, tx, "player_nationalities", 3);

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
                // Must-now E6-S2: laufender Vertrag steht in contracts.terms; Spalte bleibt nur für Altspeicher.
                null, p.IsYouth, p.Retired, p.Transfer.Listed, p.Transfer.AskingPrice,
                J(p.Transfer), p.Loan is null ? null : J(p.Loan), J(p.Training), p.PreContract is null ? null : J(p.PreContract),
                J(p.Registration),
            });
            Run(cmd, values.ToArray());

            foreach (var (position, suit) in p.Positions) Run(pos, p.Id, position.ToString(), suit.ToString());
            for (var i = 0; i < p.Nationalities.Count; i++) Run(nat, p.Id, i, p.Nationalities[i]);

            void Season(PlayerSeasonStats s, bool current, int ord) =>
                Run(sea, p.Id, current, ord, s.SeasonYear, s.ClubId, s.Appearances, s.Minutes, s.Goals, s.Assists,
                    s.YellowCards, s.RedCards, s.GradedMatches, s.GradeSum);
            Season(p.SeasonStats, true, 0);
            for (var i = 0; i < p.History.Count; i++) Season(p.History[i], false, i);
        }
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT * FROM players ORDER BY id"))
        {
            var p = ReadPlayer(r);
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

        try
        {
            var byPlayer = new Dictionary<int, List<string>>();
            foreach (var r in Rows(con, "SELECT player_id, ord, code FROM player_nationalities ORDER BY player_id, ord"))
            {
                var id = r.GetInt32(0);
                if (!byPlayer.TryGetValue(id, out var list)) byPlayer[id] = list = new List<string>();
                list.Add(r.GetString(2));
            }
            foreach (var (id, codes) in byPlayer)
                if (w.Players.TryGetValue(id, out var p) && codes.Count > 0) p.Nationalities = codes;
        }
        catch (SqliteException) { /* ältere Abschnitte ohne Tabelle */ }

        // Must-now E6-S4: Kaderliste aus players.club_id aufbauen; Abweichungen werden überschrieben.
        foreach (var club in w.Clubs.Values)
        {
            var fromClubId = w.Players.Values.Where(p => p.ClubId == club.Id && !p.IsYouth && !p.Retired).Select(p => p.Id).Order().ToList();
            if (club.PlayerIds.Count == 0)
                club.PlayerIds = fromClubId;
            else if (!club.PlayerIds.Order().SequenceEqual(fromClubId))
            {
                // Integrität: club_id ist die Wahrheit; die Kaderliste folgt.
                club.PlayerIds = fromClubId;
            }
        }

        // Must-now E6-S5: Zweite Mannschaft als Team unter dem Hauptverein spiegeln.
        // Spielplan-IDs bleiben die bisherigen Club-/Team-IDs (FirstTeam.Id == Club.Id, Reserve-Id == Team.Id).
        foreach (var reserve in w.Clubs.Values.Where(c => c.ReserveOf is not null))
        {
            if (!w.Clubs.TryGetValue(reserve.ReserveOf!.Value, out var parent)) continue;
            if (parent.Teams.Any(t => t.Id == reserve.Id)) continue;
            var team = reserve.FirstTeam;
            team.Id = reserve.Id;
            team.ClubId = parent.Id;
            parent.Teams.Add(team);
        }
    }

    private static Player ReadPlayer(SqliteDataReader r)
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
        col++;
        p.Registration = col < r.FieldCount && !r.IsDBNull(col) ? PJ<PlayerRegistration>(r.GetString(col)) ?? new() : new PlayerRegistration { ClubId = p.ClubId };
        return p;
    }
}
