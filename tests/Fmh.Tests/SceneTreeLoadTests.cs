using Fmh.SceneTree;

namespace Fmh.Tests;

/// <summary>
/// Beschädigte Textmodus-Dateien des Nutzers dürfen den Spielstart nicht verhindern: Es gibt dann keinen Szenenbaum.
/// Läuft ohne die Originaldateien, also auch in der CI.
/// </summary>
public sealed class SceneTreeLoadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fmh-scenetree-" + Guid.NewGuid().ToString("N"));

    public SceneTreeLoadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void BrokenJsonGivesNoTreeInsteadOfAnException()
    {
        File.WriteAllText(Path.Combine(_dir, "scene-tree.json"), "{ \"nodes\": [ kaputt");
        Assert.Null(SceneTreeAsset.TryLoad(_dir, out var error));
        Assert.Contains(_dir, error);
    }

    [Fact]
    public void BrokenBinaryTreeOrMissingLinesGiveNoTree()
    {
        File.WriteAllBytes(Path.Combine(_dir, "Textm.sav"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        Assert.Null(SceneTreeAsset.TryLoad(_dir, out var missingLines));
        Assert.NotNull(missingLines);

        File.WriteAllText(Path.Combine(_dir, "Textm.txt"), "Zeile\r\n");
        var bytes = new byte[64];
        TextmImporter.Magic.CopyTo(bytes, 0);
        bytes[8] = 2;
        bytes[16] = 0xFF; bytes[17] = 0xFF; bytes[18] = 0xFF; bytes[19] = 0x7F; // unsinnige Knotendaten
        File.WriteAllBytes(Path.Combine(_dir, "Textm.sav"), bytes);
        Assert.Null(SceneTreeAsset.TryLoad(_dir, out var garbage));
        Assert.NotNull(garbage);
    }
}
