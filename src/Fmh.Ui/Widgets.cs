using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;

namespace Fmh.Ui;

/// <summary>Auswahl und Scrollposition einer Liste.</summary>
public sealed class ListState
{
    public int Index { get; set; }
    public int Offset { get; private set; }

    public void Clamp(int count)
    {
        Index = count == 0 ? 0 : Math.Clamp(Index, 0, count - 1);
    }

    /// <summary>Behandelt Pfeiltasten, Bild auf/ab, Pos1/Ende. Gibt true zurück, wenn die Taste verbraucht wurde.</summary>
    public bool HandleKey(ConsoleKeyInfo key, int count, int page = 10)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Index--; break;
            case ConsoleKey.DownArrow: Index++; break;
            case ConsoleKey.PageUp: Index -= page; break;
            case ConsoleKey.PageDown: Index += page; break;
            case ConsoleKey.Home: Index = 0; break;
            case ConsoleKey.End: Index = count - 1; break;
            default: return false;
        }
        Clamp(count);
        return true;
    }

    /// <summary>Verschiebt den sichtbaren Ausschnitt so, dass die Auswahl sichtbar ist.</summary>
    public int VisibleStart(int visibleRows, int count)
    {
        Clamp(count);
        if (Index < Offset) Offset = Index;
        if (Index >= Offset + visibleRows) Offset = Index - visibleRows + 1;
        Offset = Math.Clamp(Offset, 0, Math.Max(0, count - visibleRows));
        return Offset;
    }
}

/// <summary>Anzeigeformate, die von den Einstellungen des Managerprofils abhängen.</summary>
public static class Display
{
    public static string Wage(int weekly, ManagerProfile profile, string currency, Texts t) => profile.WageDisplay switch
    {
        WageDisplay.Week => $"{Fmt.Money(weekly, currency)}{t["unit.perWeek"]}",
        WageDisplay.Year => $"{Fmt.Money(weekly * 52L, currency)}{t["unit.perYear"]}",
        _ => $"{Fmt.Money((long)Math.Round(weekly * 52 / 12.0), currency)}{t["unit.perMonth"]}",
    };

    public static string Strength(double s) => Fmt.Num(s, "0.0");

    public static string Grade(double? g) => g is { } v ? Fmt.Num(v, "0.0") : "–";

    public static string Bar(double value, double max, int width)
    {
        var filled = (int)Math.Round(Math.Clamp(value / max, 0, 1) * width);
        return new string('█', filled) + new string('░', width - filled);
    }

    /// <summary>Attributwert gemäß Profil: Zahl, Balken oder Sterne (jeweils mit Symbol für Farbunabhängigkeit).</summary>
    public static string Attribute(int value, AttributeDisplay mode) => mode switch
    {
        AttributeDisplay.Bars => Bar(value, 20, 10),
        AttributeDisplay.Stars => Stars(value / 4.0),
        _ => $"{value,2}{Theme.AttributeSymbol(value)}",
    };

    public static string Stars(double stars)
    {
        stars = Math.Clamp(Math.Round(stars * 2) / 2, 0, 5);
        var full = (int)stars;
        var half = stars - full >= 0.5;
        return new string('★', full) + (half ? "½" : "") + new string('☆', 5 - full - (half ? 1 : 0));
    }

    public static string Position(Position p, Texts t) => t[PositionInfo.TextKey(p)];

    public static string Morale(Morale m, Texts t) => t[Ratings.MoraleKey(m)];

    public static ConsoleColor MoraleColor(Morale m, Theme th) => m switch
    {
        <= Core.Model.Morale.Bad => th.Bad,
        <= Core.Model.Morale.Okay => th.Warn,
        _ => th.Good,
    };

    public static ConsoleColor ConditionColor(double c, Theme th) => c switch
    {
        < 70 => th.Bad,
        < 88 => th.Warn,
        _ => th.Good,
    };

    public static ConsoleColor GradeColor(double? g, Theme th) => g switch
    {
        null => th.Dim,
        <= 2.5 => th.Good,
        <= 4.0 => th.Fg,
        _ => th.Bad,
    };

    /// <summary>Kurzstatus eines Spielers für Kaderlisten.</summary>
    public static string Status(Player p, Club club, Texts t)
    {
        if (p.State.Injury is { } inj) return t.Format("status.injured", inj.DaysRemaining);
        if (p.State.SuspendedMatches > 0) return t.Format("status.suspended", p.State.SuspendedMatches);
        if (club.Tactic.Lineup.Contains(p.Id)) return t["status.lineup"];
        if (club.Tactic.Bench.Contains(p.Id)) return t["status.bench"];
        return "";
    }

    public static ConsoleColor ClubColor(string name, ConsoleColor fallback) =>
        Enum.TryParse<ConsoleColor>(name, true, out var c) ? c : fallback;

    /// <summary>Lesbare Schriftfarbe auf einer Hintergrundfarbe.</summary>
    public static ConsoleColor ContrastOn(ConsoleColor bg) => bg switch
    {
        ConsoleColor.White or ConsoleColor.Yellow or ConsoleColor.Gray or ConsoleColor.Cyan or ConsoleColor.Green
            or ConsoleColor.DarkYellow or ConsoleColor.Magenta => ConsoleColor.Black,
        _ => ConsoleColor.White,
    };

    public static string Truncate(string s, int width) => s.Length <= width ? s : width <= 1 ? s[..width] : s[..(width - 1)] + "…";
}

/// <summary>Kleine Zeichenhilfen für wiederkehrende Elemente.</summary>
public static class Draw
{
    /// <summary>Kopfzeile einer Tabelle mit Spalten (Titel, Breite, Ausrichtung).</summary>
    public static void TableHeader(Canvas c, int x, int y, Theme th, params (string title, int width, Align align)[] cols)
    {
        var cx = x;
        foreach (var (title, width, align) in cols)
        {
            c.WriteField(cx, y, width, title, th.Accent, th.Bg, align);
            cx += width + 1;
        }
    }

    public static void KeyHelp(Canvas c, int y, Theme th, string text)
    {
        c.Fill(0, y, c.Width, 1, ' ', th.MenuFg, th.MenuBg);
        c.Write(1, y, text, th.MenuFg, th.MenuBg, c.Width - 2);
    }

    public static void Selection(Canvas c, int x, int y, int w, Theme th)
    {
        for (var i = 0; i < w && x + i < c.Width; i++)
            c.Set(x + i, y, c.CharAt(x + i, y), th.SelFg, th.SelBg);
    }
}
