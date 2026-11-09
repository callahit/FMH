using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

/// <summary>
/// Arbeitsbereich des Editors (Umsetzungskonzept 7): ein eigenes Datenpaket über seinen Abhängigkeiten (z. B. base).
/// Ausgelieferte Pakete werden nie verändert; jede Änderung landet im eigenen Paket. Dateien werden wie im Spiel
/// übereinandergelegt: Eine Datei im eigenen Paket ersetzt die darunterliegende, Sprachdateien werden schlüsselweise
/// zusammengeführt (das Paket enthält dann nur geänderte Texte).
/// </summary>
public sealed class PackageWorkspace
{
    public const string Tool = "fmh-editor";

    private readonly List<PackageManifest> _lower;
    private readonly Dictionary<string, JsonNode?> _docs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode?> _baseline = new(StringComparer.Ordinal);
    private readonly Stack<(JsonEdit undo, JsonEdit redo)> _undo = new();
    private readonly Stack<(JsonEdit undo, JsonEdit redo)> _redo = new();

    public string DataRoot { get; }
    public PackageManifest Manifest { get; }
    public string PackageDirectory => Path.Combine(DataRoot, Manifest.Id);

    /// <summary>Wird nach jeder Änderung, jedem Rückgängig und Wiederholen ausgelöst.</summary>
    public event Action? Changed;

    private PackageWorkspace(string dataRoot, PackageManifest manifest, List<PackageManifest> lower)
    {
        DataRoot = dataRoot;
        Manifest = manifest;
        _lower = lower;
    }

    /// <summary>Alle Pakete im Datenverzeichnis.</summary>
    public static List<PackageManifest> Packages(string dataRoot)
    {
        var list = new List<PackageManifest>();
        foreach (var dir in Directory.GetDirectories(dataRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var path = Path.Combine(dir, PackageLoader.ManifestFile);
            if (!File.Exists(path)) continue;
            var m = Json.Load<PackageManifest>(path);
            m.Directory = dir;
            list.Add(m);
        }
        return list;
    }

    /// <summary>Legt ein neues, leeres Paket an (noch nicht gespeichert).</summary>
    public static PackageWorkspace Create(string dataRoot, string id, string name, IEnumerable<string> dependsOn)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Paket-Id darf nur Buchstaben, Ziffern, '-' und '_' enthalten.", nameof(id));
        var all = Packages(dataRoot);
        if (all.Any(p => p.Id == id)) throw new InvalidOperationException($"Paket '{id}' gibt es schon.");
        var manifest = new PackageManifest { Id = id, Name = name, Version = "1.0.0", DependsOn = dependsOn.ToList(), CreatedWith = Tool };
        return new PackageWorkspace(dataRoot, manifest, Lower(all, manifest));
    }

    /// <summary>Öffnet ein mit dem Editor angelegtes Paket; ausgelieferte Pakete lassen sich nicht öffnen.</summary>
    public static PackageWorkspace Open(string dataRoot, string id)
    {
        var all = Packages(dataRoot);
        var manifest = all.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException($"Paket '{id}' fehlt.");
        if (manifest.CreatedWith != Tool)
            throw new InvalidOperationException($"Paket '{id}' wurde nicht mit dem Editor angelegt und bleibt unverändert. Lege ein eigenes Paket darüber an.");
        return new PackageWorkspace(dataRoot, manifest, Lower(all, manifest));
    }

    /// <summary>Abhängigkeiten des Pakets (transitiv), in Ladereihenfolge.</summary>
    private static List<PackageManifest> Lower(List<PackageManifest> all, PackageManifest manifest)
    {
        var byId = all.ToDictionary(p => p.Id);
        var needed = new HashSet<string>();
        void Add(string dep)
        {
            if (!byId.TryGetValue(dep, out var m)) throw new InvalidOperationException($"Paket '{manifest.Id}' braucht fehlendes Paket '{dep}'.");
            if (needed.Add(dep)) foreach (var d in m.DependsOn) Add(d);
        }
        foreach (var dep in manifest.DependsOn) Add(dep);
        return PackageLoader.SortByDependencies(all.Where(p => needed.Contains(p.Id)).ToList());
    }

    private static bool IsLanguageFile(string rel) => rel.StartsWith("lang/", StringComparison.Ordinal);

