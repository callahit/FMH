using System.IO.Compression;
using Fmh.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Fmh.Tests;

/// <summary>Vor dem Anheben eines alten Spielstands entsteht eine Kopie des Originals; aktuelle Stände bleiben unberührt.</summary>
public sealed class SaveBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fmh-backup-" + Guid.NewGuid().ToString("N"));

    public SaveBackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void OldSaveIsCopiedBeforeMigrationAndCurrentSaveIsNot()
    {
        var path = Path.Combine(_dir, "alt" + SaveGame.Extension);
        var source = Path.Combine(TestData.RepoRoot(), "tests", "Fmh.Tests", "Saves", "v5.fmh.gz");
        using (var input = new GZipStream(File.OpenRead(source), CompressionMode.Decompress))
        using (var output = File.Create(path))
            input.CopyTo(output);
        var original = File.ReadAllBytes(path);

        var world = SqliteStore.Load(path);
        var backup = Assert.Single(Directory.GetFiles(_dir, "*.bak"));
        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.NotEqual(original, File.ReadAllBytes(path)); // der Spielstand selbst ist angehoben
        // Die Kopie erscheint nicht in der Liste der Spielstände.
        Assert.Single(SaveGame.List(_dir));

        // Ein aktueller Spielstand braucht keine Migration und bekommt keine Kopie.
        var current = Path.Combine(_dir, "neu" + SaveGame.Extension);
        SaveGame.Save(world, current);
        SqliteStore.Load(current);
        Assert.Single(Directory.GetFiles(_dir, "*.bak"));
    }
}
