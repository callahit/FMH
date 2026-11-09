using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core.Data.Editing;

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

    /// <summary>Werte nachziehen; hat sich der Aufbau geändert (Einträge dazu oder weg), Baum neu aufbauen.</summary>
    public override void Refresh()
    {
        var doc = Workspace.Document(File);
        if (!Nodes.All(n => n.Sync(doc))) Rebuild();
    }

    internal void Set(JsonPath path, JsonNode? value) => Workspace.Apply(new SetValue(File, path, value));
}

/// <summary>Knoten im Datei-Baum: Objekt, Liste oder Wert.</summary>
public sealed class JsonNodeItem : ObservableObject
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
