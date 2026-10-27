using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Training“ für andere Module.</summary>
public interface ITrainingService
{
    /// <summary>Vertrautheit eines Vereins mit einer Formation (0–100). Unbekannte Formationen starten bei 30.</summary>
    double Familiarity(Club club, string formationId);
    void AddFamiliarity(Club club, string formationId, double amount);
    /// <summary>Fortschritt pro Einheit: Trainer, Intensität, Alter, Luft nach oben und Professionalität.</summary>
    double SessionGain(Club club, Player p, TrainingIntensity intensity);
    /// <summary>Angesparter Fortschritt ab 1,0 hebt ein Attribut der Gruppe; Positionsgewichtung entscheidet welches.</summary>
    void ApplyProgress(Player p);
}
