using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Stadion (Kapitel 8) und Vereinsgelände (Kapitel 9): Bauaufträge mit Genehmigung, Bauunternehmen und Verzögerungen,
/// Gebäudestufen, Zustand, Unterhalt sowie die Wirkungen der Gebäude auf Sport, Jugend, Finanzen und Fans.
/// </summary>
public sealed partial class FacilitiesModule : GameModule, IFacilitiesService
{
    public override string Id => "facilities";
    public override IReadOnlyList<string> DependsOn => new[] { "youth" };
    public override void OnDay(DayContext day) => ConstructionDaily();

    private readonly Dictionary<int, Dictionary<string, double>> _effects = new();
    private readonly Dictionary<int, PathGraph> _graphs = new();

    public void InvalidateEffects(Club club)
    {
        _effects.Remove(club.Id);
        _graphs.Remove(club.Id);
    }

    /// <summary>Summe einer Gebäudewirkung (Stufe × Wert × Zustand × Lage × Weganschluss + Synergien). Verpachtete Gebäude wirken nicht.</summary>
    public double Effect(Club club, string key)
    {
        EnsureGrounds(club);
        if (!_effects.TryGetValue(club.Id, out var map))
        {
            map = ComputeEffects(club);
            _effects[club.Id] = map;
        }
        return map.TryGetValue(key, out var v) ? v : 0;
    }

    public PathGraph PathGraphOf(Club club)
    {
        EnsureGrounds(club);
        if (!_graphs.TryGetValue(club.Id, out var graph))
        {
            graph = PathGraph.Build(club.Grounds, GroundsWidth(club));
            _graphs[club.Id] = graph;
        }
        return graph;
    }

    public void EnsureGrounds(Club club)
    {
        club.Grounds.EnsureCurrent();
        if (club.Grounds.MapVersion < CurrentMapVersion) FitFootprints(club);
    }

    private Dictionary<string, double> ComputeEffects(Club club)
    {
        var map = new Dictionary<string, double>();
        var g = club.Grounds;
        var graph = PathGraphOf(club);
        var width = GroundsWidth(club);
        var active = g.Buildings.Where(b => !b.Leased && !(b.UnderConstruction && b.Level == 0)).ToList();
        foreach (var b in active)
        {
            if (Data.Building(b.Type) is not { } def) continue;
            var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
            var condition = Math.Clamp(b.Condition, 0, 100) / 100.0;
            var location = LocationFactor(club, b, def);
            var connected = graph.IsConnected(b, width, g.Height, fw, fh);
            // Ohne Wegenetz (leere Paths): alle Gebäude gelten als nicht angeschlossen → 50 % (G-08).
            var link = connected ? 1.0 : 0.5;
            foreach (var (key, value) in def.Effects)
                map[key] = map.GetValueOrDefault(key) + value * b.Level * condition * location * link;
            foreach (var syn in def.Synergies.Where(s => active.Any(o => o.Type == s.With)))
                map[syn.Effect] = map.GetValueOrDefault(syn.Effect) + syn.Value * condition * link;
        }
        return map;
    }

    /// <summary>
    /// Lagebonus nach Anstoß-Formel: 1.0 + 0.1 × max(0, 5 − d).
    /// Bezugspunkt: Stadioneingang („entrance“) bzw. Kabinentrakt („dressing“).
    /// </summary>
    public double LocationFactor(Club club, Building b, BuildingDef def)
    {
        if (string.IsNullOrEmpty(def.Location)) return 1.0;
        var g = club.Grounds;
        var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
        var cx = b.X + fw / 2;
        var cy = b.Y + fh / 2;
        var target = def.Location switch
        {
            "entrance" => (g.EntranceX, g.EntranceY),
            "dressing" when g.Buildings.FirstOrDefault(o => o.Type == "dressingRooms") is { } d => (d.X, d.Y),
            _ => ((int?)null, (int?)null),
        };
        if (target.Item1 is not { } tx || target.Item2 is not { } ty) return 1.0;
        return PathGraph.LageBonus(PathGraph.Manhattan(cx, cy, tx, ty));
    }

    public bool IsConnected(Club club, Building b)
    {
        if (Data.Building(b.Type) is not { } def) return false;
        var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
        return PathGraphOf(club).IsConnected(b, GroundsWidth(club), club.Grounds.Height, fw, fh);
    }

    public int BuildingLevel(Club club, string type) =>
        club.Grounds.Buildings.Where(b => b.Type == type && !b.Leased).Select(b => b.Level).DefaultIfEmpty(0).Max();

    // ================================================================== Gelände-Raster

    public int GroundsWidth(Club club) => club.Grounds.Width + club.Grounds.ExtraColumns;

