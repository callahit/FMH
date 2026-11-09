using System.Collections.ObjectModel;
using Fmh.Core.Data.Editing;

namespace Fmh.Editor.ViewModels;

/// <summary>Ergebnis der Prüfung wie beim Spielstart (Datenvalidator).</summary>
public sealed class ValidationSectionViewModel : SectionViewModel
{
    public ValidationSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.validation") { }

    public ObservableCollection<string> Errors { get; } = new();

    public void Run()
    {
        Errors.Clear();
        foreach (var e in Workspace.Validate()) Errors.Add(e);
        OnPropertyChanged(nameof(IsValid));
    }

    public bool IsValid => Errors.Count == 0;
}
