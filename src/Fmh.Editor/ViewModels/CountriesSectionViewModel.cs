using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>
/// Länder und Wettbewerbe: Pyramide je Formatversion (Ebenen, Ligen, Staffeln, Auf- und Abstieg), neue Formatversion ab
/// einer Saison, Assistent „Neues Land“ und die Prüfmeldungen des Landes. Einzelheiten werden im Baum bearbeitet.
/// </summary>
public sealed partial class CountriesSectionViewModel : SectionViewModel
{
    private readonly Action<string> _openInTree;

    public CountriesSectionViewModel(PackageWorkspace ws, Action<string> openInTree) : base(ws, "editor.section.countries")
    {
        _openInTree = openInTree;
        Refresh();
        _file = Files.FirstOrDefault() ?? "";
        LoadPyramid();
    }

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<PyramidLevel> Levels { get; } = new();
    public ObservableCollection<string> Problems { get; } = new();
    [ObservableProperty] private string _file;
    [ObservableProperty] private string _info = "";
    [ObservableProperty] private string _newVersionSeason = "2030";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _wizardCode = "NL";
    [ObservableProperty] private string _wizardName = "Neuland";
    [ObservableProperty] private string _wizardTeams = "16, 16, 16, 16";
    [ObservableProperty] private bool _wizardCup = true;

    partial void OnFileChanged(string value) => LoadPyramid();

    public override void Refresh()
    {
        var files = CountryTools.CountryFiles(Workspace);
        if (!files.SequenceEqual(Files))
        {
            Files.Clear();
            foreach (var f in files) Files.Add(f);
        }
        LoadPyramid();
    }

    private JsonObject? Country => string.IsNullOrEmpty(File) ? null : Workspace.Document(File) as JsonObject;

    private void LoadPyramid()
    {
        Levels.Clear();
        if (Country is not { } c) { Info = ""; return; }
        foreach (var l in CountryTools.Pyramid(c)) Levels.Add(l);
        var to = c["validTo"] is { } t ? t.ToString() : "…";
        Info = EditorState.F("editor.countries.info", c["name"]?.ToString() ?? "", c["code"]?.ToString() ?? "", c["validFrom"]?.ToString() ?? "", to,
            Levels.Sum(l => l.Teams), (c["cups"] as JsonArray)?.Count ?? 0, (c["playoffs"] as JsonArray)?.Count ?? 0);
    }

    /// <summary>Prüfmeldungen, die dieses Land betreffen (Code, Ligen oder Staffeln im Text).</summary>
    public void ShowProblems(IEnumerable<string> errors)
    {
        Problems.Clear();
        if (Country is not { } c) return;
        var names = Levels.SelectMany(l => l.Divisions).SelectMany(d => d.Groups.Append(d.Name).Append(d.Id)).Append($"'{c["code"]}'").ToList();
        foreach (var e in errors.Where(e => names.Any(n => e.Contains(n, StringComparison.Ordinal)))) Problems.Add(e);
    }

    [RelayCommand]
    private void NewVersion()
    {
        try
        {
            if (!int.TryParse(NewVersionSeason, out var season)) throw new ArgumentException(EditorState.T["editor.countries.badSeason"]);
            var target = CountryTools.NewFormatVersion(Workspace, File, season);
            File = target;
            Message = EditorState.F("editor.countries.versionCreated", target);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Message = ex.Message; }
    }

    [RelayCommand]
    private void CreateCountry()
    {
        try
        {
            var teams = WizardTeams.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var files = CountryWizard.Create(Workspace, new NewCountrySpec(WizardCode, WizardName, teams, WizardCup));
            File = files[0];
            Message = EditorState.F("editor.countries.created", WizardCode.ToUpperInvariant(), teams.Sum());
        }
        catch (FormatException) { Message = EditorState.T["editor.countries.badTeams"]; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Message = ex.Message; }
    }

    [RelayCommand]
    private void EditInTree() => _openInTree(File);
}
