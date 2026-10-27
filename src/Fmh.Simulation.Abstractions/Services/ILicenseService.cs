using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Lizenz“ für andere Module.</summary>
public interface ILicenseService
{
    /// <summary>Saisonabschluss für FFP und Lizenz festhalten (beim Saisonwechsel).</summary>
    void RecordSeasonFinance(Club club);
    void FfpAssessment();
    /// <summary>Saisonende: Plätze nach Abschlusstabelle und Pokalsieger; Vereine ohne Zulassung werden übersprungen.</summary>
    void AllocateEuropeanPlaces();
    /// <summary>Saisonstart: Startprämie der Teilnehmer.</summary>
    void EuropeSeasonStart();
}
