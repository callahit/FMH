using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Isometrische Geländekarte: Aufbau, Vorschau beim Bauen, Klicks, Drehen und Wege ziehen.</summary>
public sealed partial class StadiumPageViewModel
{
    private void BuildIsoMap()
    {
        var s = Session;
        var club = Club;
        s.EnsureGrounds(club);
        var g = club.Grounds;
        var width = s.GroundsWidth(club);
        var buildings = g.Buildings.Select(b =>
        {
            var def = Data.Building(b.Type);
            var (fw, fh) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            return new IsoBuildingVisual
            {
                Id = b.Id,
                X = b.X,
                Y = b.Y,
                Width = fw,
                Height = fh,
                Level = Math.Max(1, b.Level),
                Category = def?.Category ?? "",
                Label = T["building." + b.Type],
                Type = b.Type,
                UnderConstruction = b.UnderConstruction,
                Connected = s.IsBuildingConnected(club, b),
            };
        }).ToList();

        var state = new IsoMapState
        {
            Width = width,
            Height = g.Height,
            BaseWidth = g.Width,
            StadiumX = g.StadiumX,
            StadiumY = g.StadiumY,
            StadiumWidth = g.StadiumWidth,
            StadiumHeight = g.StadiumHeight,
            StadiumRoof = club.Stadium.RoofShare >= 0.5,
            StandHeights = club.Stadium.Stands.ToDictionary(st => st.Side.ToString(), st => Math.Clamp(st.Capacity / 5000 + 1, 1, 4)),
            StandCapacities = club.Stadium.Stands.ToDictionary(st => st.Side.ToString(), st => st.Capacity),
            RoofSides = club.Stadium.Stands.Where(st => st.Roof).Select(st => st.Side.ToString()).ToHashSet(),
            BoxSides = club.Stadium.Stands.Where(st => st.Boxes > 0).Select(st => st.Side.ToString()).ToHashSet(),
            Floodlight = club.Stadium.Floodlight,
            SeatColor = SeatColor(club),
            StadiumLabel = $"{s.StadiumDisplayName(club)} · {Fmt.Int(club.Stadium.Capacity)}",
            SelectedId = _selected?.Id,
            SelectedCell = _selected is null && _cell is { } sc ? new GridPos(sc.x, sc.y) : null,
            Paths = g.Paths.ToList(),
            Gates = g.Gates.ToList(),
            Buildings = buildings,
            ShowPaths = ShowPaths,
            Tool = ToolIndex switch { 1 => IsoMapTool.Build, 2 => IsoMapTool.Path, _ => IsoMapTool.Select },
            OnHover = OnIsoHover,
            OnClick = OnIsoClick,
            OnRightClick = OnIsoRightClick,
            OnRotate = RotatePreview,
            OnPathDrag = OnPathDrag,
            OnPathRelease = OnPathRelease,
        };
        UpdateGhost(state);
        IsoMap = state;
    }

    /// <summary>Sitzfarbe der Tribünen: Vereinsfarbe, zu dunkle oder zu helle Farben weichen der Zweitfarbe bzw. einem Rot.</summary>
    private static Color SeatColor(Club club)
    {
        static bool Usable(Color c) => c.R + c.G + c.B is > 90 and < 690;
        var primary = Palette.ClubColor(club.PrimaryColor);
        if (Usable(primary)) return primary;
        var secondary = Palette.ClubColor(club.SecondaryColor);
        return Usable(secondary) ? secondary : Color.Parse("#C8414A");
    }

    private void UpdateGhost(IsoMapState state)
    {
        if (ToolIndex == 1 && PaletteType is { } type && Data.Building(type) is { } def && _hover is { } h)
        {
            var (fw, fh) = BuildingFootprint.Size(def, PreviewRotation);
            var fits = Session.Fits(Club, def, h.x, h.y, PreviewRotation);
            var canPay = Financed || def.Cost <= Club.Budget.Balance;
            state.Ghost = new IsoGhost
            {
                X = h.x, Y = h.y, Width = fw, Height = fh,
                Valid = fits && canPay && (def.Multiple || Club.Grounds.Buildings.All(b => b.Type != def.Id)),
                Category = def.Category,
                CostLabel = M(Cost(def.Cost)),
            };
        }
        else state.Ghost = null;
    }

