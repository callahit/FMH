using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Core.Data.Schema;

namespace Fmh.Editor.ViewModels;

/// <summary>
/// Regeln und Inhalte: jede Datendatei (Spielregeln, Gebäude, Ereignisse, Pressefragen, Sponsoren, Namen …) als Baum;
/// Werte lassen sich direkt bearbeiten, Zahlen und Wahrheitswerte behalten ihren Typ.
/// </summary>
public sealed partial class FilesSectionViewModel : SectionViewModel
{
    public FilesSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.files")
    {
        foreach (var f in ws.Files.Where(f => !f.StartsWith("clubs/", StringComparison.Ordinal) && !f.StartsWith("lang/", StringComparison.Ordinal)))
            Files.Add(f);
        _file = Files.FirstOrDefault(f => f == "rules/engine.json") ?? Files.FirstOrDefault() ?? "";
        Rebuild();
    }

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<JsonNodeItem> Nodes { get; } = new();
    [ObservableProperty] private string _file;

    partial void OnFileChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Nodes.Clear();
        if (string.IsNullOrEmpty(File)) return;
        var doc = Workspace.Document(File);
        foreach (var item in JsonNodeItem.Children(this, doc, JsonPath.Root)) Nodes.Add(item);
    }

    [ObservableProperty] private JsonNodeItem? _selectedNode;

    /// <summary>Datei im Baum öffnen (z. B. aus dem Länder-Bereich), optional mit Sprung zu einem Feld.</summary>
    public void Open(string file, JsonPath? path = null)
    {
        if (!Files.Contains(file)) Files.Add(file);
        File = file;
        if (path is null) return;
        // Bis zum tiefsten vorhandenen Knoten aufklappen und auswählen
        IEnumerable<JsonNodeItem> level = Nodes;
        JsonNodeItem? found = null;
        for (var depth = 1; depth <= path.Segments.Count; depth++)
        {
            var prefix = new JsonPath(path.Segments.Take(depth).ToList());
            var next = level.FirstOrDefault(n => n.Path.Equals(prefix));
            if (next is null) break;
            if (found is not null) found.IsExpanded = true;
            found = next;
            level = next.Items;
        }
        SelectedNode = found;
    }

    /// <summary>Teilschema eines Knotens (Beschreibung, Feldvorschläge, Werttyp); null ohne Schema.</summary>
    internal System.Text.Json.Nodes.JsonObject? SchemaAt(JsonPath path) =>
        Workspace.Schema(File) is { } schema ? SchemaNavigator.At(schema, path) : null;

    /// <summary>Werte nachziehen; hat sich der Aufbau geändert (Einträge dazu oder weg), Baum neu aufbauen.</summary>
    public override void Refresh()
    {
        foreach (var f in Workspace.Files.Where(f => !f.StartsWith("clubs/", StringComparison.Ordinal) && !f.StartsWith("lang/", StringComparison.Ordinal) && !Files.Contains(f)))
            Files.Add(f);
        var doc = Workspace.Document(File);
        if (!Nodes.All(n => n.Sync(doc))) Rebuild();
    }

    internal void Set(JsonPath path, JsonNode? value) => Workspace.Apply(new SetValue(File, path, value));

    internal JsonNode? Resolve(JsonPath path) => path.Resolve(Workspace.Document(File));

    internal void Insert(JsonPath list, int index, JsonNode? item) => Workspace.Apply(new InsertItem(File, list, index, item));

    internal void Remove(JsonPath path)
    {
        if (path.Last is int i) Workspace.Apply(new RemoveItem(File, path.Parent, i));
        else Workspace.Apply(new SetValue(File, path, null, Remove: true));
    }
}

/// <summary>Knoten im Datei-Baum: Objekt, Liste oder Wert; Listen bekommen Einträge, Objekte Felder, alles lässt sich entfernen.</summary>
public sealed partial class JsonNodeItem : ObservableObject
{
    private readonly FilesSectionViewModel _owner;
    private JsonValueKind _kind;
    private string _value = "";

    private JsonNodeItem(FilesSectionViewModel owner, string name, JsonPath path, JsonNode? node)
    {
        _owner = owner;
        Name = name;
        Path = path;
        Load(node);
        foreach (var child in Children(owner, node, path)) Items.Add(child);
    }

    public string Name { get; }
    public JsonPath Path { get; }
    public ObservableCollection<JsonNodeItem> Items { get; } = new();
    public bool IsLeaf => _kind is not (JsonValueKind.Object or JsonValueKind.Array);
    public string Summary => _kind == JsonValueKind.Array ? $"[{Items.Count}]" : _kind == JsonValueKind.Object ? "{…}" : "";
    public bool IsArray => _kind == JsonValueKind.Array;
    public bool IsObject => _kind == JsonValueKind.Object;
    public bool CanRemove => Path.Segments.Count > 0;

    /// <summary>Name und Wert für ein neues Feld (nur Objekte).</summary>
    public string NewField { get; set; } = "";
    public string NewValue { get; set; } = "";

    [ObservableProperty] private bool _isExpanded;

