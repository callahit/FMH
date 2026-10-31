using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Spielbetrieb: alle Spiele aus Liga, Pokal und Relegation mit Ergebnis, Toren und Karten.</summary>
public sealed class MatchdaySection : SaveSection
{
    public override string ModuleId => "matchday";

    /// <summary>Version 6: Phasennummer. Version 7: Fremdschlüssel an Spielen, Toren und Karten (Must-now E6-S1).</summary>
    public override int Version => 7;

    /// <summary>5 → 6: Spalte phase. 6 → 7: Fremdschlüssel.</summary>
    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, "ALTER TABLE fixtures ADD COLUMN phase INTEGER NOT NULL DEFAULT 0");
        if (fromVersion <= 6)
        {
            Exec(c, tx, """
                CREATE TABLE fixtures_v7 (id INTEGER PRIMARY KEY, competition_id TEXT NOT NULL, matchday INTEGER NOT NULL, date TEXT NOT NULL,
                    home_id INTEGER NOT NULL REFERENCES clubs(id), away_id INTEGER NOT NULL REFERENCES clubs(id), referee_id INTEGER NOT NULL,
                    played INTEGER NOT NULL, home_goals INTEGER, away_goals INTEGER, ht_home INTEGER, ht_away INTEGER, attendance INTEGER,
                    weather TEXT, home_xg REAL, away_xg REAL, home_possession INTEGER, home_shots INTEGER, away_shots INTEGER, potm INTEGER,
                    kind TEXT NOT NULL DEFAULT 'League', tie_id INTEGER, leg INTEGER NOT NULL DEFAULT 0, extra_time INTEGER NOT NULL DEFAULT 0,
                    pens_home INTEGER, pens_away INTEGER, phase INTEGER NOT NULL DEFAULT 0);
                INSERT INTO fixtures_v7 SELECT * FROM fixtures;
                DROP TABLE fixtures; ALTER TABLE fixtures_v7 RENAME TO fixtures;
                CREATE INDEX fixtures_league ON fixtures (competition_id, matchday);
                CREATE TABLE goals_v7 (fixture_id INTEGER NOT NULL REFERENCES fixtures(id), ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
                    scorer_id INTEGER NOT NULL REFERENCES players(id), assist_id INTEGER REFERENCES players(id), penalty INTEGER NOT NULL, own_goal INTEGER NOT NULL);
                INSERT INTO goals_v7 SELECT * FROM goals;
                DROP TABLE goals; ALTER TABLE goals_v7 RENAME TO goals;
                CREATE TABLE cards_v7 (fixture_id INTEGER NOT NULL REFERENCES fixtures(id), ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
                    player_id INTEGER NOT NULL REFERENCES players(id), red INTEGER NOT NULL);
                INSERT INTO cards_v7 SELECT * FROM cards;
                DROP TABLE cards; ALTER TABLE cards_v7 RENAME TO cards;
                """);
        }
    }

    protected override string Schema => """
        CREATE TABLE fixtures (id INTEGER PRIMARY KEY, competition_id TEXT NOT NULL, matchday INTEGER NOT NULL, date TEXT NOT NULL,
            home_id INTEGER NOT NULL REFERENCES clubs(id), away_id INTEGER NOT NULL REFERENCES clubs(id), referee_id INTEGER NOT NULL,
            played INTEGER NOT NULL, home_goals INTEGER, away_goals INTEGER, ht_home INTEGER, ht_away INTEGER, attendance INTEGER,
            weather TEXT, home_xg REAL, away_xg REAL, home_possession INTEGER, home_shots INTEGER, away_shots INTEGER, potm INTEGER,
            kind TEXT NOT NULL DEFAULT 'League', tie_id INTEGER, leg INTEGER NOT NULL DEFAULT 0, extra_time INTEGER NOT NULL DEFAULT 0,
            pens_home INTEGER, pens_away INTEGER, phase INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX fixtures_league ON fixtures (competition_id, matchday);
        CREATE TABLE goals (fixture_id INTEGER NOT NULL REFERENCES fixtures(id), ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
            scorer_id INTEGER NOT NULL REFERENCES players(id), assist_id INTEGER REFERENCES players(id), penalty INTEGER NOT NULL, own_goal INTEGER NOT NULL);
        CREATE TABLE cards (fixture_id INTEGER NOT NULL REFERENCES fixtures(id), ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
            player_id INTEGER NOT NULL REFERENCES players(id), red INTEGER NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var fx = Insert(con, tx, "fixtures", 27);
        using var gl = Insert(con, tx, "goals", 8);
        using var cd = Insert(con, tx, "cards", 6);
        foreach (var f in w.AllFixtures)
        {
            var r = f.Result;
            Run(fx, f.Id, f.CompetitionId, f.Matchday, D(f.Date), f.HomeId, f.AwayId, f.RefereeId, r is not null,
                r?.HomeGoals, r?.AwayGoals, r?.HalfTimeHome, r?.HalfTimeAway, r?.Attendance, r?.Weather.ToString(),
                r?.HomeXg, r?.AwayXg, r?.HomePossession, r?.HomeShots, r?.AwayShots, r?.PlayerOfTheMatchId,
                f.Kind.ToString(), f.TieId, f.Leg, r?.ExtraTime ?? false, r?.PenaltiesHome, r?.PenaltiesAway, f.Phase);
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

    /// <summary>Braucht geladene Ligen und Pokale: jedes Spiel wird seinem Wettbewerb zugeordnet.</summary>
    public override void Load(SqliteConnection con, World w)
    {
        var leagues = w.Leagues.ToDictionary(l => l.Id);
        var cups = w.Cups.ToDictionary(c => c.Id);
        var fixtures = new Dictionary<int, Fixture>();
        foreach (var r in Rows(con, "SELECT * FROM fixtures ORDER BY id"))
        {
            var f = new Fixture
            {
                Id = r.GetInt32(0), CompetitionId = r.GetString(1), Matchday = r.GetInt32(2), Date = PD(r.GetString(3)),
                HomeId = r.GetInt32(4), AwayId = r.GetInt32(5), RefereeId = r.GetInt32(6),
                Kind = Enum.Parse<CompetitionKind>(r.GetString(20)), TieId = NInt(r, 21), Leg = r.GetInt32(22),
                Phase = r.FieldCount > 26 && !r.IsDBNull(26) ? r.GetInt32(26) : 0,
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
    }
}
