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
            // Migration 5 → 6 des Abschnitts "world": jeder Verein hat seine erste Mannschaft.
            Assert.All(world.Clubs.Values, c => Assert.Equal((c.Id, c.Id, Fmh.Core.Model.Team.MenWorld), (c.FirstTeam.Id, c.FirstTeam.ClubId, c.FirstTeam.WorldId)));
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>
    /// Ein neuer Spielstand hat genau das Schema eines migrierten: Version 5 (Altschema) plus die Migrationen jedes Abschnitts
    /// (Spalten, Reihenfolge, Vorgaben, Indizes).
    /// </summary>
    [Fact]
    public void NewSchemaMatchesMigratedVersionFive()
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
        using (var tx = legacy.BeginTransaction())
        {
            foreach (var s in SqliteStore.Sections.Where(s => s.Version > SqliteStore.SchemaVersion))
                s.Migrate(legacy, tx, SqliteStore.SchemaVersion);
            tx.Commit();
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

    /// <summary>Positionen und Saisonzeilen verweisen auf den Spieler; ein Spielstand der Version 5 lässt sich danach wieder speichern.</summary>
    [Fact]
    public void PlayerChildRowsReferenceThePlayerAndOldSavesStillSave()
    {
        var path = Fixture();
        try
        {
            var world = SqliteStore.Load(path);
            SqliteStore.Save(world, path);
            Assert.Equal(world.Players.Count, SqliteStore.Load(path).Players.Count);
            using var con = new SqliteConnection($"Data Source={path};Pooling=False");
            con.Open();
            foreach (var table in new[] { "player_positions", "player_seasons" })
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"PRAGMA foreign_key_list({table})";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read(), table);
                Assert.Equal("players", r.GetString(2));
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>Weitere Mannschaften (z. B. Frauen, AP 15) werden vollständig gespeichert; die erste bleibt in den Vereinsspalten.</summary>
    [Fact]
    public void AdditionalTeamsRoundTrip()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        club.Teams.Add(new Fmh.Core.Model.Team { Id = 9001, ClubId = club.Id, WorldId = "women", LeagueId = "test", PlayerIds = { 1, 2 } });
        var path = Path.Combine(Path.GetTempPath(), $"fmh-teams-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path).Clubs[club.Id];
            Assert.Equal(2, loaded.Teams.Count);
            Assert.Equal(club.LeagueId, loaded.FirstTeam.LeagueId);
            Assert.Equal(Fmh.Core.Model.Team.MenWorld, loaded.FirstTeam.WorldId);
            var women = loaded.Teams[1];
            Assert.Equal((9001, "women", "test"), (women.Id, women.WorldId, women.LeagueId));
            Assert.Equal(new[] { 1, 2 }, women.PlayerIds);
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>Must-now E6: Fremdschlüssel an Transfers/Raten/Bedingungen und Nationalitäten-Tabelle nach Migration.</summary>
    [Fact]
    public void MustNowForeignKeysAndNationalitiesExistAfterMigration()
    {
        var path = Fixture();
        try
        {
            var world = SqliteStore.Load(path);
            SqliteStore.Save(world, path);
            using var con = new SqliteConnection($"Data Source={path};Pooling=False");
            con.Open();
            foreach (var (table, expected) in new[]
                     {
                         ("transfers", "players"), ("goals", "fixtures"), ("cards", "fixtures"),
                         ("transactions", "clubs"), ("player_nationalities", "players"), ("clauses", "players"),
                     })
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"PRAGMA foreign_key_list({table})";
                using var r = cmd.ExecuteReader();
                var refs = new List<string>();
                while (r.Read()) refs.Add(r.GetString(2));
                Assert.Contains(expected, refs);
            }
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM player_nationalities";
                Assert.True((long)cmd.ExecuteScalar()! > 0);
            }
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM contracts WHERE current = 1 AND terms IS NOT NULL";
                Assert.True((long)cmd.ExecuteScalar()! > 0);
            }
            // Spieler-JSON-Vertrag ist leer; Terms stehen in contracts.
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM players WHERE contract IS NOT NULL";
                Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
            }
        }
        finally
        {
            Cleanup(path);
        }
    }
}
