using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Paketauswahl: eigenes Paket öffnen oder ein neues über ausgewählten Paketen anlegen.</summary>
public sealed partial class StartViewModel : ViewModelBase
{
    private readonly Action<PackageWorkspace> _open;

    public string DataRoot { get; }
    public ObservableCollection<PackageItem> Packages { get; } = new();
    [ObservableProperty] private string _newId = "mein-paket";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _error = "";

    public StartViewModel(string dataRoot, Action<PackageWorkspace> open)
    {
        DataRoot = dataRoot;
        _open = open;
        foreach (var m in PackageWorkspace.Packages(dataRoot))
            Packages.Add(new PackageItem(m.Id, m.Name, m.CreatedWith == PackageWorkspace.Tool) { Base = m.Id == "base" });
    }

    [RelayCommand]
    private void Open(PackageItem item)
    {
        try { _open(PackageWorkspace.Open(DataRoot, item.Id)); }
        catch (InvalidOperationException ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void Create()
    {
        try
        {
            var deps = Packages.Where(p => p.Base).Select(p => p.Id).ToList();
            if (deps.Count == 0) { Error = EditorState.T["editor.start.needBase"]; return; }
            _open(PackageWorkspace.Create(DataRoot, NewId.Trim(), string.IsNullOrWhiteSpace(NewName) ? NewId.Trim() : NewName.Trim(), deps));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Error = ex.Message; }
    }
}

/// <summary>Paket in der Auswahl; <see cref="Base"/> = als Grundlage für ein neues Paket ausgewählt.</summary>
public sealed partial class PackageItem : ObservableObject
{
    public PackageItem(string id, string name, bool editable) => (Id, Name, Editable) = (id, name, editable);

    public string Id { get; }
    public string Name { get; }
    public bool Editable { get; }
    [ObservableProperty] private bool _base;
}
