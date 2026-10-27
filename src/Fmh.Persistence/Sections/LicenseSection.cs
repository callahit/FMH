using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Lizenz: Teilnehmer der Europapokale (die Lizenzakte steht beim Verein).</summary>
public sealed class LicenseSection : SaveSection
{
    public override string ModuleId => "license";

    protected override string Schema => """
        CREATE TABLE european_entries (club_id INTEGER NOT NULL, competition TEXT NOT NULL, season_year INTEGER NOT NULL,
            stage INTEGER NOT NULL, eliminated INTEGER NOT NULL, replacement INTEGER NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "european_entries", 6);
        foreach (var e in w.EuropeanEntries) Run(cmd, e.ClubId, e.CompetitionId, e.SeasonYear, e.Stage, e.Eliminated, e.Replacement);
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT club_id, competition, season_year, stage, eliminated, replacement FROM european_entries"))
            w.EuropeanEntries.Add(new EuropeanEntry
            {
                ClubId = r.GetInt32(0), CompetitionId = r.GetString(1), SeasonYear = r.GetInt32(2), Stage = r.GetInt32(3),
                Eliminated = r.GetInt32(4) == 1, Replacement = r.GetInt32(5) == 1,
            });
    }
}
