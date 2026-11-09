using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

/// <summary>
/// Geländekarte Schema 2: Grundflächen nach realem Platzbedarf (eine Kachel ≈ 46 m, Maßstab des Stadions).
/// Spielstände mit Schema 1 haben Gebäude mit den alten, kleineren Flächen platziert; hier werden überlappende
/// Gebäude erst gedreht, dann auf einen freien Platz gesetzt. Reicht der Platz nicht, wächst das Gelände kostenlos
/// um Reihen. Danach liegen keine Wege mehr unter Gebäuden, und jedes Gebäude ist wieder angeschlossen.
/// </summary>
public sealed partial class FacilitiesModule
{
    /// <summary>Aktuelle Schemaversion der Geländekarte.</summary>
    public const int CurrentMapVersion = 2;

    private void FitFootprints(Club club)
    {
        var g = club.Grounds;
        g.MapVersion = CurrentMapVersion; // zuerst setzen: Occupancy/FreeSpot rufen EnsureGrounds erneut auf
        var placed = new HashSet<(int x, int y)>();
        void Occupy(Building b, BuildingDef def)
        {
            var (fw, fh) = BuildingFootprint.Size(def, b.Rotation);
            for (var x = b.X; x < b.X + fw; x++)
                for (var y = b.Y; y < b.Y + fh; y++)
                    placed.Add((x, y));
        }
        // Durchgang 1: Gebäude, die (auch gedreht) an ihrem Platz passen, bleiben stehen
        var pending = new List<(Building b, BuildingDef def)>();
        var moved = false;
        foreach (var b in g.Buildings.OrderBy(b => b.Id))
        {
            if (Data.Building(b.Type) is not { } def)
            {
                placed.Add((b.X, b.Y));
                continue;
            }
            var turned = b.Rotation is 0 or 180 ? 90 : 0;
            if (FitsAmong(club, def, b.X, b.Y, b.Rotation, placed, avoidPaths: false)) { }
            else if (FitsAmong(club, def, b.X, b.Y, turned, placed, avoidPaths: false)) (b.Rotation, moved) = (turned, true);
            else { pending.Add((b, def)); continue; }
            Occupy(b, def);
        }
        // Durchgang 2: die übrigen auf freie Plätze setzen, notfalls wächst das Gelände um Reihen
        moved |= pending.Count > 0;
        foreach (var (b, def) in pending)
        {
            var spot = SpotAmong(club, def, placed);
            for (var grow = 0; spot is null && grow < 12; grow++)
            {
                g.Height++;
                spot = SpotAmong(club, def, placed);
            }
            if (spot is null) continue; // kein Platz: Gebäude bleibt, wie es war
            (b.X, b.Y, b.Rotation) = (spot.Value.x, spot.Value.y, 0);
            Occupy(b, def);
        }
        // Auch ohne Umsetzung kann ein Weg unter einer nur größer gewordenen Fläche liegen
        var pathsBefore = g.Paths.Count;
        g.Paths = g.Paths.Where(p => !placed.Contains((p.X, p.Y))).ToList();
        if (!moved && g.Paths.Count == pathsBefore) return;
        EnsureBuildingsLinked(club);
        InvalidateEffects(club);
    }

    private bool FitsAmong(Club club, BuildingDef def, int x, int y, int rotation, HashSet<(int x, int y)> placed, bool avoidPaths)
    {
        var g = club.Grounds;
        var (fw, fh) = BuildingFootprint.Size(def, rotation);
        if (x < 0 || y < 0 || x + fw > GroundsWidth(club) || y + fh > g.Height) return false;
        for (var i = x; i < x + fw; i++)
            for (var j = y; j < y + fh; j++)
            {
                if (placed.Contains((i, j)) || g.IsStadiumCell(i, j) || g.Gates.Any(p => p.X == i && p.Y == j)) return false;
                if (avoidPaths && g.Paths.Any(p => p.X == i && p.Y == j)) return false;
            }
        return true;
    }

    /// <summary>Freier Platz für ein umzusetzendes Gebäude: bevorzugt ohne Wege, sonst auch über Wege.</summary>
    private (int x, int y)? SpotAmong(Club club, BuildingDef def, HashSet<(int x, int y)> placed)
    {
        foreach (var avoidPaths in new[] { true, false })
            for (var y = 0; y < club.Grounds.Height; y++)
                for (var x = 0; x < GroundsWidth(club); x++)
                    if (FitsAmong(club, def, x, y, 0, placed, avoidPaths)) return (x, y);
        return null;
    }
}
