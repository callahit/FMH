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
        set { if (value != Name) Workspace.Apply(new SetManifestField("name", value)); }
    }

    public string Version
    {
        get => Workspace.Manifest.Version;
        set { if (value != Version) Workspace.Apply(new SetManifestField("version", value)); }
    }

    public ObservableCollection<string> ChangedFiles { get; } = new();

    public override void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Version));
        ChangedFiles.Clear();
        foreach (var f in Workspace.ChangedFiles) ChangedFiles.Add(f);
    }
}
