using System.Text.Json.Nodes;
using Fmh.Core.Data;
using Fmh.Core.Data.Editing;
using Fmh.Core.Data.Schema;

namespace Fmh.Tests;

/// <summary>AP 6c: JSON-Schemas unter data/schema – aktuell, passend zu allen ausgelieferten Daten, mit klaren Meldungen.</summary>
public sealed class SchemaTests
{
    private static string SchemaDir => Path.Combine(TestData.RepoRoot(), "data", SchemaCatalog.Folder);

    /// <summary>
    /// Die Schemadateien entsprechen den Datentypen. Nach einer Änderung an den Typen mit FMH_UPDATE_SCHEMAS=1 neu
    /// schreiben lassen und die Dateien mit einchecken.
    /// </summary>
    [Fact]
    public void SchemaFilesAreUpToDate()
    {
        var update = Environment.GetEnvironmentVariable("FMH_UPDATE_SCHEMAS") == "1";
        var stale = new List<string>();
        foreach (var entry in SchemaCatalog.Entries)
        {
            var path = Path.Combine(SchemaDir, entry.FileName);
            var generated = CanonicalJson.Serialize(SchemaCatalog.Generate(entry));
            // Zeilenenden egal (Git kann unter Windows CRLF auschecken)
            var current = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
            if (current == generated) continue;
            if (update) { Directory.CreateDirectory(SchemaDir); File.WriteAllText(path, generated); }
            else stale.Add(entry.FileName);
        }
        var vscode = Path.Combine(TestData.RepoRoot(), ".vscode", "settings.json");
        var settings = CanonicalJson.Serialize(SchemaCatalog.EditorSettings());
        if ((File.Exists(vscode) ? File.ReadAllText(vscode).Replace("\r\n", "\n") : "") != settings)
        {
            if (update) { Directory.CreateDirectory(Path.GetDirectoryName(vscode)!); File.WriteAllText(vscode, settings); }
            else stale.Add(".vscode/settings.json");
        }
        var known = SchemaCatalog.Entries.Select(e => e.FileName).ToHashSet();
        stale.AddRange(Directory.EnumerateFiles(SchemaDir, "*.schema.json").Select(Path.GetFileName).Where(f => !known.Contains(f!))!);
        Assert.True(stale.Count == 0, "Veraltete Schemas (FMH_UPDATE_SCHEMAS=1 setzen): " + string.Join(", ", stale));
    }

    [Fact]
    public void AllShippedDataMatchesItsSchemaAndEveryFileHasOne()
    {
        var data = TestData.AllData;
        Assert.Empty(SchemaCheck.Validate(data.Packages));
        var unmapped = data.Packages.SelectMany(p => Directory.EnumerateFiles(p.Directory, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(p.Directory, f).Replace('\\', '/')))
            .Where(rel => SchemaCatalog.For(rel) is null).ToList();
        Assert.Empty(unmapped);
    }

    [Fact]
    public void SchemaReportsUnknownFieldsWrongTypesRangesEnumsAndDates()
    {
        var schema = SchemaCatalog.Generate(SchemaCatalog.Entries.Single(e => e.Name == "players"));
        var players = JsonNode.Parse("""
            [ { "key": "a", "firstName": "A", "lastName": "B", "position": "Sturm", "level": 25, "age": "alt",
                "birthDate": "15.03.2004", "hidden": { "loyality": 5 }, "attributes": { "Tempo": 10 } },
              { "KEY": "b", "FirstName": "C", "lastName": "D", "position": "st", "positions": { "AMC": "good" } } ]
            """);
        var errors = SchemaValidator.Validate(players, schema).Select(e => e.ToString()).ToList();
        Assert.Contains(errors, e => e.StartsWith("[0].position: „Sturm“ ist nicht erlaubt", StringComparison.Ordinal));
        Assert.Contains("[0].level: 25 ist größer als 20.", errors);
        Assert.Contains(errors, e => e.StartsWith("[0].age: erwartet ganze Zahl", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("[0].birthDate: „15.03.2004“ ist kein Datum", StringComparison.Ordinal));
        Assert.Contains("[0].hidden.loyality: unbekanntes Feld „loyality“.", errors);
        Assert.Contains(errors, e => e.StartsWith("[0].attributes.Tempo: „Tempo“ ist als Schlüssel nicht erlaubt", StringComparison.Ordinal));
        // Wie der Paketlader: Groß- und Kleinschreibung bei Feldern und Enum-Werten egal
        Assert.DoesNotContain(errors, e => e.StartsWith("[1]", StringComparison.Ordinal));
        Assert.Equal(6, errors.Count);

        // Beschreibungen aus den XML-Kommentaren für Texteditoren
        var key = schema["items"]!["properties"]!["key"]!;
        Assert.Contains("Eindeutiger Schlüssel", key["description"]!.GetValue<string>());
    }

    [Fact]
    public void ValidatorReportsSchemaErrorsWithFileAndPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "fmh-schema-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = Path.Combine(root, "schema-test");
            Directory.CreateDirectory(Path.Combine(package, "sponsors"));
            File.WriteAllText(Path.Combine(package, "package.json"), """{ "id": "schema-test", "name": "Schema", "version": "1", "dependsOn": ["base"] }""");
            var sponsors = (JsonObject)CanonicalJson.Load(Path.Combine(PackageLoader.FindDataRoot(), "base", "sponsors", "de.json"))!;
            sponsors["sponsorz"] = new JsonArray();
            File.WriteAllText(Path.Combine(package, "sponsors", "de.json"), CanonicalJson.Serialize(sponsors));
            var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot(), root });
            Assert.Contains("schema-test/sponsors/de.json: sponsorz: unbekanntes Feld „sponsorz“.", DataValidator.Validate(data));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
