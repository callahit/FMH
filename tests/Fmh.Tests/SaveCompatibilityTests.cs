using System.IO.Compression;
using Fmh.Core.Persistence;

namespace Fmh.Tests;

/// <summary>
/// Alte Spielstände müssen laden (Umsetzungskonzept 4.4): v5.fmh.gz ist ein Spielstand der globalen Schemaversion 5
/// (Seed 5150, dritter Spieltag), erzeugt vor der Modularisierung.
/// </summary>
public class SaveCompatibilityTests
{
    [Fact]
    public void VersionFiveSaveStillLoadsAndPlays()
    {
        var source = Path.Combine(TestData.RepoRoot(), "tests", "Fmh.Tests", "Saves", "v5.fmh.gz");
        var path = Path.Combine(Path.GetTempPath(), $"fmh-v5-{Guid.NewGuid():N}.fmh");
        try
        {
            using (var input = new GZipStream(File.OpenRead(source), CompressionMode.Decompress))
            using (var output = File.Create(path))
                input.CopyTo(output);
            var world = SqliteStore.Load(path);
            Assert.Equal(5150UL, world.Seed);
            Assert.NotEmpty(world.Players);
            var s = new Fmh.Core.Game.GameSession(world, TestData.Data, new Fmh.MatchEngine.TextMatchEngine());
            Assert.True(world.LeagueOf(s.HumanClub).CurrentMatchday >= 3);
            var day = world.Date;
            TestData.PlayUntil(s, _ => s.World.Date >= day.AddDays(14));
            Assert.True(s.World.Date >= day.AddDays(14));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
