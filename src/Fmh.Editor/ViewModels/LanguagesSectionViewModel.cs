using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core.Data.Editing;

namespace Fmh.Editor.ViewModels;

/// <summary>Texte je Sprache nebeneinander; fehlende Texte und abweichende Platzhalter sind markiert und filterbar.</summary>
public sealed partial class LanguagesSectionViewModel : SectionViewModel
{
    private List<LanguageRow> _all = new();

    public LanguagesSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.languages")
    {
        Languages = ws.Files.Where(f => f.StartsWith("lang/", StringComparison.Ordinal) && !f.EndsWith(".commentary.json", StringComparison.Ordinal))
            .Select(f => f["lang/".Length..^".json".Length]).ToList();
        Rebuild();
    }

    public IReadOnlyList<string> Languages { get; }
    public ObservableCollection<LanguageRow> Rows { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _onlyProblems;
    public int ProblemCount => _all.Count(r => r.Problem);

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnOnlyProblemsChanged(bool value) => ApplyFilter();

    private void Rebuild()
    {
        var docs = Languages.ToDictionary(l => l, l => Workspace.Document($"lang/{l}.json") as JsonObject ?? new JsonObject());
        var keys = docs.Values.SelectMany(d => d.Select(kv => kv.Key)).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        _all = keys.Select(k => new LanguageRow(this, k, Languages.ToDictionary(l => l, l => docs[l][k]?.ToString()))).ToList();
        ApplyFilter();
    }

    public override void Refresh()
    {
        foreach (var l in Languages)
        {
            var doc = Workspace.Document($"lang/{l}.json") as JsonObject ?? new JsonObject();
            foreach (var row in _all) row.Update(l, doc[row.Key]?.ToString());
        }
        OnPropertyChanged(nameof(ProblemCount));
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var r in _all.Where(r => (!OnlyProblems || r.Problem) &&
                     (Filter.Length == 0 || r.Key.Contains(Filter, StringComparison.OrdinalIgnoreCase) || r.Texts.Values.Any(t => t?.Contains(Filter, StringComparison.OrdinalIgnoreCase) == true))))
            Rows.Add(r);
        OnPropertyChanged(nameof(ProblemCount));
    }

    internal void Set(string language, string key, string text) =>
        Workspace.Apply(new SetValue($"lang/{language}.json", JsonPath.Root.Then(key), JsonValue.Create(text)));
}

/// <summary>Ein Textschlüssel mit seinen Übersetzungen (Indexer je Sprachcode für die Tabellenspalten).</summary>
public sealed class LanguageRow : ObservableObject
{
    private readonly LanguagesSectionViewModel _owner;

    public LanguageRow(LanguagesSectionViewModel owner, string key, Dictionary<string, string?> texts) => (_owner, Key, Texts) = (owner, key, texts);

    public string Key { get; }
    public Dictionary<string, string?> Texts { get; }

    public string this[string language]
    {
        get => Texts.GetValueOrDefault(language) ?? "";
        set
        {
            if (value == this[language]) return;
            _owner.Set(language, Key, value);
        }
    }

    internal void Update(string language, string? text)
    {
        if (Texts.GetValueOrDefault(language) == text) return;
        Texts[language] = text;
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(ProblemText));
    }

    /// <summary>Platzhalter {0}, {name} … eines Textes.</summary>
    private static string Placeholders(string? s) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(s ?? "", @"\{[^}]+\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal));

    public bool Problem => Texts.Values.Any(string.IsNullOrEmpty) || Texts.Values.Select(Placeholders).Distinct().Count() > 1;

    public string ProblemText => Texts.Values.Any(string.IsNullOrEmpty) ? Services.EditorState.T["editor.languages.missing"]
        : Problem ? Services.EditorState.T["editor.languages.placeholders"] : "";
}
