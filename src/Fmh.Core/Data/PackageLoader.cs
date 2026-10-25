using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>
/// Lädt Datenpakete (Kapitel 12). Jedes Unterverzeichnis mit package.json ist ein Paket.
/// Pakete werden nach Abhängigkeiten sortiert und übereinandergelegt: Eine Datei mit gleichem
/// relativen Pfad in einem späteren Paket ersetzt die frühere (z. B. Grundwelt + eigene Vereinsnamen).
/// </summary>
public static class PackageLoader
{
    public const string ManifestFile = "package.json";

    /// <summary>Sucht das Datenverzeichnis neben der Anwendung bzw. im Repository.</summary>
    public static string FindDataRoot(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            if (!Directory.Exists(explicitPath)) throw new DirectoryNotFoundException($"Datenverzeichnis '{explicitPath}' fehlt.");
            return explicitPath;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, ManifestFile, SearchOption.AllDirectories).Any())
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Kein Datenverzeichnis 'data' mit Paketen gefunden.");
    }

    public static GameData Load(string dataRoot, string language = "de", IReadOnlyCollection<string>? enabledPackages = null)
    {
        var manifests = new List<PackageManifest>();
        foreach (var dir in Directory.GetDirectories(dataRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(dir, ManifestFile);
            if (!File.Exists(manifestPath)) continue;
            var m = Json.Load<PackageManifest>(manifestPath);
            m.Directory = dir;
            if (enabledPackages is null || enabledPackages.Contains(m.Id)) manifests.Add(m);
        }
        if (manifests.Count == 0) throw new InvalidDataException($"Keine Datenpakete in '{dataRoot}'.");

        var ordered = SortByDependencies(manifests);
        var files = Overlay(ordered);

        string? Path1(string rel) => files.TryGetValue(rel, out var p) ? p : null;
        IEnumerable<string> PathsUnder(string folder) =>
            files.Where(kv => kv.Key.StartsWith(folder + "/", StringComparison.Ordinal) && kv.Key.EndsWith(".json", StringComparison.Ordinal))
                 .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                 .Select(kv => kv.Value);

        var countries = PathsUnder("countries").Select(Json.Load<CountryConfig>).ToList();
        var clubs = PathsUnder("clubs").SelectMany(Json.Load<List<ClubDefinition>>).ToList();
        var names = PathsUnder("names").Select(Json.Load<NameList>).ToDictionary(n => n.Country);

        var texts = new Dictionary<string, string>();
        var commentary = new Dictionary<string, List<string>>();
        foreach (var m in ordered)
        {
            // Sprachdateien werden schlüsselweise zusammengeführt, damit Pakete einzelne Texte ändern können.
            var langFile = Path.Combine(m.Directory, "lang", language + ".json");
            if (File.Exists(langFile))
                foreach (var kv in Json.Load<Dictionary<string, string>>(langFile)) texts[kv.Key] = kv.Value;
            var commentaryFile = Path.Combine(m.Directory, "lang", language + ".commentary.json");
            if (File.Exists(commentaryFile))
                foreach (var kv in Json.Load<Dictionary<string, List<string>>>(commentaryFile)) commentary[kv.Key] = kv.Value;
        }

        return new GameData
        {
            Packages = ordered,
            Countries = countries,
            Clubs = clubs,
            Names = names,
            Formations = Path1("formations.json") is { } f ? Json.Load<List<Formation>>(f) : new(),
            PositionProfiles = Path1("rules/positions.json") is { } p ? Json.Load<PositionProfiles>(p) : new(),
            Injuries = Path1("rules/injuries.json") is { } i ? Json.Load<List<InjuryType>>(i) : new(),
            Engine = Path1("rules/engine.json") is { } e ? Json.Load<EngineConfig>(e) : new(),
            Texts = new Texts(texts, language),
            Commentary = commentary,
        };
    }

    private static Dictionary<string, string> Overlay(IEnumerable<PackageManifest> ordered)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in ordered)
        {
            foreach (var file in Directory.EnumerateFiles(m.Directory, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(m.Directory, file).Replace('\\', '/');
                if (rel == ManifestFile) continue;
                files[rel] = file;
            }
        }
        return files;
    }

    private static List<PackageManifest> SortByDependencies(List<PackageManifest> manifests)
    {
        var byId = manifests.ToDictionary(m => m.Id);
        var result = new List<PackageManifest>();
        var state = new Dictionary<string, int>(); // 1 = in Arbeit, 2 = fertig

        void Visit(PackageManifest m)
        {
            if (state.TryGetValue(m.Id, out var s))
            {
                if (s == 1) throw new InvalidDataException($"Zyklische Paketabhängigkeit bei '{m.Id}'.");
                return;
            }
            state[m.Id] = 1;
            foreach (var dep in m.DependsOn)
            {
                if (!byId.TryGetValue(dep, out var d))
                    throw new InvalidDataException($"Paket '{m.Id}' braucht fehlendes Paket '{dep}'.");
                Visit(d);
            }
            state[m.Id] = 2;
            result.Add(m);
        }

        foreach (var m in manifests.OrderBy(m => m.Id, StringComparer.Ordinal)) Visit(m);
        return result;
    }
}