    /// <summary>Belegung des Rasters: Gebäude-Id, -1 = Stadion, -2 = Weg, 0 = frei.</summary>
    public int[,] Occupancy(Club club)
    {
        EnsureGrounds(club);
        var g = club.Grounds;
        var grid = new int[GroundsWidth(club), g.Height];
        for (var x = g.StadiumX; x < g.StadiumX + g.StadiumWidth; x++)
            for (var y = g.StadiumY; y < g.StadiumY + g.StadiumHeight; y++)
                if ((uint)x < (uint)grid.GetLength(0) && (uint)y < (uint)grid.GetLength(1))
                    grid[x, y] = -1;
        foreach (var p in g.Paths)
            if ((uint)p.X < (uint)grid.GetLength(0) && (uint)p.Y < (uint)grid.GetLength(1) && grid[p.X, p.Y] == 0)
                grid[p.X, p.Y] = -2;
        foreach (var b in g.Buildings)
        {
            if (Data.Building(b.Type) is not { } def) continue;
            var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
            for (var x = b.X; x < b.X + fw; x++)
                for (var y = b.Y; y < b.Y + fh; y++)
                    if (x < grid.GetLength(0) && y < grid.GetLength(1)) grid[x, y] = b.Id;
        }
        return grid;
    }

    public bool Fits(Club club, BuildingDef def, int x, int y, int rotation = 0)
    {
        var grid = Occupancy(club);
        var (fw, fh) = BuildingFootprint.Size(def, rotation);
        if (x < 0 || y < 0 || x + fw > grid.GetLength(0) || y + fh > grid.GetLength(1)) return false;
        for (var i = x; i < x + fw; i++)
            for (var j = y; j < y + fh; j++)
                if (grid[i, j] != 0) return false;
        return true;
    }

    /// <summary>Erster freier Platz; bevorzugt angeschlossene Felder und Lagebonus-Ziele.</summary>
    public (int x, int y)? FreeSpot(Club club, BuildingDef def)
    {
        var spots = new List<(int x, int y)>();
        for (var y = 0; y < club.Grounds.Height; y++)
            for (var x = 0; x < GroundsWidth(club); x++)
                if (Fits(club, def, x, y)) spots.Add((x, y));
        if (spots.Count == 0) return null;
        var g = club.Grounds;
        EnsureGrounds(club);
        var paths = new HashSet<(int, int)>(g.Paths.Select(p => (p.X, p.Y)));
        foreach (var gate in g.Gates) paths.Add((gate.X, gate.Y));
        int TouchScore(int sx, int sy)
        {
            var (fw, fh) = BuildingFootprint.Size(def, 0);
            var touch = 0;
            for (var x = sx; x < sx + fw; x++)
            for (var y = sy; y < sy + fh; y++)
            foreach (var n in PathGraph.Neighbors(x, y))
                if (paths.Contains(n)) touch++;
            return touch;
        }
        IOrderedEnumerable<(int x, int y)> ordered = spots.OrderByDescending(s => TouchScore(s.x, s.y));
        if (def.Location == "entrance")
            ordered = ordered.ThenBy(s => Math.Abs(s.x - g.EntranceX) + Math.Abs(s.y - g.EntranceY));
        return ordered.First();
    }

    public long MonthlyUpkeep(Club club, Building b) =>
        Data.Building(b.Type) is { } def ? (long)(def.Upkeep * (1 + 0.5 * (b.Level - 1))) : 0;

    /// <summary>L-förmigen Weg anlegen und Kosten abbuchen. Fehlertext-Schlüssel oder null.</summary>
    public string? DrawPath(Club club, int x0, int y0, int x1, int y1, bool free = false)
    {
        EnsureGrounds(club);
        var width = GroundsWidth(club);
        var grid = Occupancy(club);
        bool CanPlace(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= club.Grounds.Height) return false;
            var cell = grid[x, y];
            return cell is 0 or -2; // frei oder bereits Weg
        }
        var cells = PathGraph.DrawPathL(x0, y0, x1, y1, CanPlace);
        var neu = cells.Where(c => !club.Grounds.Paths.Any(p => p.X == c.X && p.Y == c.Y)).ToList();
        if (neu.Count == 0) return "build.error.pathNone";
        var cost = neu.Count * PathGraph.PathTileCost;
        if (!free)
        {
            if (cost > club.Budget.Balance) return "neg.error.balance";
            Finance.Book(club, TransactionType.Construction, -cost, TR("tx.path"));
        }
        var set = new HashSet<(int, int)>(club.Grounds.Paths.Select(p => (p.X, p.Y)));
        foreach (var c in neu) set.Add((c.X, c.Y));
        club.Grounds.Paths = set.Select(p => new GridPos(p.Item1, p.Item2)).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        InvalidateEffects(club);
        return null;
    }

    /// <summary>Wegkacheln entfernen (keine Rückerstattung).</summary>
    public void ErasePath(Club club, IEnumerable<GridPos> cells)
    {
        var remove = new HashSet<(int, int)>(cells.Select(c => (c.X, c.Y)));
        club.Grounds.Paths = club.Grounds.Paths.Where(p => !remove.Contains((p.X, p.Y))).ToList();
        InvalidateEffects(club);
    }
}
