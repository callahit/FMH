using Avalonia;
using Fmh.Core.Data;
using Fmh.Core.Persistence;
using Fmh.Desktop.Services;

namespace Fmh.Desktop;

/// <summary>FMH – Fußball-Manager. Start: fmh [--data pfad] [--saves pfad] [--lang de|en|...]</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? dataPath = null, savePath = null, language = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data") dataPath = args[i + 1];
            if (args[i] == "--saves") savePath = args[i + 1];
            if (args[i] == "--lang") language = args[i + 1];
        }

        GameData data;
        try
        {
            data = PackageLoader.Load(PackageLoader.FindDataRoot(dataPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"Daten konnten nicht geladen werden: {ex.Message}");
            return 2;
        }
        var problems = DataValidator.Validate(data);
        if (problems.Count > 0)
        {
            Console.Error.WriteLine("Fehler in den Datenpaketen:");
            foreach (var p in problems) Console.Error.WriteLine("  - " + p);
            return 2;
        }

        AppState.Initialise(data, savePath ?? SaveGame.DefaultDirectory(), language);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
