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

/// <summary>Geländeraster: Kacheln, Auswahl eines Feldes oder Gebäudes und Bauauswahl.</summary>
public sealed partial class StadiumPageViewModel
{
    private void BuildTiles()
    {
        var s = Session;
        var club = Club;
        var g = club.Grounds;
        var width = s.GroundsWidth(club);
        var tiles = new List<GroundTile>();
        var occupied = new bool[width, g.Height];
        for (var x = g.StadiumX; x < g.StadiumX + g.StadiumWidth; x++)
            for (var y = g.StadiumY; y < g.StadiumY + g.StadiumHeight; y++) occupied[x, y] = true;
        foreach (var p in g.Paths)
            if ((uint)p.X < (uint)width && (uint)p.Y < (uint)g.Height) occupied[p.X, p.Y] = true;
        tiles.Add(new GroundTile(g.StadiumX * Cell, g.StadiumY * Cell, g.StadiumWidth * Cell - 4, g.StadiumHeight * Cell - 4,
            s.StadiumDisplayName(club), Fmt.Int(club.Stadium.Capacity), new SolidColorBrush(Color.Parse("#3E8E5A")), new SolidColorBrush(P.Gold),
            g.StadiumX, g.StadiumY, null, true, new RelayCommand(() => SelectCell(null, null)), StadiumIcon));
        foreach (var p in g.Paths)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= width || p.Y >= g.Height) continue;
            // Wege als ruhige Pflasterfläche, Beschriftung nur im Tooltip
            tiles.Add(new GroundTile(p.X * Cell, p.Y * Cell, Cell - 4, Cell - 4, T["build.tool.path"], "",
                new SolidColorBrush(Color.FromArgb(150, 217, 207, 184)), new SolidColorBrush(Colors.Transparent),
                p.X, p.Y, null, false, new RelayCommand(() => { }), IsPath: true));
        }
        foreach (var b in g.Buildings)
        {
            var def = Data.Building(b.Type);
            var (w, h) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            for (var x = b.X; x < Math.Min(width, b.X + w); x++)
                for (var y = b.Y; y < Math.Min(g.Height, b.Y + h); y++) occupied[x, y] = true;
            var building = b;
            var (color, _, icon) = IsoArt.Category(def?.Category ?? "");
            tiles.Add(new GroundTile(b.X * Cell, b.Y * Cell, w * Cell - 4, h * Cell - 4, T["building." + b.Type],
                b.UnderConstruction ? T["build.underConstruction"] : def?.Land == true ? "" : T.Format("build.level", b.Level),
                new SolidColorBrush(b.UnderConstruction ? Color.FromArgb(120, color.R, color.G, color.B) : color),
                new SolidColorBrush(_selected == b ? P.Gold : Colors.Transparent), b.X, b.Y, b, false,
                new RelayCommand(() => SelectCell(building, null)), icon));
        }
        for (var x = 0; x < width; x++)
            for (var y = 0; y < g.Height; y++)
            {
                if (occupied[x, y]) continue;
                var cell = (x, y);
                var chosen = _selected is null && _cell == cell;
                tiles.Add(new GroundTile(x * Cell, y * Cell, Cell - 4, Cell - 4, "", x >= g.Width ? T["build.boughtLand"] : "",
                    new SolidColorBrush(Color.FromArgb(40, 120, 160, 120)), new SolidColorBrush(chosen ? P.Gold : Color.FromArgb(60, 255, 255, 255)),
                    x, y, null, false, new RelayCommand(() => SelectCell(null, cell))));
            }
        Tiles = tiles;
        GridWidth = width * Cell;
        GridHeight = g.Height * Cell;
    }

    private static readonly Geometry StadiumIcon = StreamGeometry.Parse("M3,9 C3,5 21,5 21,9 V16 C21,20.5 3,20.5 3,16 Z M3,9 C3,13 21,13 21,9 M8,12.3 V19.4 M16,12.3 V19.4 M12,12.8 V19.8");

    private void SelectCell(Building? building, (int x, int y)? cell)
    {
        _selected = building;
        _cell = cell;
        BuildTiles();
        BuildIsoMap();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var s = Session;
        var club = Club;
        HasBuilding = _selected is not null;
        IsFreeCell = _selected is null && _cell is not null;
        if (_selected is { } b && Data.Building(b.Type) is { } def)
        {
            SelectionTitle = T["building." + b.Type];
            var lines = new List<string>
            {
                T.Format("build.levelOf", b.Level, def.MaxLevel) + " · " + T.Format("build.condition", (int)Math.Round(b.Condition)),
                T.Format("build.upkeep", M(s.MonthlyUpkeep(club, b))),
                EffectText(def, b.Level),
                s.IsBuildingConnected(club, b) ? T["build.connected"] : T["build.disconnected"],
            };
            if (b.Leased) lines.Add(T["build.leased"]);
            SelectionText = string.Join("\n", lines.Where(l => l.Length > 0));
            UpgradeText = b.Level < def.MaxLevel && !def.Land
                ? T.Format("build.upgrade", b.Level + 1, M(Cost(GameSession.LevelCost(def, b.Level + 1))))
                : T["build.maxLevel"];
            LeaseText = b.Leased ? T["build.endLease"] : T["build.lease"];
            RefreshPalette();
            return;
        }
        if (_cell is { } c)
        {
            SelectionTitle = T.Format("build.freeCell", c.x + 1, c.y + 1);
            SelectionText = T["build.chooseBuilding"];
            RefreshPalette(c);
            return;
        }
        SelectionTitle = T["build.selectTitle"];
        SelectionText = T["build.selectHelp"];
        RefreshPalette();
    }

    private void RefreshPalette((int x, int y)? cell = null)
    {
        var s = Session;
        var club = Club;
        var category = CategoryIndex <= 0 ? null : CategoryIds[CategoryIndex - 1];
        BuildOptions = Data.Buildings
            .Where(d => category is null || d.Category == category)
            .Where(d => d.Multiple || club.Grounds.Buildings.All(x => x.Type != d.Id))
            .Select(d =>
            {
                var (fw, fh) = BuildingFootprint.Size(d, PreviewRotation);
                var fits = cell is { } c && s.Fits(club, d, c.x, c.y, PreviewRotation);
                var missing = s.MissingRequirement(club, d) is null ? null
                    : string.Join(", ", d.Requires.Where(r => s.BuildingLevel(club, r.Type) < r.Level).Select(r => T.Format("build.requiresLevel", T["building." + r.Type], r.Level)));
                var hint = cell is null ? T.Format("build.size", fw, fh)
                    : !fits ? T.Format("build.size", fw, fh)
                    : missing is not null ? T.Format("build.needs", missing)
                    : T.Format("build.size", fw, fh);
                var type = d.Id;
                var possible = missing is null && (cell is null || fits);
                return new BuildOption(type, T["building." + d.Id], T["buildingCat." + d.Category], M(Cost(d.Cost)), EffectText(d, 1),
                    T.Format("build.upkeep", M(d.Upkeep)), possible, hint,
                    new RelayCommand(() =>
                    {
                        if (cell is not { } at) return;
                        Show(s.OrderBuilding(type, at.x, at.y, Contractor, Financed, PreviewRotation), "build.ordered");
                        _cell = null;
                        Refresh();
                    }, () => cell is not null && possible),
                    new RelayCommand(() => PickBuilding(type)), IsoArt.Category(d.Category).icon, new SolidColorBrush(IsoArt.Category(d.Category).dark));
            })
            .OrderByDescending(o => o.Possible).ThenBy(o => o.Name).ToList();
    }

    private string EffectText(BuildingDef def, int level) => def.Effects.Count == 0 ? "" :
        string.Join(", ", def.Effects.Select(e => $"{T["bfx." + e.Key]} +{Fmt.Num(e.Value * level, e.Value * level >= 10 ? "N0" : "0.##")}"));
}
