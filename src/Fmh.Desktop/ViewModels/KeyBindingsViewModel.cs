using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed partial class KeyBindingRow : ObservableObject
{
    public required string Action { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] private string _gesture = "";
    [ObservableProperty] private bool _isCapturing;
}

/// <summary>Tastenbelegung ändern (AP 12b): Zeile wählen, dann die neue Taste drücken; Esc bricht ab.</summary>
public sealed partial class KeyBindingsViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    public KeyBindingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Rows = KeyMap.Current.Actions.Select(a => new KeyBindingRow { Action = a, Label = T["keys." + a] }).ToList();
        Load();
    }

    public IReadOnlyList<KeyBindingRow> Rows { get; }

    [ObservableProperty] private string _message = "";

    public KeyBindingRow? Capturing => Rows.FirstOrDefault(r => r.IsCapturing);

    private void Load()
    {
        var map = KeyMap.Current;
        foreach (var r in Rows) r.Gesture = map.Display(r.Action, T);
    }

    [RelayCommand]
    private void Capture(KeyBindingRow? row)
    {
        foreach (var r in Rows) r.IsCapturing = r == row;
        Message = row is null ? "" : T.Format("keys.press", row.Label);
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (Capturing is not { } row) return false;
        if (key == Key.Escape)
        {
            Capture(null);
            return true;
        }
        if (KeyMap.IsModifierOnly(key)) return true;
        var gesture = new KeyGesture(key, modifiers);
        if (KeyMap.IsReserved(gesture))
        {
            Message = T.Format("keys.reserved", KeyMap.Display(gesture, T));
            return true;
        }
        var other = KeyMap.Rebind(Data.Ui, State.Settings, row.Action, gesture);
        State.SaveSettings();
        Capture(null);
        Load();
        if (other is not null) Message = T.Format("keys.swapped", T["keys." + other], KeyMap.Current.Display(other, T));
        return true;
    }

    [RelayCommand]
    private void Reset()
    {
        State.Settings.KeyBindings.Clear();
        State.SaveSettings();
        Capture(null);
        Load();
    }

    /// <summary>Zurück zu den Einstellungen; Menü neu aufbauen, damit die Tasten im Menü stimmen.</summary>
    [RelayCommand]
    private void Close()
    {
        var shell = _shell.Recreate();
        MainWindowViewModel.Instance?.Navigate(shell);
        MainWindowViewModel.Instance?.ShowDialog(new OptionsViewModel(shell));
    }
}
