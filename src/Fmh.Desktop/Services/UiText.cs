using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Desktop.Services;

/// <summary>Anzeigeformate, die von den Einstellungen des Managerprofils abhängen.</summary>
public static class UiText
{
    private static Texts T => AppState.Current.T;

    public static string Wage(int weekly, ManagerProfile profile, string currency) => profile.WageDisplay switch
    {
        WageDisplay.Week => $"{Fmt.Money(weekly, currency)}{T["unit.perWeek"]}",
        WageDisplay.Year => $"{Fmt.Money(weekly * 52L, currency)}{T["unit.perYear"]}",
        _ => $"{Fmt.Money((long)Math.Round(weekly * 52 / 12.0), currency)}{T["unit.perMonth"]}",
    };

    public static string Position(Position p) => T[PositionInfo.TextKey(p)];

    public static string Morale(Morale m) => T[Ratings.MoraleKey(m)];

    public static string Strength(double s) => Fmt.Num(s, "0.0");

    public static string Grade(double? g) => g is { } v ? Fmt.Num(v, "0.0") : "–";

    public static string Status(Player p, Club club)
    {
        if (p.State.Injury is { } inj) return T.Format("status.injured", inj.DaysRemaining);
        if (p.State.SuspendedMatches > 0) return T.Format("status.suspended", p.State.SuspendedMatches);
        if (club.Tactic.Lineup.Contains(p.Id)) return T["status.lineup"];
        if (club.Tactic.Bench.Contains(p.Id)) return T["status.bench"];
        return "";
    }

    public static string Stars(double stars)
    {
        stars = Math.Clamp(Math.Round(stars * 2) / 2, 0, 5);
        var full = (int)stars;
        var half = stars - full >= 0.5;
        return new string('★', full) + (half ? "½" : "") + new string('☆', 5 - full - (half ? 1 : 0));
    }

    /// <summary>Farbcodierung zusätzlich als Symbol (Barrierefreiheit).</summary>
    public static string AttributeSymbol(int v) => v switch { <= 5 => "▼", <= 10 => "·", <= 15 => "▲", _ => "★" };

    public static string Currency(Club club) => AppState.Current.Session!.World.Country(club.CountryCode).Currency;
}
