using Avalonia;
using Fmh.Core.Data;
using Fmh.Editor.Services;

namespace Fmh.Editor;

/// <summary>FMH-Datenbank-Editor (AP 6). Start: fmh-editor [--data pfad] [--lang de|en|...]</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? dataPath = null, language = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data") dataPath = args[i + 1];
            if (args[i] == "--lang") language = args[i + 1];
        }
        try
        {
            EditorState.Initialise(PackageLoader.FindDataRoot(dataPath), language ?? "de");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"Daten konnten nicht geladen werden: {ex.Message}");
            return 2;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
