using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>
/// Arbeitsbereich eines Pakets: Bereiche links, Rückgängig/Wiederholen/Speichern oben, Prüfung läuft nach jeder
/// Änderung (leicht verzögert) und zeigt die Fehlerzahl.
/// </summary>
public sealed partial class WorkspaceViewModel : ViewModelBase
{
    private readonly DispatcherTimer _validateTimer;

    public PackageWorkspace Workspace { get; }
    public IReadOnlyList<SectionViewModel> Sections { get; }
    public ValidationSectionViewModel Validation { get; }
    [ObservableProperty] private SectionViewModel _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _dirty;

    private readonly Action _close;

    public WorkspaceViewModel(PackageWorkspace ws, Action close)
    {
        Workspace = ws;
        _close = close;
        Validation = new ValidationSectionViewModel(ws);
        Sections = new SectionViewModel[]
        {
            new PackageSectionViewModel(ws), new ClubsSectionViewModel(ws), new FilesSectionViewModel(ws),
            new LanguagesSectionViewModel(ws), Validation, new DiffSectionViewModel(ws),
        };
        _selected = Sections[0];
        _validateTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => RunValidation());
        ws.Changed += OnChanged;
        RunValidation();
    }

    public string Title => EditorState.F("editor.workspace.title", Workspace.Manifest.Name, Workspace.Manifest.Id);

    private void OnChanged()
    {
        Dirty = true;
        Status = Workspace.UndoLabel ?? "";
        foreach (var s in Sections) s.Refresh();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        _validateTimer.Stop();
        _validateTimer.Start();
    }

    /// <summary>Prüfung sofort ausführen (z. B. nach dem Laden oder in Tests).</summary>
    public void RunValidation()
    {
        _validateTimer.Stop();
        Validation.Run();
        OnPropertyChanged(nameof(ErrorSummary));
    }

    public string ErrorSummary => Validation.Errors.Count == 0
        ? EditorState.T["editor.validation.ok"]
        : EditorState.F("editor.validation.count", Validation.Errors.Count);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Workspace.Undo();

    private bool CanUndo() => Workspace.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Workspace.Redo();

    private bool CanRedo() => Workspace.CanRedo;

    [RelayCommand]
    private void Save()
    {
        Workspace.Save();
        Dirty = false;
        Status = EditorState.F("editor.saved", Workspace.PackageDirectory);
    }

    [RelayCommand]
    private void Close() => _close();
}

/// <summary>Ein Bereich im Arbeitsbereich (Navigation links).</summary>
public abstract class SectionViewModel : ViewModelBase
{
    protected SectionViewModel(PackageWorkspace ws, string titleKey)
    {
        Workspace = ws;
        Title = EditorState.T[titleKey];
    }

    public PackageWorkspace Workspace { get; }
    public string Title { get; }

    /// <summary>Nach jeder Änderung im Arbeitsbereich neu einlesen.</summary>
    public virtual void Refresh() { }
}
