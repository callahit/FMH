using Fmh.Core.Data;
using Fmh.Core.Data.Editing;

namespace Fmh.Tests;

/// <summary>Editor: Pakete schreiben nur in ihren eigenen Ordner; der Validator prüft den Szenenkatalog.</summary>
public sealed class EditorSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-editor-safety-" + Guid.NewGuid().ToString("N"));

    public EditorSafetyTests()
    {
        var source = Path.Combine(PackageLoader.FindDataRoot(), "base");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, "data", "base", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private string DataRoot => Path.Combine(_root, "data");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void WriteManifest(string folder, string id)
    {
        Directory.CreateDirectory(Path.Combine(DataRoot, folder));
        File.WriteAllText(Path.Combine(DataRoot, folder, PackageLoader.ManifestFile),
            $$"""{ "id": "{{id}}", "name": "Test", "version": "1.0.0", "dependsOn": ["base"], "createdWith": "{{PackageWorkspace.Tool}}" }""");
    }

    [Fact]
    public void PackageWithAPathAsIdIsNotOpened()
    {
        WriteManifest("boese", "../../ausserhalb");
        var ex = Assert.Throws<InvalidOperationException>(() => PackageWorkspace.Open(DataRoot, "../../ausserhalb"));
        Assert.Contains("ungültig", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "..", "ausserhalb")));
    }

    [Fact]
    public void SavingWritesIntoTheFolderThePackageCameFrom()
    {
        WriteManifest("ordner", "paket");
        var ws = PackageWorkspace.Open(DataRoot, "paket");
        ws.Apply(new SetManifestField("name", "Geändert"));
        ws.Save();
        Assert.Contains("Geändert", File.ReadAllText(Path.Combine(DataRoot, "ordner", PackageLoader.ManifestFile)));
        Assert.False(Directory.Exists(Path.Combine(DataRoot, "paket")));
    }

    [Fact]
    public void ShippedScenesAreValidAndBrokenOnesAreReported()
    {
        var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base" });
        Assert.Empty(SceneValidator.Validate(data));

        var first = data.Scenes.Scenes[0];
        data.Scenes.Scenes.Add(new SceneDef { Id = first.Id, On = new() { "kickoff" }, Steps = new() { new SceneStep { Text = "scene.gibtsnicht" } } });
        data.Scenes.Scenes.Add(new SceneDef
        {
            Id = "test.leer", On = new(), Weight = 0,
            When = new SceneCondition { MinMinute = 80, MaxMinute = 10 },
        });
        var errors = SceneValidator.Validate(data);
        Assert.Contains(errors, e => e.Contains($"'{first.Id}' ist mehrfach"));
        Assert.Contains(errors, e => e.Contains("scene.gibtsnicht") && e.Contains("Standardsprache"));
        Assert.Contains(errors, e => e.Contains("'test.leer': keine Ereignisse"));
        Assert.Contains(errors, e => e.Contains("'test.leer': Gewicht"));
        Assert.Contains(errors, e => e.Contains("'test.leer': keine Zeilen"));
        Assert.Contains(errors, e => e.Contains("„minMinute“ liegt nach „maxMinute“"));
    }
}
