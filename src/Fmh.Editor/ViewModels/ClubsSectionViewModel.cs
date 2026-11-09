using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Vereine als Tabelle (Massenbearbeitung), Hinzufügen, Löschen, CSV-Import und -Export.</summary>
public sealed partial class ClubsSectionViewModel : SectionViewModel
{
    public ClubsSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.clubs")
    {
        foreach (var f in ws.Files.Where(f => f.StartsWith("clubs/", StringComparison.Ordinal))) Files.Add(f);
        _file = Files.FirstOrDefault() ?? "clubs/de.json";
        Refresh();
    }

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<ClubRow> Rows { get; } = new();
    [ObservableProperty] private string _file;
    [ObservableProperty] private ClubRow? _selectedRow;
    [ObservableProperty] private string _message = "";

    partial void OnFileChanged(string value) => Refresh();

    private JsonArray List => Workspace.Document(File) as JsonArray ?? new JsonArray();

    /// <summary>Zeilen an den aktuellen Inhalt angleichen, ohne die Tabelle neu aufzubauen (Eingabe bleibt erhalten).</summary>
    public override void Refresh()
    {
        var clubs = List;
        while (Rows.Count > clubs.Count) Rows.RemoveAt(Rows.Count - 1);
        for (var i = 0; i < clubs.Count; i++)
        {
            if (i >= Rows.Count) Rows.Add(new ClubRow(this, i));
            Rows[i].Load(clubs[i] as JsonObject);
        }
    }

    internal void Set(int index, string field, string? text)
    {
        var path = JsonPath.Root.Then(index).Then(field);
        JsonNode? value = string.IsNullOrWhiteSpace(text) ? null
            : field is "reputation" or "capacity" && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n)
            : field is "latitude" or "longitude" && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d)
            : JsonValue.Create(text.Trim());
        var current = (List[index] as JsonObject)?[field];
        if (CanonicalJson.Same(current, value)) return;
        Workspace.Apply(value is null ? new SetValue(File, path, null, Remove: true) : new SetValue(File, path, value));
    }

    [RelayCommand]
    private void Add()
    {
        var n = List.Count + 1;
        Workspace.Apply(new InsertItem(File, JsonPath.Root, List.Count, new JsonObject
        {
            ["key"] = $"neu{n}", ["name"] = EditorState.F("editor.clubs.newName", n), ["shortName"] = $"Neu {n}",
            ["city"] = "", ["division"] = "", ["reputation"] = 5, ["capacity"] = 5000,
        }));
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedRow is { } row) Workspace.Apply(new RemoveItem(File, JsonPath.Root, row.Index));
    }

    public string ExportCsv() => ClubCsv.Export(List);

    /// <summary>Ersetzt die Vereine der Datei durch die CSV (ein Schritt, rückgängig machbar).</summary>
    public void ImportCsv(string csv)
    {
        var (clubs, errors) = ClubCsv.Import(csv);
        Message = errors.Count == 0 ? EditorState.F("editor.clubs.imported", clubs.Count) : string.Join("\n", errors.Take(8));
        if (clubs.Count > 0) Workspace.Apply(new SetValue(File, JsonPath.Root, clubs));
    }
}

/// <summary>Eine Tabellenzeile; Änderungen gehen als Bearbeitungsschritt in den Arbeitsbereich.</summary>
public sealed class ClubRow : ObservableObject
{
    private readonly ClubsSectionViewModel _owner;
    private readonly Dictionary<string, string> _values = new();

    public ClubRow(ClubsSectionViewModel owner, int index) => (_owner, Index) = (owner, index);

    public int Index { get; }

    internal void Load(JsonObject? club)
    {
        foreach (var col in ClubCsv.Columns)
        {
            var text = club?[col] is JsonValue v ? (v.TryGetValue<double>(out var d) && !v.TryGetValue<string>(out _) ? d.ToString(CultureInfo.InvariantCulture) : v.ToString()) : "";
            if (_values.TryGetValue(col, out var old) && old == text) continue;
            _values[col] = text;
            OnPropertyChanged(Prop(col));
        }
    }

    private static string Prop(string col) => char.ToUpperInvariant(col[0]) + col[1..];

    private string Get(string col) => _values.GetValueOrDefault(col, "");

    private void Put(string col, string value) => _owner.Set(Index, col, value);

    public string Key { get => Get("key"); set => Put("key", value); }
    public string Name { get => Get("name"); set => Put("name", value); }
    public string ShortName { get => Get("shortName"); set => Put("shortName", value); }
    public string City { get => Get("city"); set => Put("city", value); }
    public string Division { get => Get("division"); set => Put("division", value); }
    public string Reputation { get => Get("reputation"); set => Put("reputation", value); }
    public string ReserveOf { get => Get("reserveOf"); set => Put("reserveOf", value); }
    public string Region { get => Get("region"); set => Put("region", value); }
    public string Latitude { get => Get("latitude"); set => Put("latitude", value); }
    public string Longitude { get => Get("longitude"); set => Put("longitude", value); }
    public string PrimaryColor { get => Get("primaryColor"); set => Put("primaryColor", value); }
    public string SecondaryColor { get => Get("secondaryColor"); set => Put("secondaryColor", value); }
    public string Stadium { get => Get("stadium"); set => Put("stadium", value); }
    public string Capacity { get => Get("capacity"); set => Put("capacity", value); }
}