    private void OnIsoHover(int x, int y)
    {
        _hover = (x, y);
        if (IsoMap is { } map)
        {
            UpdateGhost(map);
            if (ToolIndex == 2) return;
            map.Ghost = map.Ghost;
            IsoMap = map;
            // Trigger redraw via new reference
            IsoMap = CloneMap(map);
        }
    }

    private static IsoMapState CloneMap(IsoMapState m) => new()
    {
        Width = m.Width, Height = m.Height, BaseWidth = m.BaseWidth,
        StadiumX = m.StadiumX, StadiumY = m.StadiumY, StadiumWidth = m.StadiumWidth, StadiumHeight = m.StadiumHeight,
        StadiumRoof = m.StadiumRoof, StandHeights = m.StandHeights, Paths = m.Paths, Gates = m.Gates, Buildings = m.Buildings,
        StandCapacities = m.StandCapacities, RoofSides = m.RoofSides, BoxSides = m.BoxSides, Floodlight = m.Floodlight,
        SeatColor = m.SeatColor, StadiumLabel = m.StadiumLabel, SelectedId = m.SelectedId, SelectedCell = m.SelectedCell,
        Ghost = m.Ghost, PathGhost = m.PathGhost, PathGhostValid = m.PathGhostValid, ShowPaths = m.ShowPaths, Tool = m.Tool,
        OnHover = m.OnHover, OnClick = m.OnClick, OnRightClick = m.OnRightClick, OnRotate = m.OnRotate,
        OnPathDrag = m.OnPathDrag, OnPathRelease = m.OnPathRelease,
    };

    private void OnIsoClick(int x, int y)
    {
        var s = Session;
        var club = Club;
        if (ToolIndex == 1 && PaletteType is { } type)
        {
            Show(s.OrderBuilding(type, x, y, Contractor, Financed, PreviewRotation), "build.ordered");
            return;
        }
        if (ToolIndex == 2) return;
        var hit = club.Grounds.Buildings.FirstOrDefault(b =>
        {
            var def = Data.Building(b.Type);
            var (fw, fh) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            return x >= b.X && x < b.X + fw && y >= b.Y && y < b.Y + fh;
        });
        if (hit is not null) SelectCell(hit, null);
        else if (x >= 0 && y >= 0 && x < s.GroundsWidth(club) && y < club.Grounds.Height)
            SelectCell(null, (x, y));
    }

    private void OnIsoRightClick(int x, int y)
    {
        var club = Club;
        var hit = club.Grounds.Buildings.FirstOrDefault(b =>
        {
            var def = Data.Building(b.Type);
            var (fw, fh) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            return x >= b.X && x < b.X + fw && y >= b.Y && y < b.Y + fh;
        });
        if (hit is not null)
        {
            SelectCell(hit, null);
            Demolish();
            return;
        }
        if (x >= club.Grounds.Width)
            Show(Session.BuyLand(), "build.landBought");
    }

    private void RotatePreview()
    {
        PreviewRotation = BuildingFootprint.NormalizeRotation(PreviewRotation + 90);
        if (IsoMap is { } map)
        {
            UpdateGhost(map);
            IsoMap = CloneMap(map);
        }
    }

    private void OnPathDrag(int x0, int y0, int x1, int y1)
    {
        if (IsoMap is not { } map) return;
        var width = Session.GroundsWidth(Club);
        var grid = Session.Occupancy(Club);
        bool Can(int x, int y) => x >= 0 && y >= 0 && x < width && y < Club.Grounds.Height && grid[x, y] is 0 or -2;
        var cells = PathGraph.DrawPathL(x0, y0, x1, y1, Can);
        map.PathGhost = cells;
        map.PathGhostValid = cells.Count > 0 && cells.Count * PathGraph.PathTileCost <= Club.Budget.Balance;
        IsoMap = CloneMap(map);
    }

    private void OnPathRelease(int x0, int y0, int x1, int y1)
    {
        Show(Session.DrawPath(x0, y0, x1, y1), "build.ordered");
        if (IsoMap is { } map)
        {
            map.PathGhost = null;
            IsoMap = CloneMap(map);
        }
    }
}
