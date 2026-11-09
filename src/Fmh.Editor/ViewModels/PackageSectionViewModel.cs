using System.Collections.ObjectModel;
using Fmh.Core.Data.Editing;

namespace Fmh.Editor.ViewModels;

/// <summary>Übersicht des Pakets: Kennung, Grundlage, geänderte Dateien.</summary>
public sealed class PackageSectionViewModel : SectionViewModel
{
    public PackageSectionViewModel(PackageWorkspace ws) : base(ws, "editor.section.package") => Refresh();

    public string Id => Workspace.Manifest.Id;
    public string DependsOn => string.Join(", ", Workspace.Manifest.DependsOn);
    public string Directory => Workspace.PackageDirectory;

    public string Name
    {
        get => Workspace.Manifest.Name;
        set { Workspace.Manifest.Name = value; OnPropertyChanged(); }
    }

    public string Version
    {
        get => Workspace.Manifest.Version;
        set { Workspace.Manifest.Version = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> ChangedFiles { get; } = new();

    public override void Refresh()
    {
        ChangedFiles.Clear();
        foreach (var f in Workspace.ChangedFiles) ChangedFiles.Add(f);
    }
}
