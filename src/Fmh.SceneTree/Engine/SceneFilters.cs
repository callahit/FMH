using Fmh.Core.Model;

namespace Fmh.SceneTree;

/// <summary>Harte Filter vor der Gewichtung: Rolle muss besetzt sein, Wetter und die belegten Bedingungsbits.</summary>
public sealed class SceneGate
{
    public Weather Weather { get; init; } = Weather.Sunny;
    public bool Floodlit { get; init; }
    public bool Shootout { get; init; }
    /// <summary>Tordifferenz aus Sicht der angreifenden Mannschaft.</summary>
    public int AttackLead { get; init; }
    public bool DefenceOffsideTrap { get; init; }
    /// <summary>Sondertexte (Byte 29) nur, wenn die Partie das Easter-Egg gezogen hat.</summary>
    public bool EasterEgg { get; init; }
    public required Func<RoleRef, bool> Occupied { get; init; }
}

public static class SceneFilters
{
    public static bool Allows(SceneNode node, SceneGate gate)
    {
        var c = node.Conditions;
        if ((c & SceneCondition.Wet) != 0 && gate.Weather is not (Weather.Rain or Weather.Snow)) return false;
        if ((c & SceneCondition.Icy) != 0 && gate.Weather != Weather.Snow) return false;
        if ((c & SceneCondition.Floodlit) != 0 && !gate.Floodlit) return false;
        if ((c & SceneCondition.LivePlay) != 0 && gate.Shootout) return false;
        if ((c & SceneCondition.ClearLead) != 0 && gate.AttackLead < 2) return false;
        // Bit 32 ist unklar und filtert nicht.
        if (node.SpecialId != 0 && !gate.EasterEgg) return false;
        if (node.OffsideTrap && !gate.DefenceOffsideTrap) return false;
        return RolesFit(node, gate.Occupied);
    }

    public static bool RolesFit(SceneNode node, Func<RoleRef, bool> occupied)
    {
        if (!node.Actor.IsEmpty && !occupied(node.Actor)) return false;
        if (!node.ModARole.IsEmpty && !occupied(node.ModARole)) return false;
        if (!node.ModBRole.IsEmpty && !occupied(node.ModBRole)) return false;
        if (!node.ModCRole.IsEmpty && !occupied(node.ModCRole)) return false;
        return true;
    }

    /// <summary>Wetter und Rolle, ohne Abseitsfalle und Sondertext. Fallback, wenn sonst niemand übrig bleibt.</summary>
    public static bool AllowsCore(SceneNode node, SceneGate gate)
    {
        var c = node.Conditions;
        if ((c & SceneCondition.Wet) != 0 && gate.Weather is not (Weather.Rain or Weather.Snow)) return false;
        if ((c & SceneCondition.Icy) != 0 && gate.Weather != Weather.Snow) return false;
        if ((c & SceneCondition.Floodlit) != 0 && !gate.Floodlit) return false;
        if ((c & SceneCondition.LivePlay) != 0 && gate.Shootout) return false;
        if ((c & SceneCondition.ClearLead) != 0 && gate.AttackLead < 2) return false;
        return RolesFit(node, gate.Occupied);
    }
}
