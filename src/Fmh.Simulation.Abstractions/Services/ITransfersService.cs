using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Transfers“ für andere Module.</summary>
public interface ITransfersService
{
    Contract MakeContract(ContractOffer o, DateOnly start);
    /// <summary>Zum Saisonwechsel: Verträge enden, Vorverträge greifen, Leihen laufen aus.</summary>
    void ExpireContractsAndLoans();
    /// <summary>Vereinslose ohne Perspektive beenden ihre Laufbahn (auch freigegebene Jugendspieler unterhalb des Amateurniveaus).</summary>
    void RetireFreeAgents();
    double Rating(Player p);
    bool SignBestFreeAgent(Club club, Position pos);
}
