using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Vereinsgelände“ für andere Module.</summary>
public interface IFacilitiesService
{
    void InvalidateEffects(Club club);
    /// <summary>Summe einer Gebäudewirkung (Stufe × Wert, Zustand, Lagebonus, Synergien). Verpachtete Gebäude wirken nicht.</summary>
    double Effect(Club club, string key);
    int BuildingLevel(Club club, string type);
    long MonthlyUpkeep(Club club, Building b);
    /// <summary>Was ein Stadionausbau kostet und wie lange er dauert (Standardunternehmen).</summary>
    (long cost, int days)? StadiumProjectQuote(Club club, ProjectKind kind, StandSide? side);
    string? Order(Club club, ConstructionProject p, long baseCost, int baseDays, Contractor contractor, bool financed);
    TextArg ProjectName(ConstructionProject p);
    /// <summary>Monatlich: Zustand der Gebäude, Rasenpflege.</summary>
    void FacilitiesMonthly(Club club);
    /// <summary>Startgebäude nach Vereinsgröße.</summary>
    void CreateGrounds(Club club);
}
