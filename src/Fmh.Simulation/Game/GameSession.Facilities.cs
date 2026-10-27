using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Vereinsgelände“.</summary>
public sealed partial class GameSession
{
    public static double Scale(Club club) => FacilitiesModule.Scale(club);
    public void InvalidateEffects(Club club) => _facilities.InvalidateEffects(club);
    public double Effect(Club club, string key) => _facilities.Effect(club, key);
    public double LocationFactor(Club club, Building b, BuildingDef def) => _facilities.LocationFactor(club, b, def);
    public int BuildingLevel(Club club, string type) => _facilities.BuildingLevel(club, type);
    public int GroundsWidth(Club club) => _facilities.GroundsWidth(club);
    public int[,] Occupancy(Club club) => _facilities.Occupancy(club);
    public bool Fits(Club club, BuildingDef def, int x, int y) => _facilities.Fits(club, def, x, y);
    public (int x, int y)? FreeSpot(Club club, BuildingDef def) => _facilities.FreeSpot(club, def);
    public static long LevelCost(BuildingDef def, int level) => FacilitiesModule.LevelCost(def, level);
    public long MonthlyUpkeep(Club club, Building b) => _facilities.MonthlyUpkeep(club, b);
    public static long LandPrice(Club club) => FacilitiesModule.LandPrice(club);
    public static (double cost, double days) ContractorFactors(Contractor c) => FacilitiesModule.ContractorFactors(c);
    public (long cost, int days)? StadiumProjectQuote(Club club, ProjectKind kind, StandSide? side) => _facilities.StadiumProjectQuote(club, kind, side);
    public string? OrderStadiumProject(ProjectKind kind, StandSide? side, Contractor contractor, bool financed = false) => _facilities.OrderStadiumProject(kind, side, contractor, financed);
    public string? OrderBuilding(string type, int x, int y, Contractor contractor, bool financed = false) => _facilities.OrderBuilding(type, x, y, contractor, financed);
    public string? UpgradeBuilding(Building b, Contractor contractor, bool financed = false) => _facilities.UpgradeBuilding(b, contractor, financed);
    public string? MissingRequirement(Club club, BuildingDef def) => _facilities.MissingRequirement(club, def);
    public string? Demolish(Building b) => _facilities.Demolish(b);
    public void ToggleLease(Building b) => _facilities.ToggleLease(b);
    public string? BuyLand() => _facilities.BuyLand();
    public TextArg ProjectName(ConstructionProject p) => _facilities.ProjectName(p);
    public static Stadium CreateStadium(Club club, Rng rng) => FacilitiesModule.CreateStadium(club, rng);
    public void CreateGrounds(Club club) => _facilities.CreateGrounds(club);
}
