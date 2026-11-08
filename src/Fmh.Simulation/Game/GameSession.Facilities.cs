using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Vereinsgelände“.</summary>
public sealed partial class GameSession
{
    public void InvalidateEffects(Club club) => _facilities.InvalidateEffects(club);
    public double Effect(Club club, string key) => _facilities.Effect(club, key);
    public double LocationFactor(Club club, Building b, BuildingDef def) => _facilities.LocationFactor(club, b, def);
    public bool IsBuildingConnected(Club club, Building b) => _facilities.IsConnected(club, b);
    public PathGraph PathGraphOf(Club club) => _facilities.PathGraphOf(club);
    public void EnsureGrounds(Club club) => _facilities.EnsureGrounds(club);
    public int BuildingLevel(Club club, string type) => _facilities.BuildingLevel(club, type);
    public int GroundsWidth(Club club) => _facilities.GroundsWidth(club);
    public int[,] Occupancy(Club club) => _facilities.Occupancy(club);
    public bool Fits(Club club, BuildingDef def, int x, int y, int rotation = 0) => _facilities.Fits(club, def, x, y, rotation);
    public (int x, int y)? FreeSpot(Club club, BuildingDef def) => _facilities.FreeSpot(club, def);
    public long MonthlyUpkeep(Club club, Building b) => _facilities.MonthlyUpkeep(club, b);
    public (long cost, int days)? StadiumProjectQuote(Club club, ProjectKind kind, StandSide? side) => _facilities.StadiumProjectQuote(club, kind, side);
    public string? OrderStadiumProject(ProjectKind kind, StandSide? side, Contractor contractor, bool financed = false) => _facilities.OrderStadiumProject(kind, side, contractor, financed);
    public string? OrderBuilding(string type, int x, int y, Contractor contractor, bool financed = false, int rotation = 0) =>
        _facilities.OrderBuilding(type, x, y, contractor, financed, rotation);
    public string? UpgradeBuilding(Building b, Contractor contractor, bool financed = false) => _facilities.UpgradeBuilding(b, contractor, financed);
    public string? MissingRequirement(Club club, BuildingDef def) => _facilities.MissingRequirement(club, def);
    public string? Demolish(Building b) => _facilities.Demolish(b);
    public void ToggleLease(Building b) => _facilities.ToggleLease(b);
    public string? BuyLand() => _facilities.BuyLand();
    public string? DrawPath(int x0, int y0, int x1, int y1, bool free = false) => _facilities.DrawPath(HumanClub, x0, y0, x1, y1, free);
    public TextArg ProjectName(ConstructionProject p) => _facilities.ProjectName(p);
    public void CreateGrounds(Club club) => _facilities.CreateGrounds(club);
}
