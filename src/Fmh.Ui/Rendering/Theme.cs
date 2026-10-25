namespace Fmh.Ui.Rendering;

/// <summary>Farbthemen (Kapitel 11): Klassik wie Anstoß, Hell und Terminal.</summary>
public sealed record Theme(
    string Name,
    ConsoleColor Bg,
    ConsoleColor Fg,
    ConsoleColor Dim,
    ConsoleColor Accent,
    ConsoleColor Border,
    ConsoleColor MenuBg,
    ConsoleColor MenuFg,
    ConsoleColor SelBg,
    ConsoleColor SelFg,
    ConsoleColor PanelBg,
    ConsoleColor Good,
    ConsoleColor Bad,
    ConsoleColor Warn)
{
    public static readonly Theme Classic = new("Klassik",
        Bg: ConsoleColor.DarkBlue, Fg: ConsoleColor.Gray, Dim: ConsoleColor.DarkGray, Accent: ConsoleColor.Yellow,
        Border: ConsoleColor.Cyan, MenuBg: ConsoleColor.DarkCyan, MenuFg: ConsoleColor.White,
        SelBg: ConsoleColor.Gray, SelFg: ConsoleColor.DarkBlue, PanelBg: ConsoleColor.DarkBlue,
        Good: ConsoleColor.Green, Bad: ConsoleColor.Red, Warn: ConsoleColor.Yellow);

    public static readonly Theme Light = new("Hell",
        Bg: ConsoleColor.White, Fg: ConsoleColor.Black, Dim: ConsoleColor.DarkGray, Accent: ConsoleColor.DarkBlue,
        Border: ConsoleColor.DarkGray, MenuBg: ConsoleColor.Gray, MenuFg: ConsoleColor.Black,
        SelBg: ConsoleColor.DarkBlue, SelFg: ConsoleColor.White, PanelBg: ConsoleColor.White,
        Good: ConsoleColor.DarkGreen, Bad: ConsoleColor.DarkRed, Warn: ConsoleColor.DarkYellow);

    public static readonly Theme Terminal = new("Terminal",
        Bg: ConsoleColor.Black, Fg: ConsoleColor.Green, Dim: ConsoleColor.DarkGreen, Accent: ConsoleColor.Green,
        Border: ConsoleColor.DarkGreen, MenuBg: ConsoleColor.Black, MenuFg: ConsoleColor.Green,
        SelBg: ConsoleColor.Green, SelFg: ConsoleColor.Black, PanelBg: ConsoleColor.Black,
        Good: ConsoleColor.Green, Bad: ConsoleColor.Red, Warn: ConsoleColor.Yellow);

    public static readonly IReadOnlyList<Theme> All = new[] { Classic, Light, Terminal };

    public static Theme ByName(string name) => All.FirstOrDefault(t => t.Name == name) ?? Classic;

    /// <summary>Attributwerte farbcodiert: 1–5 rot, 6–10 orange, 11–15 grün, 16–20 dunkelgrün.</summary>
    public ConsoleColor AttributeColor(int value)
    {
        if (this == Terminal) return value >= 11 ? ConsoleColor.Green : value >= 6 ? ConsoleColor.DarkGreen : ConsoleColor.Red;
        return value switch
        {
            <= 5 => this == Light ? ConsoleColor.DarkRed : ConsoleColor.Red,
            <= 10 => ConsoleColor.DarkYellow,
            <= 15 => this == Light ? ConsoleColor.DarkGreen : ConsoleColor.Green,
            _ => this == Light ? ConsoleColor.DarkGreen : ConsoleColor.DarkGreen,
        };
    }

    /// <summary>Farbcodierung zusätzlich als Symbol (Barrierefreiheit, Kapitel 12).</summary>
    public static char AttributeSymbol(int value) => value switch
    {
        <= 5 => '▼',
        <= 10 => '·',
        <= 15 => '▲',
        _ => '★',
    };
}
