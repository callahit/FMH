using System.IO.Compression;
using Fmh.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Fmh.Tests;

/// <summary>
/// Alte Spielstände müssen laden (Umsetzungskonzept 4.4): v5.fmh.gz ist ein Spielstand der globalen Schemaversion 5
/// (Seed 5150, dritter Spieltag), erzeugt vor der Modularisierung. Seit AP 1 führt jeder Speicherabschnitt eine eigene Version.
/// </summary>
public class SaveCompatibilityTests
{
    private static string Fixture()
    {
        var source = Path.Combine(TestData.RepoRoot(), "tests", "Fmh.Tests", "Saves", "v5.fmh.gz");
        var path = Path.Combine(Path.GetTempPath(), $"fmh-v5-{Guid.NewGuid():N}.fmh");
        using var input = new GZipStream(File.OpenRead(source), CompressionMode.Decompress);
        using var output = File.Create(path);
        input.CopyTo(output);
        return path;
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }

    private static void Exec(string path, string sql)
    {
        using var con = new SqliteConnection($"Data Source={path};Pooling=False");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void VersionFiveSaveStillLoadsAndPlays()
    {
        var path = Fixture();
        try
        {
            var world = SqliteStore.Load(path);
            Assert.Equal(5150UL, world.Seed);
            Assert.NotEmpty(world.Players);
            var s = new Fmh.Core.Game.GameSession(world, TestData.Data, new Fmh.MatchEngine.TextMatchEngine());
            Assert.True(world.LeagueOf(s.HumanClub).CurrentMatchday >= 3);
            var day = world.Date;
            TestData.PlayUntil(s, _ => s.World.Date >= day.AddDays(14));
            Assert.True(s.World.Date >= day.AddDays(14));

            // Nach dem Laden stehen alle Abschnitte mit ihrer Version im Spielstand.
            var meta = SqliteStore.ReadMetaTable(path);
            foreach (var section in SqliteStore.Sections)
                Assert.Equal(section.Version.ToString(), meta["schema." + section.ModuleId]);
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>Die Abschnitte legen genau das Schema an, das ein migrierter Spielstand der Version 5 hat (Spalten, Reihenfolge, Indizes).</summary>
    [Fact]
    public void SectionSchemaMatchesMigratedVersionFive()
    {
        string Describe(SqliteConnection con)
        {
            var lines = new List<string>();
            var tables = new List<string>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT type, name, tbl_name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lines.Add($"{r.GetString(0)} {r.GetString(1)} on {r.GetString(2)}");
                    if (r.GetString(0) == "table") tables.Add(r.GetString(1));
                }
            }
            foreach (var t in tables)
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({t})";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    lines.Add($"{t}.{r.GetInt32(0)} {r.GetString(1)} {r.GetString(2)} nn={r.GetInt32(3)} def={(r.IsDBNull(4) ? "-" : r.GetString(4))} pk={r.GetInt32(5)}");
            }
            return string.Join("\n", lines);
        }

        using var legacy = new SqliteConnection("Data Source=:memory:");
        legacy.Open();
        using (var cmd = legacy.CreateCommand())
        {
            cmd.CommandText = LegacySchema.Schema(SqliteStore.SchemaVersion);
            cmd.ExecuteNonQuery();
        }
        using var sections = new SqliteConnection("Data Source=:memory:");
        sections.Open();
        using (var tx = sections.BeginTransaction())
        {
            using (var cmd = sections.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);";
                cmd.ExecuteNonQuery();
            }
            foreach (var s in SqliteStore.Sections) s.CreateSchema(sections, tx);
            tx.Commit();
        }
        Assert.Equal(Describe(legacy), Describe(sections));
    }

    [Fact]
    public void NewerSectionOrUnknownSectionIsRejected()
    {
        var path = Fixture();
        try
        {
            SqliteStore.Load(path);
            Exec(path, "UPDATE meta SET value = '99' WHERE key = 'schema.finance'");
            Assert.Throws<InvalidDataException>(() => SqliteStore.Load(path));
            Exec(path, "UPDATE meta SET value = '5' WHERE key = 'schema.finance'");
            Exec(path, "INSERT INTO meta (key, value) VALUES ('schema.future', '6')");
            Assert.Throws<InvalidDataException>(() => SqliteStore.Load(path));
        }
        finally
        {
            Cleanup(path);
        }
    }
}
