using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Pokale: Pokal- und Relegationswettbewerbe mit Runden und Duellen (die Spiele stehen im Spielbetrieb).</summary>
public sealed class CupsSection : SaveSection
{
    public override string ModuleId => "cups";

    protected override string Schema => """
        CREATE TABLE cups (id TEXT PRIMARY KEY, ord INTEGER NOT NULL, name TEXT NOT NULL, country TEXT NOT NULL, kind TEXT NOT NULL,
            season_year INTEGER NOT NULL, winner_id INTEGER, data TEXT NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "cups", 8);
        for (var i = 0; i < w.Cups.Count; i++)
        {
            var c = w.Cups[i];
            // Spiele stehen in der Tabelle "fixtures"; im JSON nur Runden und Duelle.
            var fixtures = c.Fixtures;
            c.Fixtures = new List<Fixture>();
            try { Run(cmd, c.Id, i, c.Name, c.CountryCode, c.Kind.ToString(), c.SeasonYear, c.WinnerId, J(c)); }
            finally { c.Fixtures = fixtures; }
        }
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM cups ORDER BY ord"))
            w.Cups.Add(PJ<Cup>(r.GetString(0))!);
    }
}
