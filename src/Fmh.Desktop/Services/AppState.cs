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

    private AppState(GameData data, string saveDirectory, string settingsFile)
    {
        Data = data;
        SaveDirectory = saveDirectory;
        SettingsFile = settingsFile;
        Settings = AppSettings.Load(settingsFile);
    }

    public static AppState Current => _current ?? throw new InvalidOperationException("AppState nicht initialisiert.");

    /// <summary>
    /// Initialisiert den Zustand. Die Sprache kommt aus dem Parameter (z. B. --lang), sonst aus den gespeicherten
    /// Einstellungen, sonst aus der Systemumgebung.
    /// </summary>
    public static void Initialise(GameData data, string saveDirectory, string? language = null, string? settingsFile = null)
    {
        settingsFile ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(saveDirectory)) ?? saveDirectory, "settings.json");
        _current = new AppState(data, saveDirectory, settingsFile);
        var lang = language ?? _current.Settings.Language ?? AppSettings.DetectLanguage(data);
        _current.ApplyLanguage(lang);
    }

    public GameData Data { get; }
    public Texts T => Data.Texts;
    public IMatchEngine Engine { get; } = new TextMatchEngine();
    public string SaveDirectory { get; }
    public string SettingsFile { get; }
    public AppSettings Settings { get; }

    /// <summary>Verfügbare Sprachen, Anzeige in der jeweiligen Sprache ("Deutsch", "English").</summary>
    public IReadOnlyList<LanguagePack> Languages => Data.Languages.Values.OrderBy(l => l.Code == Data.DefaultLanguage ? 0 : 1).ThenBy(l => l.Name).ToList();

    public string Language => Data.Language;

    /// <summary>Sprache wechseln und dauerhaft merken. Die Oberfläche baut sich danach neu auf (MainWindowViewModel.Reload).</summary>
    public void SetLanguage(string code)
    {
        ApplyLanguage(code);
        Settings.Language = Data.Language;
        Settings.Save(SettingsFile);
    }

    private void ApplyLanguage(string code)
    {
        Data.UseLanguage(code);
        // Zahlenfelder (NumericUpDown) und Formatierungen der Oberfläche folgen der Sprache.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = Fmh.Core.Fmt.Culture;
        System.Globalization.CultureInfo.CurrentCulture = Fmh.Core.Fmt.Culture;
    }
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
