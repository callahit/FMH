using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Stadionpläne“ für andere Module.</summary>
public interface IStadiumPlansService
{
    /// <summary>Grundstücksangebot der Stadt (aus dem Ereignis "citySite").</summary>
    void OfferStadiumSite(Club club);
    /// <summary>Gültiges Grundstück für den Neubau (angeboten, nicht abgelaufen, nicht bebaut).</summary>
    StadiumSite? AvailableSite(Club club);
    /// <summary>Stadionkredite: eigener Rahmen bis zum Dreifachen der Jahreseinnahmen.</summary>
    long StadiumLoanCapacity(Club club);
    /// <summary>Abgelaufene Grundstücksangebote verfallen (monatlich geprüft).</summary>
    void SiteMonthly(Club club);
}
