using System.Text.Json.Nodes;
using Fmh.Core.Data;
using Fmh.Core.Data.Editing;

namespace Fmh.Tests;

/// <summary>AP 6: Arbeitsbereich des Editors – eigenes Paket über base, Rückgängig, feste Schreibweise, Prüfung, CSV.</summary>
public sealed class EditorWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-editor-test-" + Guid.NewGuid().ToString("N"));

    public EditorWorkspaceTests()
    {
        // Kopie des Grundpakets, damit Tests nie die ausgelieferten Daten berühren
        var source = Path.Combine(PackageLoader.FindDataRoot(), "base");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, "base", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PackageWorkspace New(string id = "mod") => PackageWorkspace.Create(_root, id, "Testpaket", new[] { "base" });

    private static JsonPath ClubName(int index) => JsonPath.Root.Then(index).Then("name");

    [Fact]
    public void EditLandsInOwnPackageAndBaseStaysUntouched()
    {
        var baseClubs = File.ReadAllText(Path.Combine(_root, "base", "clubs", "de.json"));
        var ws = New();
        ws.Apply(new SetValue("clubs/de.json", ClubName(0), JsonValue.Create("FC Neustadt")));
        Assert.Equal(new[] { "clubs/de.json" }, ws.ChangedFiles);
        var diff = Assert.Single(ws.Diff("clubs/de.json"));
        Assert.Equal(DiffKind.Changed, diff.Kind);
        Assert.Equal("[key=rheinfels].name", diff.Path);

        ws.Save();
        Assert.Equal(baseClubs, File.ReadAllText(Path.Combine(_root, "base", "clubs", "de.json")));
        Assert.True(File.Exists(Path.Combine(_root, "mod", "clubs", "de.json")));
        Assert.Equal(new[] { "clubs", "package.json" },
            Directory.EnumerateFileSystemEntries(Path.Combine(_root, "mod")).Select(Path.GetFileName).OrderBy(n => n));

        // Das Spiel lädt die Änderung, und der Editor öffnet das Paket wieder
        var data = PackageLoader.Load(_root);
        Assert.Contains(data.Clubs, c => c.Name == "FC Neustadt");
        var reopened = PackageWorkspace.Open(_root, "mod");
        Assert.Equal("FC Neustadt", ClubName(0).Resolve(reopened.Document("clubs/de.json"))!.ToString());
    }

    [Fact]
    public void UndoAndRedoWorkAcrossFilesAndBatches()
    {
        var ws = New();
        var before = ClubName(1).Resolve(ws.Document("clubs/de.json"))!.ToString();
        ws.Apply(new SetValue("clubs/de.json", ClubName(1), JsonValue.Create("Neu")));
        ws.Apply(new EditBatch("Import", new JsonEdit[]
        {
            new InsertItem("clubs/de.json", JsonPath.Root, 0, new JsonObject { ["key"] = "neu", ["name"] = "SV Neu" }),
            new SetValue("rules/engine.json", JsonPath.Root.Then("testWert"), JsonValue.Create(3)),
        }));
        Assert.Equal("SV Neu", ClubName(0).Resolve(ws.Document("clubs/de.json"))!.ToString());

        ws.Undo();
        Assert.Equal("Neu", ClubName(1).Resolve(ws.Document("clubs/de.json"))!.ToString());
        Assert.Null((ws.Document("rules/engine.json") as JsonObject)!["testWert"]);
        ws.Undo();
        Assert.Equal(before, ClubName(1).Resolve(ws.Document("clubs/de.json"))!.ToString());
        Assert.Empty(ws.ChangedFiles);
        Assert.False(ws.CanUndo);

        ws.Redo();
        ws.Redo();
        Assert.Equal("SV Neu", ClubName(0).Resolve(ws.Document("clubs/de.json"))!.ToString());
        Assert.Equal(3, (int)(ws.Document("rules/engine.json") as JsonObject)!["testWert"]!);
        Assert.False(ws.CanRedo);
    }

    [Fact]
    public void SavedPackageIsByteIdenticalAndSorted()
    {
        string Build(string id)
        {
            var ws = New(id);
            ws.Apply(new SetValue("rules/engine.json", JsonPath.Root.Then("zzz"), JsonValue.Create(1.5)));
            ws.Apply(new SetValue("rules/engine.json", JsonPath.Root.Then("aaa"), JsonValue.Create("Ä")));
            ws.Save();
            ws.Save(); // zweites Speichern ändert nichts
            return File.ReadAllText(Path.Combine(_root, id, "rules", "engine.json"));
        }
        var a = Build("eins");
        var b = Build("zwei");
        Assert.Equal(a, b);
        Assert.EndsWith("}\n", a);
        Assert.DoesNotContain("\r", a);
        Assert.Contains("\"aaa\": \"Ä\"", a);
        var keys = (CanonicalJson.Parse(a) as JsonObject)!.Select(kv => kv.Key).ToList();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
        Assert.Equal(CanonicalJson.Serialize(CanonicalJson.Parse(a)), a);
    }

    [Fact]
    public void LanguageFilesOnlyStoreChangedTexts()
    {
        var ws = New();
        var key = (ws.Document("lang/de.json") as JsonObject)!.First().Key;
        ws.Apply(new SetValue("lang/de.json", JsonPath.Root.Then(key), JsonValue.Create("Geänderter Text")));
        ws.Save();
        var own = CanonicalJson.Load(Path.Combine(_root, "mod", "lang", "de.json")) as JsonObject;
        var only = Assert.Single(own!);
        Assert.Equal(key, only.Key);
        Assert.Equal("Geänderter Text", only.Value!.ToString());
        // Das Spiel führt die Texte zusammen: der geänderte gilt, alle anderen bleiben
        var data = PackageLoader.Load(_root);
        Assert.Equal("Geänderter Text", data.Texts[key]);
    }

    [Fact]
    public void RevertedFileDisappearsFromPackage()
    {
        var ws = New();
        var old = ClubName(0).Resolve(ws.Document("clubs/de.json"))!.DeepClone();
        ws.Apply(new SetValue("clubs/de.json", ClubName(0), JsonValue.Create("X")));
        ws.Save();
        ws.Apply(new SetValue("clubs/de.json", ClubName(0), old));
        ws.Save();
        Assert.False(File.Exists(Path.Combine(_root, "mod", "clubs", "de.json")));
    }

    [Fact]
    public void ShippedPackagesCannotBeOpenedAndIdsAreChecked()
    {
        Assert.Throws<InvalidOperationException>(() => PackageWorkspace.Open(_root, "base"));
        Assert.Throws<InvalidOperationException>(() => PackageWorkspace.Create(_root, "base", "x", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => PackageWorkspace.Create(_root, "mit leerzeichen", "x", new[] { "base" }));
    }

    [Fact]
    public void ValidationFindsErrorsBeforeSaving()
    {
        var ws = New();
        Assert.Empty(ws.Validate());
        var firstKey = JsonPath.Root.Then(0).Then("key").Resolve(ws.Document("clubs/de.json"))!.DeepClone();
        ws.Apply(new SetValue("clubs/de.json", JsonPath.Root.Then(1).Then("key"), firstKey));
        Assert.Contains(ws.Validate(), e => e.Contains("mehrfach"));
        Assert.False(Directory.Exists(Path.Combine(_root, "mod"))); // Prüfung schreibt nichts ins Datenverzeichnis
    }

    [Fact]
    public void ClubCsvRoundTripsAndReportsErrors()
    {
        var clubs = (New().Document("clubs/de.json") as JsonArray)!;
        var csv = ClubCsv.Export(clubs);
        Assert.StartsWith(string.Join(';', ClubCsv.Columns), csv);
        var (back, errors) = ClubCsv.Import(csv);
        Assert.Empty(errors);
        Assert.True(CanonicalJson.Same(clubs, back));

        var (_, bad) = ClubCsv.Import("key;name;capacity\nneu;\"SV \"\"Neu\"\"; Stadt\";viele\n;Ohne Schlüssel;100\n");
        Assert.Contains(bad, e => e.Contains("ganze Zahl"));
        Assert.Contains(bad, e => e.Contains("Schlüssel fehlt"));
        var (quoted, _) = ClubCsv.Import("key;name\nneu;\"SV \"\"Neu\"\"; Stadt\"\n");
        Assert.Equal("SV \"Neu\"; Stadt", quoted[0]!["name"]!.ToString());
    }
}
