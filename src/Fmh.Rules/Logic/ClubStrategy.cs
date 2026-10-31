using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Ordnet KI-Vereine einer Transferstrategie zu (Spielklasse, Ansehen in der Liga, Transferbudget).</summary>
public static class ClubStrategy
{
    public static TransferStrategy Of(World world, GameData data, Club club)
    {
        var league = world.FindLeague(club.LeagueId);
        var professional = data.TransferRules.ProfessionalLevels;
        if (league is null || league.Level > professional) return TransferStrategy.Bargain;
        var rivals = league.ClubIds.Select(id => world.Clubs[id]).ToList();
        var stronger = rivals.Count(c => c.Reputation > club.Reputation);
        // Das obere Viertel der höchsten Spielklasse.
        if (league.Level == 1 && stronger < rivals.Count / 4) return TransferStrategy.Elite;
        var budgets = rivals.Select(c => c.Budget.TransferBudget).OrderBy(b => b).ToList();
        if (league.Level == professional || club.Budget.TransferBudget < budgets[budgets.Count / 2] / 2) return TransferStrategy.Borrower;
        return TransferStrategy.Developer;
    }

    /// <summary>Marktverhalten im Land des Vereins (Leihneigung, Kaufpflicht).</summary>
    public static MarketTendency Tendency(World world, Club club) => world.Country(club.CountryCode).Market;
}
