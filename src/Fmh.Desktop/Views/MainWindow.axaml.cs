using Avalonia.Controls;
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
        if (DataContext is MainWindowViewModel vm && vm.HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
    }
}
