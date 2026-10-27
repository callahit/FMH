using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Stadionpläne“.</summary>
public sealed partial class GameSession
{
    public StadiumImpact ForecastStadium(Club club, Stadium planned, long cost) => _stadiumPlans.ForecastStadium(club, planned, cost);
    public StadiumImpact? ForecastProject(Club club, ProjectKind kind, StandSide? side, Contractor contractor = Contractor.Standard) => _stadiumPlans.ForecastProject(club, kind, side, contractor);
    public StadiumSite? AvailableSite(Club club) => _stadiumPlans.AvailableSite(club);
    public long NewStadiumCost(Club club, StadiumDesign d) => _stadiumPlans.NewStadiumCost(club, d);
    public long StadiumLoanCapacity(Club club) => _stadiumPlans.StadiumLoanCapacity(club);
    public static Stadium BuildNewStadium(Club club, StadiumDesign d) => StadiumPlansModule.BuildNewStadium(club, d);
    public StadiumImpact ForecastNewStadium(Club club, StadiumDesign d, Contractor contractor = Contractor.Standard) => _stadiumPlans.ForecastNewStadium(club, d, contractor);
    public string? OrderNewStadium(StadiumDesign d, Contractor contractor, bool financed) => _stadiumPlans.OrderNewStadium(d, contractor, financed);
}