    /// <summary>Alle JSON-Dateien aus den Abhängigkeiten und dem eigenen Paket (relative Pfade).</summary>
    public IReadOnlyList<string> Files
    {
        get
        {
            var onDisk = _lower.Select(m => m.Directory).Append(PackageDirectory).Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')));
            return onDisk.Concat(_docs.Keys).Where(r => r != PackageLoader.ManifestFile)
                .Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Inhalt der Abhängigkeiten ohne das eigene Paket (null, wenn es die Datei dort nicht gibt).</summary>
    public JsonNode? Baseline(string rel)
    {
        if (_baseline.TryGetValue(rel, out var cached)) return cached;
        JsonNode? result = null;
        foreach (var m in _lower)
        {
            var path = Path.Combine(m.Directory, rel);
            if (!File.Exists(path)) continue;
            var node = CanonicalJson.Load(path);
            if (IsLanguageFile(rel) && result is JsonObject merged && node is JsonObject add)
                foreach (var (k, v) in add) merged[k] = v?.DeepClone();
            else
                result = node;
        }
        return _baseline[rel] = result;
    }

    /// <summary>Aktueller Inhalt (Abhängigkeiten plus eigenes Paket plus ungespeicherte Änderungen).</summary>
    public JsonNode? Document(string rel)
    {
        if (_docs.TryGetValue(rel, out var doc)) return doc;
        var current = Baseline(rel)?.DeepClone();
        var own = Path.Combine(PackageDirectory, rel);
        if (File.Exists(own))
        {
            var node = CanonicalJson.Load(own);
            if (IsLanguageFile(rel) && current is JsonObject merged && node is JsonObject add)
                foreach (var (k, v) in add) merged[k] = v?.DeepClone();
            else
                current = node;
        }
        return _docs[rel] = current;
    }

    internal void ReplaceDocument(string rel, JsonNode? node) => _docs[rel] = node;

    /// <summary>Neue Datei im eigenen Paket anlegen (z. B. ein neues Land).</summary>
    public void AddFile(string rel, JsonNode content) => Apply(new SetValue(rel, JsonPath.Root, content));

    public void Apply(JsonEdit edit)
    {
        if (edit is not SetManifestField) Document(edit.File);
        var inverse = edit.Apply(this);
        _undo.Push((inverse, edit));
        _redo.Clear();
        Changed?.Invoke();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => CanUndo ? _undo.Peek().redo.Describe() : null;
    public string? RedoLabel => CanRedo ? _redo.Peek().redo.Describe() : null;

    public void Undo()
    {
        if (!CanUndo) return;
        var (undo, redo) = _undo.Pop();
        undo.Apply(this);
        _redo.Push((undo, redo));
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var (_, redo) = _redo.Pop();
        var inverse = redo.Apply(this);
        _undo.Push((inverse, redo));
        Changed?.Invoke();
    }

    /// <summary>Was das eigene Paket für eine Datei enthalten muss (null = nichts, Datei entspricht den Abhängigkeiten).</summary>
    public JsonNode? Packaged(string rel)
    {
        var doc = Document(rel);
        var baseline = Baseline(rel);
        if (CanonicalJson.Same(doc, baseline)) return null;
        if (IsLanguageFile(rel) && doc is JsonObject d && baseline is JsonObject b)
        {
            var delta = new JsonObject();
            foreach (var (k, v) in d)
                if (!b.ContainsKey(k) || !CanonicalJson.Same(b[k], v)) delta[k] = v?.DeepClone();
            return delta.Count == 0 ? null : delta;
        }
        return doc?.DeepClone();
    }

    /// <summary>Dateien, die sich gegenüber den Abhängigkeiten unterscheiden.</summary>
    public IReadOnlyList<string> ChangedFiles => Files.Where(f => Packaged(f) is not null).ToList();

    public IReadOnlyList<DiffEntry> Diff(string rel) => JsonDiff.Compare(Baseline(rel), Document(rel));

    /// <summary>Paket schreiben: Manifest und alle geänderten Dateien in fester Schreibweise; unveränderte Dateien entfallen.</summary>
    public void Save() => WriteTo(PackageDirectory);

    /// <summary>Paket mit dem aktuellen (auch ungespeicherten) Stand in ein anderes Verzeichnis schreiben, z. B. für den Probelauf.</summary>
    public void Export(string dir) => WriteTo(dir);

    private void WriteTo(string dir)
    {
        Directory.CreateDirectory(dir);
        CanonicalJson.Save(Path.Combine(dir, PackageLoader.ManifestFile), System.Text.Json.JsonSerializer.SerializeToNode(Manifest, Json.Options));
        foreach (var rel in Files)
        {
            var path = Path.Combine(dir, rel);
            if (Packaged(rel) is { } content) CanonicalJson.Save(path, content);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Prüft den aktuellen Stand wie beim Spielstart: Pakete laden (eigenes Paket aus einem temporären Verzeichnis)
    /// und den Datenvalidator laufen lassen. Speichern geht auch mit Fehlern, Spielen mit dem Paket nicht.
    /// </summary>
    public List<string> Validate()
    {
        var temp = Path.Combine(Path.GetTempPath(), "fmh-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteTo(Path.Combine(temp, Manifest.Id));
            var data = PackageLoader.Load(new[] { DataRoot, temp });
            return DataValidator.Validate(data);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return new List<string> { ex.Message };
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }
}
