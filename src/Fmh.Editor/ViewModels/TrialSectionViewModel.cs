using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Probelauf: eine Saison des gewählten Landes mit dem aktuellen Paketstand simulieren, Kennzahlen je Liga.</summary>
public sealed partial class TrialSectionViewModel : SectionViewModel
{
    private CancellationTokenSource? _cancel;

    public TrialSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.trial") => Refresh();

    public ObservableCollection<string> Countries { get; } = new();
    public ObservableCollection<TrialLeague> Leagues { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();
    [ObservableProperty] private string? _country;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCommand))] private bool _running;

    public override void Refresh()
    {
        var codes = CountryTools.CountryFiles(Workspace).Select(f => (Workspace.Document(f) as JsonObject)?["code"]?.ToString())
            .OfType<string>().Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (codes.SequenceEqual(Countries)) return;
        Countries.Clear();
        foreach (var c in codes) Countries.Add(c);
        Country ??= Countries.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        Running = true;
        Errors.Clear();
        Leagues.Clear();
        Status = EditorState.T["editor.trial.running"];
        _cancel = new CancellationTokenSource();
        var progress = new Progress<string>(date => Status = EditorState.F("editor.trial.progress", date));
        try
        {
            var code = Country;
            var result = await Task.Run(() => TrialSeason.Run(Workspace, code, 1, progress, _cancel.Token));
            foreach (var e in result.Errors) Errors.Add(e);
            foreach (var l in result.Leagues) Leagues.Add(l);
            Status = result.Errors.Count > 0 ? EditorState.T["editor.trial.failed"]
                : EditorState.F("editor.trial.done", result.Leagues.Count, result.Cups, result.Duration.TotalSeconds.ToString("F0"));
        }
        catch (OperationCanceledException) { Status = EditorState.T["editor.trial.cancelled"]; }
        finally { Running = false; }
    }

    private bool CanRun() => !Running;

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    /// <summary>Probelauf ohne Oberflächen-Thread abwarten (für Tests).</summary>
    public Task RunAndWait() => RunCommand.ExecuteAsync(null);
}
