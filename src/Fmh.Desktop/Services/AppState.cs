using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Match;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Desktop.Services;

/// <summary>Anwendungsweiter Zustand: geladene Daten, Matchengine, Spielstandverzeichnis, laufende Sitzung.</summary>
public sealed class AppState
{
    private static AppState? _current;

    private AppState(GameData data, string saveDirectory)
    {
        Data = data;
        SaveDirectory = saveDirectory;
    }

    public static AppState Current => _current ?? throw new InvalidOperationException("AppState nicht initialisiert.");

    public static void Initialise(GameData data, string saveDirectory) => _current = new AppState(data, saveDirectory);

    public GameData Data { get; }
    public Texts T => Data.Texts;
    public IMatchEngine Engine { get; } = new TextMatchEngine();
    public string SaveDirectory { get; }
    public GameSession? Session { get; set; }

    public string SaveCurrent()
    {
        var s = Session ?? throw new InvalidOperationException("Kein Spiel geladen.");
        s.SyncRng();
        if (s.World.Rules.Ironman) return SaveGame.IronmanSave(s.World, SaveDirectory);
        var name = s.HumanClub.Key + "-" + new string(s.HumanManager.Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var path = Path.Combine(SaveDirectory, name + SaveGame.Extension);
        SaveGame.Save(s.World, path);
        return path;
    }

    public string Autosave()
    {
        var s = Session ?? throw new InvalidOperationException("Kein Spiel geladen.");
        s.SyncRng();
        return SaveGame.Autosave(s.World, SaveDirectory);
    }
}
