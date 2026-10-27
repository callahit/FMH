using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Scouting: Scouts, Aufträge, Berichte und Kenntnisstand je Spieler.</summary>
public sealed class ScoutingSection : SaveSection
{
    public override string ModuleId => "scouting";

    protected override string Schema => """
        CREATE TABLE scouts (id INTEGER PRIMARY KEY, club_id INTEGER NOT NULL, name TEXT NOT NULL, judging_ability INTEGER NOT NULL,
            judging_potential INTEGER NOT NULL, region_knowledge INTEGER NOT NULL, region TEXT NOT NULL);
        CREATE TABLE scout_assignments (id INTEGER PRIMARY KEY, scout_id INTEGER NOT NULL, task TEXT NOT NULL, player_id INTEGER,
            league_id TEXT, position TEXT, max_age INTEGER NOT NULL, start_date TEXT NOT NULL, end_date TEXT NOT NULL);
        CREATE TABLE scout_reports (player_id INTEGER PRIMARY KEY, date TEXT NOT NULL, strength REAL NOT NULL, potential REAL NOT NULL,
            recommendation TEXT NOT NULL, summary TEXT NOT NULL, summary_ref TEXT);
        CREATE TABLE knowledge (player_id INTEGER PRIMARY KEY, value INTEGER NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "scouts", 7))
            foreach (var s in w.Scouts) Run(cmd, s.Id, s.ClubId, s.Name, s.JudgingAbility, s.JudgingPotential, s.RegionKnowledge, s.Region);
        using (var cmd = Insert(con, tx, "scout_assignments", 9))
            foreach (var a in w.ScoutAssignments)
                Run(cmd, a.Id, a.ScoutId, a.Task.ToString(), a.PlayerId, a.LeagueId, a.Position?.ToString(), a.MaxAge, D(a.Start), D(a.End));
        using (var cmd = Insert(con, tx, "scout_reports", 7))
            foreach (var r in w.ScoutReports)
                Run(cmd, r.PlayerId, D(r.Date), r.StrengthStars, r.PotentialStars, r.Recommendation.ToString(), r.SummaryRef?.ToString() ?? r.Summary, Ref(r.SummaryRef));
        using (var cmd = Insert(con, tx, "knowledge", 2))
            foreach (var (id, v) in w.Knowledge) Run(cmd, id, v);
    }

    public override void Load(SqliteConnection con, World w)
    {
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
    }
}
