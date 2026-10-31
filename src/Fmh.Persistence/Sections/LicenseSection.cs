using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Lizenz: Europapokal-Teilnehmer und Koeffizienten (die Lizenzakte steht beim Verein).</summary>
public sealed class LicenseSection : SaveSection
{
    public override string ModuleId => "license";
    /// <summary>Version 6: Länder- und Vereinskoeffizienten (AP 5).</summary>
    public override int Version => 6;

    protected override string Schema => """
        CREATE TABLE european_entries (club_id INTEGER NOT NULL, competition TEXT NOT NULL, season_year INTEGER NOT NULL,
            stage INTEGER NOT NULL, eliminated INTEGER NOT NULL, replacement INTEGER NOT NULL);
        CREATE TABLE country_coefficients (code TEXT PRIMARY KEY, points REAL NOT NULL);
        CREATE TABLE club_coefficients (club_id INTEGER PRIMARY KEY, points REAL NOT NULL);
        """;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5)
        {
            Exec(c, tx, "CREATE TABLE IF NOT EXISTS country_coefficients (code TEXT PRIMARY KEY, points REAL NOT NULL);");
            Exec(c, tx, "CREATE TABLE IF NOT EXISTS club_coefficients (club_id INTEGER PRIMARY KEY, points REAL NOT NULL);");
        }
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "european_entries", 6);
        foreach (var e in w.EuropeanEntries) Run(cmd, e.ClubId, e.CompetitionId, e.SeasonYear, e.Stage, e.Eliminated, e.Replacement);
        using var cc = Insert(con, tx, "country_coefficients", 2);
        foreach (var (code, pts) in w.CountryCoefficients) Run(cc, code, pts);
        using var cl = Insert(con, tx, "club_coefficients", 2);
        foreach (var (id, pts) in w.ClubCoefficients) Run(cl, id, pts);
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT club_id, competition, season_year, stage, eliminated, replacement FROM european_entries"))
            w.EuropeanEntries.Add(new EuropeanEntry
            {
                ClubId = r.GetInt32(0), CompetitionId = r.GetString(1), SeasonYear = r.GetInt32(2), Stage = r.GetInt32(3),
                Eliminated = r.GetInt32(4) == 1, Replacement = r.GetInt32(5) == 1,
            });
        foreach (var r in Rows(con, "SELECT code, points FROM country_coefficients"))
            w.CountryCoefficients[r.GetString(0)] = r.GetDouble(1);
        foreach (var r in Rows(con, "SELECT club_id, points FROM club_coefficients"))
            w.ClubCoefficients[r.GetInt32(0)] = r.GetDouble(1);
    }
}
