using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Transfers“ für andere Module.</summary>
public interface ITransfersService
{
    /// <summary>Vertrag aus einem Angebot; fehlende Auf-/Abstiegsklauseln nach der Spielklasse des Vereins.</summary>
    Contract MakeContract(ContractOffer o, DateOnly start, Club club);
    /// <summary>Vertrag setzen und die bisherige Fassung in der Historie behalten.</summary>
    void SetContract(Player p, Contract? next, int? previousClubId = null, int dealId = 0);
    /// <summary>Zum Saisonwechsel: Verträge enden, Vorverträge greifen, Leihen laufen aus.</summary>
    void ExpireContractsAndLoans();
    void EnforceSquadLimits();
    /// <summary>Vereinslose ohne Perspektive beenden ihre Laufbahn.</summary>
    void RetireFreeAgents();
    double Rating(Player p);
    /// <summary>Bester passender Vereinsloser; mit <paramref name="withinBudget"/> nur, wenn das Gehaltsbudget reicht.</summary>
    bool SignBestFreeAgent(Club club, Position pos, bool withinBudget = false);
}
