using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        // Texteingaben behalten ihre Tasten.
        if (e.Source is TextBox && e.Key is not (Key.Escape or Key.Enter)) return;
        // Enter und Leertaste lösen die fokussierte Schaltfläche oder Auswahl aus, nicht das Kürzel des Bildschirms
        // (sonst lud Enter auf „Neues Spiel“ den ersten Spielstand).
        if (e.Key is Key.Enter or Key.Space && OwnsActivationKeys(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement())) return;
        if (DataContext is MainWindowViewModel vm && vm.HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    /// <summary>Steuerelemente, die Enter/Leertaste selbst auswerten (Schaltflächen, Umschalter, Aufklapplisten, Menüs).</summary>
    public static bool OwnsActivationKeys(object? focused) =>
        focused is Button or ToggleButton or ComboBox or MenuItem or Slider;
}
