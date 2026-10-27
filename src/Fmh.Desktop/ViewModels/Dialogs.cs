using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace Fmh.Desktop.ViewModels;

/// <summary>Hinweis mit mehreren Zeilen.</summary>
public sealed partial class MessageDialogViewModel : ViewModelBase
{
    private readonly Action? _onClose;

    public MessageDialogViewModel(string title, IEnumerable<string> lines, Action? onClose = null)
    {
        Title = title;
        Lines = lines.ToList();
        _onClose = onClose;
    }

    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }

    [RelayCommand]
    private void Close()
    {
        MainWindowViewModel.Instance?.CloseDialog();
        _onClose?.Invoke();
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (key is Key.Enter or Key.Escape or Key.Space) { Close(); return true; }
        return false;
    }
}

public sealed record ChoiceOption(string Label, string Detail, object? Tag = null);

/// <summary>Auswahl aus einer Liste (Klick oder Pfeiltasten + Enter).</summary>
public sealed partial class ChoiceDialogViewModel : ViewModelBase
{
    private readonly Action<int> _onSelect;

    public ChoiceDialogViewModel(string title, IReadOnlyList<ChoiceOption> options, Action<int> onSelect, int selected = 0)
    {
        Title = title;
        Options = options;
        _onSelect = onSelect;
        _selectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
    }

    public string Title { get; }
    public IReadOnlyList<ChoiceOption> Options { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private int _selectedIndex;

    [RelayCommand]
    private void Choose(ChoiceOption? option)
    {
        var idx = option is null ? SelectedIndex : Options.ToList().IndexOf(option);
        if (idx < 0) return;
        MainWindowViewModel.Instance?.CloseDialog();
        _onSelect(idx);
    }

    [RelayCommand]
    private void Cancel() => MainWindowViewModel.Instance?.CloseDialog();

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Up: SelectedIndex = Math.Max(0, SelectedIndex - 1); return true;
            case Key.Down: SelectedIndex = Math.Min(Options.Count - 1, SelectedIndex + 1); return true;
            case Key.Enter: Choose(null); return true;
        }
        return false;
    }
}

/// <summary>Ja/Nein-Abfrage.</summary>
public sealed partial class ConfirmDialogViewModel : ViewModelBase
{
    private readonly Action _onYes;

    public ConfirmDialogViewModel(string title, string text, Action onYes)
    {
        Title = title;
        Text = text;
        _onYes = onYes;
    }

    public string Title { get; }
    public string Text { get; }

    [RelayCommand]
    private void Yes()
    {
        MainWindowViewModel.Instance?.CloseDialog();
        _onYes();
    }

    [RelayCommand]
    private void No() => MainWindowViewModel.Instance?.CloseDialog();
}
