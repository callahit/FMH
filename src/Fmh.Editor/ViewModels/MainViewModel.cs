using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Hauptfenster: erst die Paketauswahl, danach der Arbeitsbereich.</summary>
public sealed partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _page;

    public MainViewModel(string? dataRoot = null)
    {
        _page = new StartViewModel(dataRoot ?? EditorState.DataRoot, Open);
    }

    public void Open(PackageWorkspace ws) => Page = new WorkspaceViewModel(ws, () => Page = new StartViewModel(ws.DataRoot, Open));
}
