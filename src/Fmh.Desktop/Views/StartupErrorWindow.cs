using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fmh.Desktop.Views;

/// <summary>Fenster mit den Fehlern der Datenpakete; ohne gültige Daten gibt es keine Sprachtexte, deshalb zweisprachig.</summary>
public static class StartupErrorWindow
{
    public static Window Create(IReadOnlyList<string> problems)
    {
        var list = new TextBox
        {
            Text = string.Join(Environment.NewLine, problems.Select(p => "• " + p)),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
        };
        var close = new Button { Content = "Beenden / Quit", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var header = new TextBlock
        {
            Text = "FMH kann nicht starten, weil die Datenpakete Fehler enthalten.\nFMH cannot start because the data packages contain errors.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        close.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(header);
        panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { Content = list });
        var window = new Window { Title = "FMH – Datenfehler / Data errors", Width = 720, Height = 480, Content = panel };
        close.Click += (_, _) => window.Close();
        return window;
    }
}
