using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Saison“ für andere Module.</summary>
public interface ISeasonService
{
    /// <summary>Verein verlassen: Kader, Jugend, Aufstellung, Bank und Beobachtungslisten.</summary>
    void RemoveFromClub(Player p);
    /// <summary>Kader auffüllen: bestes Talent ab 17 aus der eigenen Jugend, sonst ein Spieler aus dem Amateurbereich.</summary>
    void AddEmergencyPlayer(Club club);
    /// <summary>Position mit dem größten Bedarf nach der Kaderschablone.</summary>
    Position NeededPosition(Club club);
    bool IsTransferWindowOpen(Club? club = null);
}
