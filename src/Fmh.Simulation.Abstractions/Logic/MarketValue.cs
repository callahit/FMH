using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Marktwert und Gehaltsniveau aus Stärke, Alter, Potenzial, Vertragslaufzeit und Reputation.</summary>
public static class MarketValue
{
    public static long Of(Player p, DateOnly today, PositionProfiles profiles)
    {
        var rating = Ratings.PositionRating(p, p.MainPosition, profiles);
        var value = 60_000 * Math.Pow(1.55, rating - 4);
        var age = p.Age(today);
        var headroom = Math.Max(0, p.Hidden.PotentialAbility - p.Hidden.CurrentAbility);
        value *= age switch
        {
            <= 21 => 1.0 + headroom / 100.0 * 1.5,
            <= 27 => 1.0 + headroom / 200.0,
            <= 30 => 0.8,
            <= 32 => 0.55,
            _ => 0.3,
        };
        if (p.Contract is { } c)
        {
            var years = (c.Until.DayNumber - today.DayNumber) / 365.0;
            value *= years switch { < 0.5 => 0.45, < 1 => 0.7, < 2 => 0.9, _ => 1.0 };
        }
        else value *= 0.3;
        value *= 0.8 + p.Reputation / 20.0 * 0.4;
        return Round(value);
    }

    public static long Round(double v) =>
        v >= 1_000_000 ? (long)(Math.Round(v / 50_000) * 50_000) : v >= 100_000 ? (long)(Math.Round(v / 10_000) * 10_000) : (long)(Math.Round(v / 1_000) * 1_000);

    /// <summary>Marktübliches Wochengehalt für einen Spieler dieser Stärke.</summary>
    public static int FairWage(Player p, PositionProfiles profiles)
    {
        var rating = Ratings.PositionRating(p, p.MainPosition, profiles);
        return (int)(Math.Round(60 * Math.Pow(1.55, rating) / 100) * 100);
    }
}

/// <summary>Vorstand: Budgets zum Saisonstart (bis zum Finanzmodul in Phase 3 aus der Reputation abgeleitet).</summary>
public static class Budgets
{
    public static long StartingBalance(Club club) => MarketValue.Round(2_000_000 * Math.Pow(1.25, club.Reputation - 5));

    /// <summary>
    /// Wöchentliche Gehaltssumme: eigener Kader (Leihspieler mit dem übernommenen Anteil) plus der Restanteil für
    /// verliehene Spieler, den der abgebende Verein weiter zahlt.
    /// </summary>
    public static long WageBill(Club club, World world) =>
        world.SquadOf(club).Sum(p => (long)(p.Contract?.WeeklyWage ?? 0) * (p.Loan is { } l ? l.WageSharePercent : 100) / 100)
        + world.Players.Values.Where(p => p.Loan?.ParentClubId == club.Id && p.ClubId != club.Id)
            .Sum(p => (long)(p.Contract?.WeeklyWage ?? 0) * (100 - p.Loan!.WageSharePercent) / 100);

    /// <summary>Budget für die neue Saison: Transferbudget aus dem Kontostand, Gehaltsbudget mit Spielraum.</summary>
    public static void SetSeasonBudget(Club club, World world)
    {
        var b = club.Budget;
        if (b.Balance <= 0) b.Balance = StartingBalance(club) / 2;
        b.TransferBudget = MarketValue.Round(Math.Max(0, b.Balance) * 0.45);
        b.WageBudget = (long)(Math.Max(WageBill(club, world), 5_000) * 1.08);
    }
}
