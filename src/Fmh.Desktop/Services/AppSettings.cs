using System.Text.Json;
using Fmh.Core.Data;

namespace Fmh.Desktop.Services;

/// <summary>
/// Geräteweite Einstellungen außerhalb der Spielstände (Sprache, Bedienung). Liegen als settings.json neben dem
/// Spielstandordner.
/// </summary>
public sealed class AppSettings
{
    public string? Language { get; set; }

    /// <summary>Eigene Tastenbelegung (Aktion → Taste); fehlende Aktionen nutzen den Standard aus rules/ui.json.</summary>
    public Dictionary<string, string> KeyBindings { get; set; } = new();

    /// <summary>Skalierung der Oberfläche (1 = normal).</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>Tutorial-Hinweise zeigen.</summary>
    public bool Tutorial { get; set; } = true;

    /// <summary>Bereits bestätigte Tutorial-Hinweise.</summary>
    public List<string> TutorialSeen { get; set; } = new();

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Defekte Einstellungen sind kein Grund, nicht zu starten.
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht speicherbar (z. B. schreibgeschützt): Einstellungen gelten dann nur für diese Sitzung.
        }
    }

    /// <summary>Sprache beim ersten Start: aus der Umgebung (LANG, LC_ALL), sonst die Standardsprache der Daten.</summary>
    public static string DetectLanguage(GameData data)
    {
        foreach (var v in new[] { "LC_ALL", "LC_MESSAGES", "LANG", "LANGUAGE" })
        {
            var value = Environment.GetEnvironmentVariable(v);
            if (string.IsNullOrEmpty(value) || value is "C" or "POSIX") continue;
            var code = value.Split('_', '.', '-', ':')[0].ToLowerInvariant();
            if (data.Languages.ContainsKey(code)) return code;
        }
        return data.DefaultLanguage;
    }
}
