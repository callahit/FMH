using Fmh.Core.Data;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;
using Fmh.Ui.Views;

namespace Fmh.Ui;

/// <summary>
/// FMH – Fußball-Manager im Textmodus.
///   fmh [--data pfad] [--saves pfad]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string? dataPath = null;
        string? savePath = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data") dataPath = args[i + 1];
            if (args[i] == "--saves") savePath = args[i + 1];
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

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine(data.Texts["app.needsTerminal"]);
            return 1;
        }

        var app = new App(data, new TextMatchEngine(), savePath ?? SaveGame.DefaultDirectory());
        app.Push(new MainMenuView(app));
        app.RunConsole();
        return 0;
    }
}
