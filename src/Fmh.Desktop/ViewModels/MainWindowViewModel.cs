using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fmh.Desktop.ViewModels;

/// <summary>Fensterrahmen: aktueller Bildschirm und darüberliegender Dialog.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    public static MainWindowViewModel? Instance { get; private set; }

    [ObservableProperty]
    private ViewModelBase _screen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private ViewModelBase? _dialog;

    public MainWindowViewModel()
    {
        Instance = this;
        _screen = new StartViewModel(this);
    }

    public bool HasDialog => Dialog is not null;

    public void Navigate(ViewModelBase screen)
    {
        Dialog = null;
        Screen = screen;
    }

    public void ShowDialog(ViewModelBase dialog) => Dialog = dialog;

    public void CloseDialog() => Dialog = null;

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (Dialog is { } d) return d.HandleKey(key, modifiers) || (key == Key.Escape && Close());
        return Screen.HandleKey(key, modifiers);
    }

    private bool Close()
    {
        Dialog = null;
        return true;
    }
}
