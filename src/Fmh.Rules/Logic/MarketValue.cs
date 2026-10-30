using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Marktwert und Gehaltsniveau aus Stärke, Alter, Potenzial, Vertragslaufzeit und Reputation.</summary>
public static class MarketValue
{
    public static long Of(Player p, World world, PositionProfiles profiles)
    {
        var today = world.Date;
        var rating = Ratings.PositionRating(p, p.MainPosition, profiles) - Shift(world, p.ClubId is { } cid ? world.Clubs[cid] : null);
        // Profimarkt: rund 9 Mio. bei Stärke 12 (Stammspieler der 1. Liga), etwa Faktor 1,85 je Stärkepunkt.
        var value = 9_000_000 * Math.Pow(1.85, rating - 12);
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

    /// <summary>
    /// Wochengehalt des Profimarkts je Stärke: 9.000 bei Stärke 9,4 (Durchschnitt der 2. Liga, rund 470.000 im Jahr),
    /// etwa Faktor 1,85 je Stärkepunkt. Untere Spielklassen zahlen anteilig (<c>wageLevel</c> der Division).
    /// </summary>
    public static double Wage(double level, World world, Club? club) => 9_000 * Math.Pow(1.85, level - Shift(world, club) - 9.4);

    /// <summary>Marktübliches Wochengehalt für einen Spieler dieser Stärke im Umfeld eines Vereins (ohne Verein: sein eigener).</summary>
    public static int FairWage(Player p, World world, PositionProfiles profiles, Club? club = null) =>
        (int)(Math.Round(Wage(Ratings.PositionRating(p, p.MainPosition, profiles), world, club ?? (p.ClubId is { } c ? world.Clubs[c] : null)) / 100) * 100);

    /// <summary>Spielklasse eines Vereins für das Marktniveau: Division der Liga bzw. der Pool.</summary>
    public static string MarketKey(World world, Club club) => world.FindLeague(club.LeagueId)?.Format.Id ?? club.LeagueId;

    /// <summary>Wie weit das Marktniveau der Spielklasse seit Spielstart gestiegen ist (0 ohne Messung, z. B. in älteren Spielständen).</summary>
    public static double Shift(World world, Club? club) =>
        club is not null && MarketKey(world, club) is var key && world.MarketLevels.TryGetValue(key, out var level)
        && world.MarketBases.TryGetValue(key, out var b) ? level - b : 0;

    /// <summary>Misst das Marktniveau aller Spielklassen (Ø Top-11 der Vereine); beim ersten Mal auch die Basis.</summary>
    public static void UpdateMarketLevel(World world, PositionProfiles profiles)
    {
        foreach (var group in world.Clubs.Values.Where(c => c.ReserveOf is null).GroupBy(c => MarketKey(world, c)))
        {
            var level = group.Average(c => world.SquadOf(c).Select(p => Ratings.PositionRating(p, p.MainPosition, profiles))
                .OrderDescending().Take(11).DefaultIfEmpty(0).Average());
            world.MarketLevels[group.Key] = Math.Round(level, 4);
            world.MarketBases.TryAdd(group.Key, world.MarketLevels[group.Key]);
        }
    }
}

/// <summary>Vorstand: Budgets zum Saisonstart (bis zum Finanzmodul in Phase 3 aus der Reputation abgeleitet).</summary>
public static class Budgets
{
    public static long StartingBalance(Club club) => MarketValue.Round(1_500_000 * Math.Pow(1.35, club.Reputation - 5));

    /// <summary>
    /// Wöchentliche Gehaltssumme: eigener Kader (Leihspieler mit dem übernommenen Anteil) plus der Restanteil für
    /// verliehene Spieler, den der abgebende Verein weiter zahlt.
    /// </summary>
    /// <param name="activeLoans">Alle derzeit verliehenen Spieler, falls schon ermittelt (spart den Lauf über alle Spieler).</param>
    public static long WageBill(Club club, World world, IEnumerable<Player>? activeLoans = null) =>
        world.SquadOf(club).Sum(p => (long)(p.Contract?.WeeklyWage ?? 0) * (p.Loan is { } l ? l.WageSharePercent : 100) / 100)
        + (activeLoans ?? world.Players.Values).Where(p => p.Loan?.ParentClubId == club.Id && p.ClubId != club.Id)
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
