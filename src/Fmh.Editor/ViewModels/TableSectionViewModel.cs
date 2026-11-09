using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Core.Data.Schema;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Art einer Listen-Datei: Ordner, Schlüsselfeld, Tabellenspalten und Vorlage für einen neuen Eintrag.</summary>
public sealed record TableKind(string Folder, string TitleKey, string KeyField, IReadOnlyList<string> Columns, Func<int, JsonObject> NewItem)
{
    public string Title => EditorState.T[TitleKey];
    public override string ToString() => Title;
}

/// <summary>
/// Spieler bzw. Personal, Manager und Schiedsrichter als Tabelle (AP 6c). Spalten und Werttypen kommen aus dem
/// JSON-Schema; weitere Felder (Attribute, Positionen, versteckte Werte) bearbeitet man im Baum.
/// </summary>
public sealed partial class TableSectionViewModel : SectionViewModel
{
    private readonly Action<string> _openInTree;

    public TableSectionViewModel(PackageWorkspace ws, string titleKey, string hintKey, IReadOnlyList<TableKind> kinds, Action<string> openInTree)
        : base(ws, titleKey)
    {
        _openInTree = openInTree;
        Kinds = kinds;
        Hint = EditorState.T[hintKey];
        _kind = kinds[0];
        _file = "";
        LoadFiles();
    }

    public IReadOnlyList<TableKind> Kinds { get; }
    public bool HasKinds => Kinds.Count > 1;
    public string Hint { get; }
    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<TableRow> Rows { get; } = new();
    [ObservableProperty] private TableKind _kind;
    [ObservableProperty] private string _file;
    [ObservableProperty] private TableRow? _selectedRow;
    [ObservableProperty] private string _message = "";

    public IReadOnlyList<string> Columns => Kind.Columns;

    /// <summary>Beschreibung eines Feldes aus dem Schema (Spaltenkopf-Hinweis).</summary>
    public string? Describe(string column) =>
        Workspace.Schema(File) is { } schema ? SchemaNavigator.Description(schema, JsonPath.Root.Then(0).Then(column)) : null;

    partial void OnKindChanged(TableKind value)
    {
        Rows.Clear();
        LoadFiles();
        OnPropertyChanged(nameof(Columns));
    }

    partial void OnFileChanged(string value) => Refresh();

    /// <summary>Dateien des Ordners; ohne eigene Datei schlägt der Editor „&lt;ordner&gt;/&lt;paket&gt;.json“ vor.</summary>
    private void LoadFiles()
    {
        Files.Clear();
        foreach (var f in Workspace.Files.Where(f => f.StartsWith(Kind.Folder + "/", StringComparison.Ordinal))) Files.Add(f);
        var own = $"{Kind.Folder}/{Workspace.Manifest.Id}.json";
        if (!Files.Contains(own)) Files.Add(own);
        File = Files[0];
        Refresh();
    }

    private JsonArray List => Workspace.Document(File) as JsonArray ?? new JsonArray();

    public override void Refresh()
    {
        foreach (var f in Workspace.Files.Where(f => f.StartsWith(Kind.Folder + "/", StringComparison.Ordinal) && !Files.Contains(f))) Files.Add(f);
        var items = List;
        while (Rows.Count > items.Count) Rows.RemoveAt(Rows.Count - 1);
        for (var i = 0; i < items.Count; i++)
        {
            if (i >= Rows.Count) Rows.Add(new TableRow(this, i));
            Rows[i].Load(items[i] as JsonObject);
        }
    }

    /// <summary>Zelle übernehmen: Text im Typ des Schemas, ungültige Eingaben werden gemeldet und nicht gespeichert.</summary>
    internal void Set(int index, string column, string text)
    {
        var path = JsonPath.Root.Then(index).Then(column);
        var field = Workspace.Schema(File) is { } schema ? SchemaNavigator.At(schema, path) : null;
        if (!SchemaNavigator.TryParse(field, text, out var value))
        {
            var allowed = SchemaNavigator.EnumValues(field);
            Message = EditorState.F("editor.table.invalid", column, text) + (allowed.Count > 0 ? " " + string.Join(", ", allowed) : "");
            Rows[index].Load(List[index] as JsonObject, force: true);
            return;
        }
        Message = "";
        if (CanonicalJson.Same((List[index] as JsonObject)?[column], value)) return;
        Workspace.Apply(value is null ? new SetValue(File, path, null, Remove: true) : new SetValue(File, path, value));
    }

    [RelayCommand]
    private void Add()
    {
        var used = List.OfType<JsonObject>().Select(o => o[Kind.KeyField]?.ToString()).ToHashSet();
        var n = 1;
        JsonObject item;
        do item = Kind.NewItem(n++);
        while (used.Contains(item[Kind.KeyField]?.ToString()));
        // Neue Datei: als Liste mit dem ersten Eintrag anlegen (ein Schritt)
        Workspace.Apply(Workspace.Document(File) is JsonArray list
            ? new InsertItem(File, JsonPath.Root, list.Count, item)
            : new SetValue(File, JsonPath.Root, new JsonArray(item)));
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedRow is { } row && row.Index < List.Count) Workspace.Apply(new RemoveItem(File, JsonPath.Root, row.Index));
    }

    [RelayCommand]
    private void EditInTree() => _openInTree(File);

    public string ExportCsv() => TableCsv.Export(List, Columns);

    /// <summary>Ersetzt die Einträge durch die CSV; Felder ohne Spalte bleiben bei bekanntem Schlüssel erhalten.</summary>
    public void ImportCsv(string csv)
    {
        if (Workspace.Schema(File) is not { } schema) return;
        var (rows, errors) = TableCsv.Import(csv, schema, Kind.KeyField, Workspace.Document(File) as JsonArray);
        Message = errors.Count == 0 ? EditorState.F("editor.table.imported", rows.Count) : string.Join("\n", errors.Take(8));
        if (rows.Count > 0) Workspace.Apply(new SetValue(File, JsonPath.Root, rows));
    }
}

/// <summary>Tabellenzeile mit Zellen je Spalte (Bindung über den Indexer „[spalte]“).</summary>
public sealed class TableRow : INotifyPropertyChanged
{
    private readonly TableSectionViewModel _owner;
    private readonly Dictionary<string, string> _values = new();

    public TableRow(TableSectionViewModel owner, int index) => (_owner, Index) = (owner, index);

    public int Index { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string column]
    {
        get => _values.GetValueOrDefault(column, "");
        set { if (value != this[column]) _owner.Set(Index, column, value ?? ""); }
    }

    internal void Load(JsonObject? item, bool force = false)
    {
        var changed = force;
        foreach (var col in _owner.Columns)
        {
            var text = item?[col] is JsonValue v ? ClubCsvText(v) : item?[col] is { } n ? n.ToJsonString() : "";
            if (_values.TryGetValue(col, out var old) && old == text) continue;
            _values[col] = text;
            changed = true;
        }
        if (!changed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
    }

    private static string ClubCsvText(JsonValue v) =>
        v.TryGetValue<double>(out var d) && !v.TryGetValue<string>(out _) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : v.ToString();
}
