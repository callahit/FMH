using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Desktop;

public sealed class App : Application
{
    /// <summary>Gesetzt, wenn die Datenpakete fehlerhaft sind: statt des Spiels erscheint nur die Fehlerliste.</summary>
    public static IReadOnlyList<string>? StartupProblems { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Palette.Apply(Palette.Classic.Name);
        AccessibleNames.Install();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = StartupProblems is { } problems
                ? StartupErrorWindow.Create(problems)
                : new MainWindow { DataContext = new MainWindowViewModel() };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
