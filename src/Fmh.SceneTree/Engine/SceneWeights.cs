using Fmh.Core.Model;

namespace Fmh.SceneTree;

public readonly record struct SceneMood(double Attack, double Defence, PassingLength Passing, int RefereeStrictness);

/// <summary>
/// w = Grundgewicht + b32 + Skala · (bis zu drei Attributterme).
/// Attributbyte ≥ 50 bedeutet Index (Byte − 50) und wirkt umgekehrt (21 − Spielerwert).
/// </summary>
public static class SceneWeights
{
    public static double Compute(SceneNode node, SceneWeightOptions options, Func<RoleRef, int, double?> attribute, SceneMood mood)
    {
        var w = options.Ground + options.BaseScale * node.BaseWeight;
        w += Term(node.ModARole, node.ModAAttr, attribute, options.AttributeScale);
        w += Term(node.ModBRole, node.ModBAttr, attribute, options.AttributeScale);
        w += Term(node.ModCRole, node.ModCAttr, attribute, options.AttributeScale);
        if (node.BaseWeight == 0 && node.HasModifier)
            w = Math.Max(w, options.ModifierFloor);
        if (node.MoodAtt != 5)
            w *= Math.Max(0.45, 1 - Math.Abs(mood.Attack - node.MoodAtt) * options.MoodSlope);
        if (node.MoodDef != 5)
            w *= Math.Max(0.45, 1 - Math.Abs(mood.Defence - node.MoodDef) * options.MoodSlope);
        w *= PassingFactor(node.Pass, mood.Passing);
        w *= RefereeFactor(node, mood.RefereeStrictness);
        return Math.Max(0, w);
    }

    public static double Term(RoleRef role, byte attr, Func<RoleRef, int, double?> attribute, double scale)
    {
        if (role.IsEmpty || attr == 0 || scale == 0) return 0;
        var inverted = attr >= 50;
        var index = inverted ? attr - 50 : attr;
        var value = attribute(role, index) ?? 10;
        if (inverted) value = 21 - value;
        return scale * value;
    }

    private static double PassingFactor(PassKind pass, PassingLength length) => length switch
    {
        PassingLength.Short when (pass & PassKind.Long) != 0 => 0.72,
        PassingLength.Long when (pass & PassKind.Short) != 0 => 0.72,
        PassingLength.Long when (pass & PassKind.Long) != 0 => 1.12,
        PassingLength.Short when (pass & PassKind.Short) != 0 => 1.12,
        _ => 1,
    };

    /// <summary>Byte 31 ist eine Hypothese (Schiri-Stil). Die Wirkung bleibt klein.</summary>
    private static double RefereeFactor(SceneNode node, int strictness)
    {
        var tilt = (strictness - 10) / 20.0;
        var style = node.RefStyle switch
        {
            1 => 1 + 0.2 * tilt,
            2 => 1 + 0.08 * tilt,
            4 => 1 - 0.2 * tilt,
            8 => 1 + 0.12 * tilt,
            _ => 1,
        };
        return Math.Clamp(style, 0.75, 1.3);
    }
}
