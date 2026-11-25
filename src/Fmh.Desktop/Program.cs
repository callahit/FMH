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

        CrashHandler.Install();
        GameData data;
        try
        {
            data = PackageLoader.Load(PackageLoader.FindDataRoot(dataPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException or System.Text.Json.JsonException)
        {
            return StartupError(args, new[] { ex.Message });
        }
        var problems = DataValidator.Validate(data);
        if (problems.Count > 0) return StartupError(args, problems);

        AppState.Initialise(data, savePath ?? SaveGame.DefaultDirectory(), language);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Datenfehler beim Start: auf der Konsole und in einem Fenster (ohne Konsole sähe sie sonst niemand).
    /// Die Sprachdateien sind dann womöglich selbst betroffen, deshalb steht der Hinweis zweisprachig im Code.
    /// </summary>
    private static int StartupError(string[] args, IReadOnlyList<string> problems)
    {
        Console.Error.WriteLine("Fehler in den Datenpaketen / Errors in the data packages:");
        foreach (var p in problems) Console.Error.WriteLine("  - " + p);
        if (Environment.GetEnvironmentVariable("FMH_NO_GUI") is { Length: > 0 }) return 2;
        App.StartupProblems = problems;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 2;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
