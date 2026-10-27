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

    public void InvalidateEffects(Club club) => _effects.Remove(club.Id);

    /// <summary>Summe einer Gebäudewirkung (Stufe × Wert, Zustand, Lagebonus, Synergien). Verpachtete Gebäude wirken nicht.</summary>
    public double Effect(Club club, string key)
    {
        if (!_effects.TryGetValue(club.Id, out var map))
        {
            map = ComputeEffects(club);
            _effects[club.Id] = map;
        }
        return map.TryGetValue(key, out var v) ? v : 0;
    }

    private Dictionary<string, double> ComputeEffects(Club club)
    {
        var map = new Dictionary<string, double>();
        var g = club.Grounds;
        var active = g.Buildings.Where(b => !b.Leased && !(b.UnderConstruction && b.Level == 0)).ToList();
        foreach (var b in active)
        {
            if (Data.Building(b.Type) is not { } def) continue;
            var condition = 0.5 + 0.5 * Math.Clamp(b.Condition, 0, 100) / 100;
            var location = LocationFactor(club, b, def);
            foreach (var (key, value) in def.Effects)
                map[key] = map.GetValueOrDefault(key) + value * b.Level * condition * location;
            foreach (var syn in def.Synergies.Where(s => active.Any(o => o.Type == s.With)))
                map[syn.Effect] = map.GetValueOrDefault(syn.Effect) + syn.Value;
        }
        return map;
    }

    /// <summary>Lagebonus: Gastronomie und Fanshop nahe am Stadioneingang, Trainingsanlagen nahe am Kabinentrakt.</summary>
    public double LocationFactor(Club club, Building b, BuildingDef def)
    {
        var g = club.Grounds;
        int Dist(int x, int y) => Math.Abs(b.X + def.Width / 2 - x) + Math.Abs(b.Y + def.Height / 2 - y);
        return def.Location switch
        {
            "entrance" => Dist(g.EntranceX, g.EntranceY) <= 3 ? 1.25 : 1.0,
            "dressing" => g.Buildings.FirstOrDefault(o => o.Type == "dressingRooms") is { } d && Dist(d.X, d.Y) <= 3 ? 1.2 : 1.0,
            _ => 1.0,
        };
    }

    public int BuildingLevel(Club club, string type) =>
        club.Grounds.Buildings.Where(b => b.Type == type && !b.Leased).Select(b => b.Level).DefaultIfEmpty(0).Max();

    // ================================================================== Gelände-Raster

    public int GroundsWidth(Club club) => club.Grounds.Width + club.Grounds.ExtraColumns;

    /// <summary>Belegung des Rasters: Gebäude-Id, -1 = Stadion, 0 = frei.</summary>
    public int[,] Occupancy(Club club)
    {
        var g = club.Grounds;
        var grid = new int[GroundsWidth(club), g.Height];
        for (var x = g.StadiumX; x < g.StadiumX + g.StadiumWidth; x++)
            for (var y = g.StadiumY; y < g.StadiumY + g.StadiumHeight; y++)
                grid[x, y] = -1;
        foreach (var b in g.Buildings)
        {
            var def = Data.Building(b.Type);
            for (var x = b.X; x < b.X + (def?.Width ?? 1); x++)
                for (var y = b.Y; y < b.Y + (def?.Height ?? 1); y++)
                    if (x < grid.GetLength(0) && y < grid.GetLength(1)) grid[x, y] = b.Id;
        }
        return grid;
    }

    public bool Fits(Club club, BuildingDef def, int x, int y)
    {
        var grid = Occupancy(club);
        if (x < 0 || y < 0 || x + def.Width > grid.GetLength(0) || y + def.Height > grid.GetLength(1)) return false;
        for (var i = x; i < x + def.Width; i++)
            for (var j = y; j < y + def.Height; j++)
                if (grid[i, j] != 0) return false;
        return true;
    }

    /// <summary>Erster freier Platz; Gebäude mit Lagebonus bevorzugt nahe am Ziel.</summary>
    public (int x, int y)? FreeSpot(Club club, BuildingDef def)
    {
        var spots = new List<(int x, int y)>();
        for (var y = 0; y < club.Grounds.Height; y++)
            for (var x = 0; x < GroundsWidth(club); x++)
                if (Fits(club, def, x, y)) spots.Add((x, y));
        if (spots.Count == 0) return null;
        var g = club.Grounds;
        return def.Location == "entrance"
            ? spots.OrderBy(s => Math.Abs(s.x - g.EntranceX) + Math.Abs(s.y - g.EntranceY)).First()
            : spots[0];
    }

    public long MonthlyUpkeep(Club club, Building b) =>
        Data.Building(b.Type) is { } def ? (long)(def.Upkeep * (1 + 0.5 * (b.Level - 1))) : 0;
}
