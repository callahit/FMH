using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Historie (AP 11b): Abschlussbilanzen der Vereine je Ligasaison und Spielrekorde je Verein und Wettbewerb.</summary>
public sealed class HistorySection : SaveSection
{
    public override string ModuleId => "history";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabellen per Migration an und beginnen ohne Rekorde.</summary>
    public override int Version => 6;

    private const string Tables = """
        CREATE TABLE club_seasons (ord INTEGER PRIMARY KEY, club_id INTEGER NOT NULL, season_year INTEGER NOT NULL, data TEXT NOT NULL);
        CREATE TABLE match_records (scope TEXT NOT NULL, kind TEXT NOT NULL, data TEXT NOT NULL, PRIMARY KEY (scope, kind));
        """;

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cs = Insert(con, tx, "club_seasons", 4);
        for (var i = 0; i < w.ClubSeasons.Count; i++)
            Run(cs, i, w.ClubSeasons[i].ClubId, w.ClubSeasons[i].SeasonYear, J(w.ClubSeasons[i]));
        using var mr = Insert(con, tx, "match_records", 3);
        foreach (var r in w.Records.Values)
            Run(mr, r.Scope, r.Kind, J(r));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM club_seasons ORDER BY ord"))
            w.ClubSeasons.Add(PJ<ClubSeason>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT data FROM match_records ORDER BY rowid"))
        {
            var record = PJ<MatchRecord>(r.GetString(0))!;
            w.Records[MatchRecord.Key(record.Scope, record.Kind)] = record;
        }
    }
}
