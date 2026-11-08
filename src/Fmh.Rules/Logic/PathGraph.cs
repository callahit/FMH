using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Wegenetz-Erreichbarkeit ab den Gelände-Toren (BFS), Distanz und Lagebonus nach Anstoß-3-Formel.
/// </summary>
public sealed class PathGraph
{
    public const int Unreachable = int.MaxValue;
    /// <summary>Kosten einer Wegkachel (G-20).</summary>
    public const long PathTileCost = 2_500;

    private readonly Dictionary<(int x, int y), int> _distance = new();

    public IReadOnlyDictionary<(int x, int y), int> Distances => _distance;

    /// <summary>BFS ab jedem Gate entlang der Pfadkacheln. Unerreichbare Kacheln fehlen in <see cref="Distances"/>.</summary>
    public static PathGraph Build(Grounds grounds, int width)
    {
        var graph = new PathGraph();
        var paths = new HashSet<(int x, int y)>(grounds.Paths.Select(p => (p.X, p.Y)));
        foreach (var gate in grounds.Gates)
            paths.Add((gate.X, gate.Y));

        var queue = new Queue<(int x, int y)>();
        foreach (var gate in grounds.Gates)
        {
            if (gate.X < 0 || gate.Y < 0 || gate.X >= width || gate.Y >= grounds.Height) continue;
            var key = (gate.X, gate.Y);
            if (graph._distance.ContainsKey(key)) continue;
            graph._distance[key] = 0;
            queue.Enqueue(key);
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            var dist = graph._distance[(x, y)];
            foreach (var (nx, ny) in Neighbors(x, y))
            {
                if (nx < 0 || ny < 0 || nx >= width || ny >= grounds.Height) continue;
                if (!paths.Contains((nx, ny))) continue;
                if (graph._distance.ContainsKey((nx, ny))) continue;
                graph._distance[(nx, ny)] = dist + 1;
                queue.Enqueue((nx, ny));
            }
        }
        return graph;
    }

    public int DistanceTo(int x, int y) => _distance.TryGetValue((x, y), out var d) ? d : Unreachable;

    /// <summary>Manhattan-Distanz zweier Rasterpunkte.</summary>
    public static int Manhattan(int x0, int y0, int x1, int y1) => Math.Abs(x0 - x1) + Math.Abs(y0 - y1);

    /// <summary>Lagebonus: 1.0 + 0.1 × max(0, 5 − d). Bei d=0 → 1.5, ab d≥5 → 1.0.</summary>
    public static double LageBonus(int manhattanDistance) => 1.0 + 0.1 * Math.Max(0, 5 - manhattanDistance);

    /// <summary>
    /// Gebäude ist angeschlossen, wenn mindestens eine orthogonal angrenzende Kachel
    /// ein erreichbarer Weg (oder ein Tor) ist.
    /// </summary>
    public bool IsConnected(Building building, int width, int height, int footprintW, int footprintH)
    {
        for (var x = building.X; x < building.X + footprintW; x++)
        for (var y = building.Y; y < building.Y + footprintH; y++)
        {
            foreach (var (nx, ny) in Neighbors(x, y))
            {
                if (nx >= building.X && nx < building.X + footprintW && ny >= building.Y && ny < building.Y + footprintH)
                    continue;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                if (_distance.ContainsKey((nx, ny))) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// L-förmigen Weg von (x0,y0) nach (x1,y1) berechnen (zuerst horizontal, dann vertikal).
    /// Bestehende Wege bleiben; Stadionzellen und belegte Gebäudezellen werden übersprungen.
    /// </summary>
    public static List<GridPos> DrawPathL(int x0, int y0, int x1, int y1, Func<int, int, bool> canPlace)
    {
        var cells = new List<GridPos>();
        var seen = new HashSet<(int, int)>();
        void Add(int x, int y)
        {
            if (!canPlace(x, y) || !seen.Add((x, y))) return;
            cells.Add(new GridPos(x, y));
        }
        var x = x0;
        var y = y0;
        Add(x, y);
        while (x != x1)
        {
            x += Math.Sign(x1 - x);
            Add(x, y);
        }
        while (y != y1)
        {
            y += Math.Sign(y1 - y);
            Add(x, y);
        }
        return cells;
    }

    public static IEnumerable<(int x, int y)> Neighbors(int x, int y)
    {
        yield return (x + 1, y);
        yield return (x - 1, y);
        yield return (x, y + 1);
        yield return (x, y - 1);
    }
}

/// <summary>Grundfläche eines Gebäudes unter Berücksichtigung der Rotation.</summary>
public static class BuildingFootprint
{
    public static (int width, int height) Size(BuildingDef def, int rotation)
    {
        var rot = ((rotation % 360) + 360) % 360;
        return rot is 90 or 270 ? (def.Height, def.Width) : (def.Width, def.Height);
    }

    public static int NormalizeRotation(int rotation)
    {
        var r = ((rotation % 360) + 360) % 360;
        return r / 90 * 90;
    }
}
