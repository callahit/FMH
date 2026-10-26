using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        // Zahlenfelder und Formatierungen mit deutschen Trennzeichen (Fmt.Culture, auch ohne ICU).
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = Fmh.Core.Fmt.Culture;
        System.Globalization.CultureInfo.CurrentCulture = Fmh.Core.Fmt.Culture;
        AvaloniaXamlLoader.Load(this);
        Palette.Apply(Palette.Classic.Name);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
