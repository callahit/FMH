using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Startausstattung des Vereinsgeländes: Straßenraster, Gebäudeplatzierung, Weganschluss.</summary>
public sealed partial class FacilitiesModule
{
    /// <summary>Startgebäude nach Vereinsgröße, inkl. Gate und Wegenetz (MapVersion 1).</summary>
    public void CreateGrounds(Club club)
    {
        var r = club.Reputation;
        club.Grounds = new Grounds
        {
            // Grundflächen nach realem Platzbedarf (Schema 2): mehr Fläche, damit Plätze mit Auslauf hineinpassen
            Width = r >= 10 ? 18 : r >= 5 ? 15 : 12,
            Height = r >= 10 ? 11 : r >= 5 ? 10 : 9,
            MapVersion = CurrentMapVersion,
        };
        var g = club.Grounds;
        g.StadiumX = g.Width / 2 - 2;
        g.Gates = new List<GridPos> { new(g.EntranceX, Math.Min(g.Height - 1, g.EntranceY)) };
        // Straßenraster zuerst: Gebäude werden nur auf freien (Nicht-Weg-)Feldern platziert und sind damit angeschlossen.
        SeedStreetGrid(g);
        var start = new (string type, int level, int count)[]
        {
            ("dressingRooms", 1 + r / 5, 1), ("trainingPitch", Math.Max(1, r / 4), r >= 12 ? 3 : r >= 6 ? 2 : 1), ("gym", r / 5, 1),
            ("physio", 1 + r / 6, 1), ("youthCentre", r / 4, 1), ("youthPitches", r / 7, 1), ("fanshop", 1 + r / 5, 1),
            ("snackBar", 1 + r / 8, 1 + r / 6), ("beerGarden", r / 8, 1), ("parking", 1 + r / 5, 1), ("office", 1 + r / 6, 1),
            ("scoutingDept", r / 6, 1), ("videoRoom", r / 8, 1), ("goalkeeperArea", r / 8, 1), ("clubhouse", 1, 1),
            ("museum", r / 9, 1), ("businessClub", r / 8, 1), ("medicalCentre", 0, 0), ("clinic", r / 12, 1), ("restaurant", r / 10, 1),
        };
        foreach (var (type, level, count) in start)
        {
            if (level <= 0 || Data.Building(type) is not { } def) continue;
            for (var i = 0; i < count; i++)
            {
                if (FreeSpot(club, def) is not { } spot) break;
                Place(club, def, spot.x, spot.y, Math.Min(def.MaxLevel, level));
            }
        }
        EnsureBuildingsLinked(club);
        InvalidateEffects(club);
    }

    /// <summary>Fehlende Anschlüsse nach der Platzierung per BFS über freie Felder nachziehen.</summary>
    private void EnsureBuildingsLinked(Club club)
    {
        var g = club.Grounds;
        var width = GroundsWidth(club);
        var blocked = new HashSet<(int x, int y)>();
        foreach (var b in g.Buildings)
        {
            if (Data.Building(b.Type) is not { } def) { blocked.Add((b.X, b.Y)); continue; }
            var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
            for (var x = b.X; x < b.X + fw; x++)
                for (var y = b.Y; y < b.Y + fh; y++)
                    blocked.Add((x, y));
        }
        bool Free(int x, int y) =>
            x >= 0 && y >= 0 && x < width && y < g.Height
            && !blocked.Contains((x, y))
            && !(x >= g.StadiumX && x < g.StadiumX + g.StadiumWidth && y >= g.StadiumY && y < g.StadiumY + g.StadiumHeight);

        var paths = new HashSet<(int x, int y)>(g.Paths.Select(p => (p.X, p.Y)));
        foreach (var gate in g.Gates) paths.Add((gate.X, gate.Y));

        foreach (var b in g.Buildings)
        {
            if (Data.Building(b.Type) is not { } def) continue;
            var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
            g.Paths = paths.Select(p => new GridPos(p.Item1, p.Item2)).ToList();
            if (PathGraph.Build(g, width).IsConnected(b, width, g.Height, fw, fh)) continue;

            var anchors = new HashSet<(int x, int y)>();
            for (var x = b.X; x < b.X + fw; x++)
            for (var y = b.Y; y < b.Y + fh; y++)
            foreach (var n in PathGraph.Neighbors(x, y))
                if (Free(n.x, n.y)) anchors.Add(n);
            if (anchors.Count == 0) continue;

            var came = new Dictionary<(int x, int y), (int x, int y)?>();
            var q = new Queue<(int x, int y)>();
            foreach (var s in paths)
            {
                if (!Free(s.Item1, s.Item2) && !g.Gates.Any(gt => gt.X == s.Item1 && gt.Y == s.Item2)) continue;
                q.Enqueue(s);
                came[s] = null;
            }
            (int x, int y)? hit = null;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (anchors.Contains(cur)) { hit = cur; break; }
                foreach (var n in PathGraph.Neighbors(cur.x, cur.y))
                {
                    if (!Free(n.x, n.y) || came.ContainsKey(n)) continue;
                    came[n] = cur;
                    q.Enqueue(n);
                }
            }
            if (hit is null) continue;
            for (var step = hit; step is { } s;)
            {
                paths.Add(s);
                var prev = came[s];
                if (prev is null) break;
                step = prev;
            }
        }
        g.Paths = paths.Select(p => new GridPos(p.Item1, p.Item2)).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
    }

    /// <summary>Haupt- und Querwege ums Stadion, bevor Gebäude gesetzt werden (locker, damit Bauplätze bleiben).</summary>
    private static void SeedStreetGrid(Grounds g)
    {
        var width = g.Width + g.ExtraColumns;
        var path = new HashSet<(int x, int y)>();
        var gate = g.Gates[0];
        void Add(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= g.Height) return;
            if (x >= g.StadiumX && x < g.StadiumX + g.StadiumWidth && y >= g.StadiumY && y < g.StadiumY + g.StadiumHeight) return;
            path.Add((x, y));
        }
        // Vertikale Hauptstraße vom Stadioneingang
        for (var y = gate.Y; y < g.Height; y++) Add(gate.X, y);
        // Eine Querstraße unter dem Stadion und eine in der Mitte
        foreach (var y in new[] { gate.Y, Math.Min(g.Height - 1, gate.Y + 2), g.Height / 2, g.Height - 1 })
            for (var x = 0; x < width; x++) Add(x, y);
        // Zwei weitere Nord-Süd-Achsen
        foreach (var x in new[] { 1, width / 2, width - 2 })
            for (var y = gate.Y; y < g.Height; y++) Add(x, y);
        g.Paths = path.Select(p => new GridPos(p.x, p.y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
    }
}
