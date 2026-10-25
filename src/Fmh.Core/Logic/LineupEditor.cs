using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Bearbeitung der Aufstellung durch den Manager: Spieler auf Platz oder Bank setzen, tauschen, entfernen.
/// Platz 0..n-1 sind die Formations-Slots, danach folgen die Bankplätze.
/// </summary>
public static class LineupEditor
{
    /// <summary>Setzt einen Spieler auf einen Slot oder Bankplatz. Der bisherige Inhaber übernimmt den alten Platz des Spielers.</summary>
    public static void Place(Tactic tactic, int slotCount, int benchSize, int target, int playerId)
    {
        while (tactic.Lineup.Count < slotCount) tactic.Lineup.Add(null);
        if (target < 0 || target >= slotCount + benchSize) return;

        int? previous = target < slotCount
            ? tactic.Lineup[target]
            : target - slotCount < tactic.Bench.Count ? tactic.Bench[target - slotCount] : null;
        if (previous == playerId) return;

        var fromSlot = tactic.Lineup.IndexOf(playerId);
        var fromBench = tactic.Bench.IndexOf(playerId);

        if (fromSlot >= 0) tactic.Lineup[fromSlot] = previous;
        if (fromBench >= 0)
        {
            if (previous is { } prev) tactic.Bench[fromBench] = prev;
            else tactic.Bench.RemoveAt(fromBench);
        }

        if (target < slotCount) tactic.Lineup[target] = playerId;
        else
        {
            var b = target - slotCount;
            if (fromBench >= 0 && fromBench < b && previous is null) b--;
            if (b < tactic.Bench.Count) tactic.Bench[b] = playerId;
            else tactic.Bench.Add(playerId);
        }
        // Wer aus der Startelf auf einen leeren Platz wechselt, hinterlässt eine Lücke; Doppelungen entfernen.
        tactic.Bench = tactic.Bench.Where(id => !tactic.Lineup.Contains(id)).Distinct().Take(benchSize).ToList();
    }

    /// <summary>Spieler auf die Bank setzen (ans Ende, wenn Platz ist).</summary>
    public static bool ToBench(Tactic tactic, int benchSize, int playerId)
    {
        if (tactic.Bench.Contains(playerId)) return true;
        if (tactic.Bench.Count >= benchSize) return false;
        var slot = tactic.Lineup.IndexOf(playerId);
        if (slot >= 0) tactic.Lineup[slot] = null;
        tactic.Bench.Add(playerId);
        return true;
    }

    /// <summary>Spieler aus Startelf und Bank nehmen.</summary>
    public static void Remove(Tactic tactic, int playerId)
    {
        var slot = tactic.Lineup.IndexOf(playerId);
        if (slot >= 0) tactic.Lineup[slot] = null;
        tactic.Bench.Remove(playerId);
    }

    /// <summary>Zwei Slots der Startelf tauschen.</summary>
    public static void Swap(Tactic tactic, int a, int b)
    {
        if (a < 0 || b < 0 || a >= tactic.Lineup.Count || b >= tactic.Lineup.Count) return;
        (tactic.Lineup[a], tactic.Lineup[b]) = (tactic.Lineup[b], tactic.Lineup[a]);
    }
}
