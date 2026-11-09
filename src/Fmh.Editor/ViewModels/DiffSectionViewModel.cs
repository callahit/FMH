using System.Collections.ObjectModel;
using Fmh.Core.Data.Editing;

namespace Fmh.Editor.ViewModels;

/// <summary>Unterschiedsansicht: was das Paket gegenüber seinen Abhängigkeiten ändert, je Datei und Feld.</summary>
public sealed class DiffSectionViewModel : SectionViewModel
{
    public DiffSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.diff") => Refresh();

    public ObservableCollection<DiffLine> Lines { get; } = new();

    public override void Refresh()
    {
        Lines.Clear();
        foreach (var file in Workspace.ChangedFiles)
            foreach (var d in Workspace.Diff(file).Take(500))
                Lines.Add(new DiffLine(file, d.Path, d.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => "~" }, d.Old ?? "", d.New ?? ""));
    }
}

public sealed record DiffLine(string File, string Path, string Kind, string Old, string New);
