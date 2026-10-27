using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Pokale“ für andere Module.</summary>
public interface ICupsService
{
    /// <summary>Legt die Pokale der Saison an und lost die erste Runde aus.</summary>
    void CreateCups();
    /// <summary>Nach einem Pokalspiel: Duell entscheiden, nächste Runde auslosen, Sieger ehren.</summary>
    void CupMatchPlayed(Cup cup, Fixture f);
    /// <summary>Tägliche Prüfung: Relegation ansetzen, Saisonende erkennen. true = alle Wettbewerbe beendet.</summary>
    bool CompetitionsDaily();
}