    /// <summary>Beschreibung aus dem JSON-Schema (Hinweis beim Feldnamen).</summary>
    public string? Description => _owner.SchemaAt(Path)?["description"]?.GetValue<string>();

    /// <summary>Felder, die das Objekt laut Schema noch haben kann.</summary>
    public IReadOnlyList<string> FieldSuggestions =>
        IsObject ? SchemaNavigator.Properties(_owner.SchemaAt(Path)).Where(p => Items.All(i => i.Name != p)).ToList() : Array.Empty<string>();

    /// <summary>Liste: Eintrag anhängen (Kopie des letzten, sonst leeres Objekt).</summary>
    [RelayCommand]
    private void AddItem()
    {
        if (_owner.Resolve(Path) is not JsonArray list) return;
        _owner.Insert(Path, list.Count, list.Count > 0 ? list[^1]?.DeepClone() : new JsonObject());
    }

    /// <summary>Objekt: Feld hinzufügen; der Wert wird als Zahl, Wahrheitswert oder Text gelesen.</summary>
    [RelayCommand]
    private void AddField()
    {
        var name = NewField.Trim();
        if (name.Length == 0 || _owner.Resolve(Path) is not JsonObject obj || obj.ContainsKey(name)) return;
        var text = NewValue.Trim();
        // Mit Schema: Wert im Typ des Feldes (leere Liste bzw. leeres Objekt, wenn kein Wert angegeben ist)
        if (_owner.SchemaAt(Path.Then(name)) is { } field)
        {
            var types = field["type"]?.ToJsonString() ?? "";
            if (text.Length == 0 && types.Contains("\"array\"")) { _owner.Set(Path.Then(name), new JsonArray()); return; }
            if (text.Length == 0 && types.Contains("\"object\"")) { _owner.Set(Path.Then(name), new JsonObject()); return; }
            if (SchemaNavigator.TryParse(field, text, out var typed) && typed is not null) _owner.Set(Path.Then(name), typed);
            return;
        }
        JsonNode? value = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n)
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d)
            : bool.TryParse(text, out var b) ? JsonValue.Create(b)
            : text == "[]" ? new JsonArray() : text == "{}" ? new JsonObject() : JsonValue.Create(text);
        _owner.Set(Path.Then(name), value);
    }

    [RelayCommand]
    private void Remove() => _owner.Remove(Path);

    public static IEnumerable<JsonNodeItem> Children(FilesSectionViewModel owner, JsonNode? node, JsonPath path) => node switch
    {
        JsonObject o => o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new JsonNodeItem(owner, kv.Key, path.Then(kv.Key), kv.Value)),
        JsonArray a => a.Select((n, i) => new JsonNodeItem(owner, Label(n, i), path.Then(i), n)),
        _ => Enumerable.Empty<JsonNodeItem>(),
    };

    /// <summary>Listeneinträge mit Schlüssel heißen danach, z. B. „[0] trainingPitch“.</summary>
    private static string Label(JsonNode? n, int i) =>
        n is JsonObject o && (o["id"] ?? o["key"] ?? o["code"]) is JsonValue v ? $"[{i}] {v}" : $"[{i}]";

    private void Load(JsonNode? node)
    {
        _kind = node?.GetValueKind() ?? JsonValueKind.Null;
        _value = node is JsonValue v ? (_kind == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString()) : "";
    }

    /// <summary>Gleicht den Knoten mit dem Dokument ab; false, wenn sich der Aufbau geändert hat.</summary>
    internal bool Sync(JsonNode? root)
    {
        JsonNode? node;
        try { node = Path.Resolve(root); }
        catch (InvalidOperationException) { return false; }
        var kind = node?.GetValueKind() ?? JsonValueKind.Null;
        if (IsLeaf != kind is not (JsonValueKind.Object or JsonValueKind.Array)) return false;
        if (IsLeaf)
        {
            var old = _value;
            Load(node);
            if (old != _value) OnPropertyChanged(nameof(Value));
            return true;
        }
        var count = node is JsonObject o ? o.Count : node is JsonArray a ? a.Count : 0;
        return count == Items.Count && Items.All(i => i.Sync(root));
    }

    /// <summary>Wert als Text; beim Setzen wird er im ursprünglichen Typ gespeichert (ungültige Zahl bleibt unverändert).</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (value == _value || !IsLeaf) return;
            JsonNode? node = _kind switch
            {
                JsonValueKind.Number when double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    => d == Math.Floor(d) && Math.Abs(d) < 1e15 && !value.Contains('.') && !value.Contains(',') ? JsonValue.Create((long)d) : JsonValue.Create(d),
                JsonValueKind.Number => null,
                JsonValueKind.True or JsonValueKind.False when bool.TryParse(value, out var b) => JsonValue.Create(b),
                JsonValueKind.True or JsonValueKind.False => null,
                JsonValueKind.Null when value.Length == 0 => null,
                _ => JsonValue.Create(value),
            };
            if (node is null && _kind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) { OnPropertyChanged(); return; }
            _owner.Set(Path, node);
        }
    }
}
