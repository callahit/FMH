using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Lizenz: Europapokal-Teilnehmer und Koeffizienten (die Lizenzakte steht beim Verein).</summary>
public sealed class LicenseSection : SaveSection
{
    public override string ModuleId => "license";
    /// <summary>Version 6: Länder- und Vereinskoeffizienten (AP 5). Version 7: Fünfjahresfenster (Grundwerte, Zugewinne je Saison).</summary>
    public override int Version => 7;

    private const string WindowTables = """
        CREATE TABLE coefficient_base (kind TEXT NOT NULL, key TEXT NOT NULL, points REAL NOT NULL);
        CREATE TABLE coefficient_seasons (season_year INTEGER NOT NULL, kind TEXT NOT NULL, key TEXT NOT NULL, points REAL NOT NULL);
        """;

    protected override string Schema => """
        CREATE TABLE european_entries (club_id INTEGER NOT NULL, competition TEXT NOT NULL, season_year INTEGER NOT NULL,
            stage INTEGER NOT NULL, eliminated INTEGER NOT NULL, replacement INTEGER NOT NULL);
        CREATE TABLE country_coefficients (code TEXT PRIMARY KEY, points REAL NOT NULL);
        CREATE TABLE club_coefficients (club_id INTEGER PRIMARY KEY, points REAL NOT NULL);
        """ + WindowTables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5)
        {
            Exec(c, tx, "CREATE TABLE IF NOT EXISTS country_coefficients (code TEXT PRIMARY KEY, points REAL NOT NULL);");
            Exec(c, tx, "CREATE TABLE IF NOT EXISTS club_coefficients (club_id INTEGER PRIMARY KEY, points REAL NOT NULL);");
        }
        // Version 7: leeres Fenster; beim nächsten Saisonstart zählt alles Bisherige als Grundwert.
        if (fromVersion <= 6) Exec(c, tx, WindowTables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "european_entries", 6);
        foreach (var e in w.EuropeanEntries) Run(cmd, e.ClubId, e.CompetitionId, e.SeasonYear, e.Stage, e.Eliminated, e.Replacement);
        using var cc = Insert(con, tx, "country_coefficients", 2);
        foreach (var (code, pts) in w.CountryCoefficients) Run(cc, code, pts);
        using var cl = Insert(con, tx, "club_coefficients", 2);
        foreach (var (id, pts) in w.ClubCoefficients) Run(cl, id, pts);
        var window = w.Coefficients;
        using var cb = Insert(con, tx, "coefficient_base", 3);
        foreach (var (code, pts) in window.CountryBase) Run(cb, "country", code, pts);
        foreach (var (id, pts) in window.ClubBase) Run(cb, "club", id.ToString(System.Globalization.CultureInfo.InvariantCulture), pts);
        using var cs = Insert(con, tx, "coefficient_seasons", 4);
        foreach (var season in window.Seasons)
        {
            foreach (var (code, pts) in season.Countries) Run(cs, season.SeasonYear, "country", code, pts);
            foreach (var (id, pts) in season.Clubs) Run(cs, season.SeasonYear, "club", id.ToString(System.Globalization.CultureInfo.InvariantCulture), pts);
            // Leere Saison trotzdem merken, damit das Fenster weiß, welche Saison schon abgeschlossen ist.
            if (season.Countries.Count == 0 && season.Clubs.Count == 0) Run(cs, season.SeasonYear, "none", "", 0.0);
        }
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
        var window = w.Coefficients;
        foreach (var r in Rows(con, "SELECT kind, key, points FROM coefficient_base"))
            if (r.GetString(0) == "country") window.CountryBase[r.GetString(1)] = r.GetDouble(2);
            else window.ClubBase[int.Parse(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture)] = r.GetDouble(2);
        foreach (var r in Rows(con, "SELECT season_year, kind, key, points FROM coefficient_seasons ORDER BY season_year"))
        {
            var year = r.GetInt32(0);
            if (window.Seasons.Count == 0 || window.Seasons[^1].SeasonYear != year) window.Seasons.Add(new CoefficientSeason { SeasonYear = year });
            var season = window.Seasons[^1];
            switch (r.GetString(1))
            {
                case "country": season.Countries[r.GetString(2)] = r.GetDouble(3); break;
                case "club": season.Clubs[int.Parse(r.GetString(2), System.Globalization.CultureInfo.InvariantCulture)] = r.GetDouble(3); break;
            }
        }
    }
}
