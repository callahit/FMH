using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>
/// Textmodus-Szenen (rules/scenes.json): Liste mit Filter, Bedingungen als Formular und die Zeilen mit ihren
/// Textvarianten je Sprache (lang/&lt;code&gt;.scenes.json). Fehlende Texte und abweichende Platzhalter sind markiert.
/// </summary>
public sealed partial class ScenesSectionViewModel : SectionViewModel
{
    public const string File = "rules/scenes.json";

    private List<SceneItem> _all = new();

    public ScenesSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.scenes")
    {
        Languages = ws.Files.Where(f => f.StartsWith("lang/", StringComparison.Ordinal) && f.EndsWith(".scenes.json", StringComparison.Ordinal))
            .Select(f => f["lang/".Length..^".scenes.json".Length]).ToList();
        Rebuild();
    }

    public IReadOnlyList<string> Languages { get; }
    public ObservableCollection<SceneItem> Items { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _onlyProblems;
    [ObservableProperty] private SceneItem? _selected;

    public int SceneCount => _all.Count(i => !i.Rare);
    public int RareCount => _all.Count(i => i.Rare);
    public int ProblemCount => _all.Count(i => i.Problem);
    public string Summary => EditorState.T.Format("editor.scenes.summary", SceneCount, RareCount, ProblemCount);

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnOnlyProblemsChanged(bool value) => ApplyFilter();

    internal JsonObject Root => Workspace.Document(File) as JsonObject ?? new JsonObject();

    internal JsonArray Texts(string language, string key) =>
        (Workspace.Document($"lang/{language}.scenes.json") as JsonObject)?[key] as JsonArray ?? new JsonArray();

    private void Rebuild()
    {
        var keep = Selected is { } s ? (s.Rare, s.Index) : ((bool, int)?)null;
        _all = new List<SceneItem>();
        foreach (var (list, rare) in new[] { ("scenes", false), ("rare", true) })
            if (Root[list] is JsonArray arr)
                for (var i = 0; i < arr.Count; i++) _all.Add(new SceneItem(this, rare, i));
        ApplyFilter();
        Selected = keep is { } k ? Items.FirstOrDefault(i => i.Rare == k.Item1 && i.Index == k.Item2) ?? Items.FirstOrDefault() : Items.FirstOrDefault();
    }

    /// <summary>Nach Änderungen: Werte an Ort und Stelle neu lesen, damit Eingabefelder den Fokus behalten.</summary>
    public override void Refresh()
    {
        var counts = (Root["scenes"] as JsonArray)?.Count ?? 0;
        var rare = (Root["rare"] as JsonArray)?.Count ?? 0;
        if (counts != _all.Count(i => !i.Rare) || rare != _all.Count(i => i.Rare)) { Rebuild(); return; }
        foreach (var item in _all) item.Reload();
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(Summary));
    }

    private void ApplyFilter()
    {
        // Die Auswahl bleibt erhalten, solange die Szene noch zum Filter passt.
        var selected = Selected;
        Items.Clear();
        foreach (var item in _all.Where(i => (!OnlyProblems || i.Problem) &&
                     (Filter.Length == 0 || i.Label.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                      i.Steps.Any(st => st.Key.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                                        Languages.Any(l => st[l].Contains(Filter, StringComparison.OrdinalIgnoreCase))))))
            Items.Add(item);
        Selected = selected is not null && Items.Contains(selected) ? selected : Items.FirstOrDefault();
        OnPropertyChanged(nameof(SceneCount));
        OnPropertyChanged(nameof(RareCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(Summary));
    }

    internal void Apply(JsonEdit edit) => Workspace.Apply(edit);

    /// <summary>Neue Szene als Kopie der gewählten (oder leer), direkt dahinter.</summary>
    [RelayCommand]
    private void Add()
    {
        var rare = Selected?.Rare ?? false;
        var list = rare ? "rare" : "scenes";
        var arr = Root[list] as JsonArray;
        var index = Selected is { } s ? s.Index + 1 : arr?.Count ?? 0;
        var copy = Selected?.Node.DeepClone() as JsonObject ?? new JsonObject { ["on"] = new JsonArray("kickoff"), ["steps"] = new JsonArray() };
        copy["id"] = (copy["id"]?.ToString() ?? "scene") + ".copy";
        if (arr is null) Apply(new SetValue(File, JsonPath.Root.Then(list), new JsonArray(copy)));
        else Apply(new InsertItem(File, JsonPath.Root.Then(list), index, copy));
        Rebuild();
        Selected = Items.FirstOrDefault(i => i.Rare == rare && i.Index == index);
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } s) return;
        Apply(new RemoveItem(File, JsonPath.Root.Then(s.Rare ? "rare" : "scenes"), s.Index));
        Rebuild();
    }
}
