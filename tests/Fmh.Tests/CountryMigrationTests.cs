using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;
using Microsoft.Data.Sqlite;

namespace Fmh.Tests;

/// <summary>
/// Ältere Spielstände spielen mit den aktuellen Wirtschaftsregeln (Review F-10): Beim Laden ersetzt die Sitzung die
/// gespeicherten Finanz-, Kader- und Nachwuchsregeln durch die der Daten.
/// </summary>
public class CountryMigrationTests
{
    /// <summary>
    /// Spielstand mit veralteter Länderkonfiguration (ohne Betriebskosten, Sponsorenniveau 1,0 wie vor der Kalibrierung):
    /// nach dem Laden und einer Saison dieselbe Finanzlage (±5 %) wie ein neues Spiel mit gleichem Seed.
    /// </summary>
    [Fact]
    public void OutdatedCountryRulesAreRaisedToTheCurrentData()
    {
        const ulong seed = 77;
        var fresh = TestData.NewSession(seed);
        var old = TestData.NewSession(seed);
        var de = OwnCopy(old.World, "DE");
        foreach (var div in de.Finance.Divisions.Values) { div.OperatingCostShare = 0; div.SponsorLevel = 1.0; }
        old.SyncRng();
        var path = Path.Combine(Path.GetTempPath(), $"fmh-f10-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(old.World, path);
            var loaded = new GameSession(SqliteStore.Load(path), TestData.Data, new TextMatchEngine());
            Assert.Equal(TestData.Data.Countries.Single(c => c.Code == "DE").Finance.Divisions["DE1"].OperatingCostShare,
                loaded.World.Country("DE").Finance.Divisions["DE1"].OperatingCostShare);

            double Balance(GameSession s)
            {
                var year = s.World.SeasonYear;
                TestData.PlayUntil(s, _ => s.World.SeasonYear > year, 50_000);
                return s.World.League("DE1").ClubIds.Concat(s.World.League("DE2").ClubIds).Sum(id => (double)s.World.Clubs[id].Budget.Balance);
            }
            var expected = Balance(fresh);
            var actual = Balance(loaded);
            Assert.InRange(actual / expected, 0.95, 1.05);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Unveränderte Regeln bleiben unangetastet; geänderte Länder werden gemeldet.</summary>
    [Fact]
    public void RefreshReportsOnlyChangedCountries()
    {
        var s = TestData.NewSession(78);
        Assert.Empty(CountryDataMigration.Refresh(s.World, TestData.Data));
        var de = OwnCopy(s.World, "DE");
        Assert.Empty(CountryDataMigration.Refresh(s.World, TestData.Data));
        de.Squad.MaxSquad = 40;
        Assert.Equal(new[] { "DE" }, CountryDataMigration.Refresh(s.World, TestData.Data));
        Assert.Equal(TestData.Data.Countries.Single(c => c.Code == "DE").Squad.MaxSquad, de.Squad.MaxSquad);
    }

    /// <summary>Eigene Kopie der Länderregeln im Spielstand, wie nach dem Laden (die Testdaten teilen sich sonst die Objekte).</summary>
    private static CountryConfig OwnCopy(Fmh.Core.Model.World w, string code)
    {
        var i = w.Countries.FindIndex(c => c.Code == code);
        w.Countries[i] = System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(System.Text.Json.JsonSerializer.Serialize(w.Countries[i], Json.Compact), Json.Compact)!;
        return w.Countries[i];
    }
}
