using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Schema;

/// <summary>Prüft alle Dateien der geladenen Pakete gegen ihre Schemas (Teil von <see cref="DataValidator"/>).</summary>
public static class SchemaCheck
{
    /// <summary>Höchstens so viele Meldungen je Datei, damit ein falscher Dateityp die Liste nicht flutet.</summary>
    private const int MaxPerFile = 20;

    public static IEnumerable<string> Validate(IReadOnlyList<PackageManifest> packages)
    {
        var dirs = packages.Select(p => p.Directory).Where(d => d.Length > 0 && Directory.Exists(d)).ToList();
        if (dirs.Count == 0) yield break;
        var schemas = SchemaCatalog.Load(dirs);
        foreach (var package in packages.Where(p => p.Directory.Length > 0 && Directory.Exists(p.Directory)))
        {
            foreach (var file in Directory.EnumerateFiles(package.Directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(package.Directory, file).Replace('\\', '/');
                if (SchemaCatalog.For(relative) is not { } entry) continue;
                foreach (var error in ValidateFile(file, schemas[entry.Name]).Take(MaxPerFile))
                    yield return $"{package.Id}/{relative}: {error}";
            }
        }
    }

    public static List<SchemaError> ValidateFile(string file, JsonObject schema)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(File.ReadAllText(file), documentOptions: SchemaValidator.ParseOptions); }
        catch (JsonException ex) { return new List<SchemaError> { new("", $"kein gültiges JSON ({ex.Message})") }; }
        return SchemaValidator.Validate(node, schema);
    }
}
