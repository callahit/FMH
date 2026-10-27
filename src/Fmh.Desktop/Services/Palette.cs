using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Fmh.Desktop.Services;

/// <summary>
/// Farbthemen (Kapitel 11): "Klassik" (dunkles Blau wie Anstoß), "Hell" und "Terminal" (grün auf schwarz).
/// Die Farben liegen als Ressourcen in der Anwendung und werden beim Themenwechsel ausgetauscht.
/// </summary>
public sealed record Palette(
    string Name, ThemeVariant Variant,
    Color Bg, Color Surface, Color Surface2, Color Line, Color Text, Color Muted,
    Color Accent, Color Gold, Color Good, Color Warn, Color Bad, Color Pitch, Color PitchLine)
{
    public static readonly Palette Classic = new("Klassik", ThemeVariant.Dark,
        Bg: Color.Parse("#0E1524"), Surface: Color.Parse("#16213A"), Surface2: Color.Parse("#1F2D4D"), Line: Color.Parse("#2B3B61"),
        Text: Color.Parse("#E8EDF7"), Muted: Color.Parse("#8E9BB8"), Accent: Color.Parse("#3DDC97"), Gold: Color.Parse("#F5C04E"),
        Good: Color.Parse("#34C77B"), Warn: Color.Parse("#F5A524"), Bad: Color.Parse("#F0525A"),
        Pitch: Color.Parse("#2E7D4A"), PitchLine: Color.Parse("#B9E3C6"));

    public static readonly Palette Light = new("Hell", ThemeVariant.Light,
        Bg: Color.Parse("#EEF1F6"), Surface: Color.Parse("#FFFFFF"), Surface2: Color.Parse("#E4E9F2"), Line: Color.Parse("#CCD4E2"),
        Text: Color.Parse("#141B2B"), Muted: Color.Parse("#5E6A82"), Accent: Color.Parse("#15925A"), Gold: Color.Parse("#B7801A"),
        Good: Color.Parse("#16884F"), Warn: Color.Parse("#C77A06"), Bad: Color.Parse("#C9303A"),
        Pitch: Color.Parse("#3B9A5C"), PitchLine: Color.Parse("#E8F6EC"));

    public static readonly Palette Terminal = new("Terminal", ThemeVariant.Dark,
        Bg: Color.Parse("#050805"), Surface: Color.Parse("#0B120B"), Surface2: Color.Parse("#122012"), Line: Color.Parse("#1F3A1F"),
        Text: Color.Parse("#7CFC7C"), Muted: Color.Parse("#3F9A3F"), Accent: Color.Parse("#7CFC7C"), Gold: Color.Parse("#D7FF6B"),
        Good: Color.Parse("#7CFC7C"), Warn: Color.Parse("#E3D34A"), Bad: Color.Parse("#FF5F57"),
        Pitch: Color.Parse("#0F2A12"), PitchLine: Color.Parse("#3FBF3F"));

    public static readonly IReadOnlyList<Palette> All = new[] { Classic, Light, Terminal };

    public static Palette Current { get; private set; } = Classic;

    public static Palette ByName(string name) => All.FirstOrDefault(p => p.Name == name) ?? Classic;

    public static void Apply(string name)
    {
        var p = ByName(name);
        Current = p;
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = p.Variant;
        void Set(string key, Color c) => app.Resources[key] = new SolidColorBrush(c);
        Set("BgBrush", p.Bg);
        Set("SurfaceBrush", p.Surface);
        Set("Surface2Brush", p.Surface2);
        Set("LineBrush", p.Line);
        Set("TextBrush", p.Text);
        Set("MutedBrush", p.Muted);
        Set("AccentBrush", p.Accent);
        Set("GoldBrush", p.Gold);
        Set("GoodBrush", p.Good);
        Set("WarnBrush", p.Warn);
        Set("BadBrush", p.Bad);
        Set("PitchBrush", p.Pitch);
        Set("PitchLineBrush", p.PitchLine);
        app.Resources["AccentColor"] = p.Accent;
        app.Resources["SystemAccentColor"] = p.Accent;
    }

    public IBrush Brush(Color c) => new SolidColorBrush(c);

    /// <summary>Attributwerte farbcodiert: 1–5 rot, 6–10 orange, 11–15 grün, 16–20 dunkelgrün.</summary>
    public Color AttributeColor(int v) => v switch
    {
        <= 5 => Color.Parse("#D9434B"),
        <= 10 => Color.Parse("#E58A1F"),
        <= 15 => Color.Parse("#2FA866"),
        _ => Color.Parse("#16723F"),
    };

    public Color GradeColor(double? g) => g switch
    {
        null => Muted,
        <= 2.0 => Good,
        <= 3.0 => Accent,
        <= 4.0 => Text,
        <= 4.5 => Warn,
        _ => Bad,
    };

    public Color ConditionColor(double c) => c switch { < 70 => Bad, < 88 => Warn, _ => Good };

    /// <summary>Vereinsfarben aus den Daten (ConsoleColor-Namen) als ansprechende Töne.</summary>
    public static Color ClubColor(string name) => name switch
    {
        "Black" => Color.Parse("#1B1B1F"),
        "DarkBlue" => Color.Parse("#173A8A"),
        "DarkGreen" => Color.Parse("#11703F"),
        "DarkCyan" => Color.Parse("#0E7C86"),
        "DarkRed" => Color.Parse("#8E1B2B"),
        "DarkMagenta" => Color.Parse("#6A2A8C"),
        "DarkYellow" => Color.Parse("#C08A12"),
        "Gray" => Color.Parse("#9AA3B2"),
        "DarkGray" => Color.Parse("#4B5363"),
        "Blue" => Color.Parse("#2F6FE0"),
        "Green" => Color.Parse("#27A35A"),
        "Cyan" => Color.Parse("#36B7E0"),
        "Red" => Color.Parse("#D7263D"),
        "Magenta" => Color.Parse("#D63AA8"),
        "Yellow" => Color.Parse("#F4C430"),
        "White" => Color.Parse("#F5F5F2"),
        _ => Color.Parse("#808080"),
    };

    public static Color Contrast(Color bg) =>
        0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B > 150 ? Color.Parse("#141414") : Colors.White;
}
