using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Positionsstärke und Gesamtstärke (Anstoß-Element, Kapitel 4).</summary>
public static class Ratings
{
    /// <summary>Gewichteter Durchschnitt der positionsrelevanten Attribute, 1–20.</summary>
    public static double PositionRating(Player p, Position pos, PositionProfiles profiles)
    {
        if (!profiles.Weights.TryGetValue(pos, out var weights) || weights.Count == 0)
            return AttrInfo.All.Average(a => p.Attributes[a]);
        double sum = 0, wsum = 0;
        foreach (var (attr, w) in weights)
        {
            sum += p.Attributes[attr] * w;
            wsum += w;
        }
        return wsum <= 0 ? 1 : sum / wsum;
    }

    /// <summary>Positionsstärke unter Berücksichtigung der Positionseignung.</summary>
    public static double EffectiveRating(Player p, Position pos, PositionProfiles profiles) =>
        PositionRating(p, pos, profiles) * PositionInfo.SuitabilityFactor(p.SuitabilityFor(pos));

    /// <summary>Gesamtstärke 1,0–13,0 auf der Hauptposition, eine Nachkommastelle.</summary>
    public static double Strength(Player p, PositionProfiles profiles) =>
        StrengthFromRating(PositionRating(p, p.MainPosition, profiles));

    public static double StrengthFromRating(double rating) =>
        Math.Round(Math.Clamp(1 + (rating - 1) * 12.0 / 19.0, 1, 13), 1);

    /// <summary>Aktuelle Fähigkeit 1–200 aus der Positionsstärke.</summary>
    public static int CurrentAbility(Player p, PositionProfiles profiles) =>
        (int)Math.Clamp(Math.Round((PositionRating(p, p.MainPosition, profiles) - 1) * 200.0 / 19.0), 1, 200);

    /// <summary>Mannschaftsstärke als Durchschnitt der Startelf auf ihren Positionen.</summary>
    public static double TeamRating(IEnumerable<(Player player, Position pos)> eleven, PositionProfiles profiles)
    {
        var list = eleven.ToList();
        return list.Count == 0 ? 0 : list.Average(x => EffectiveRating(x.player, x.pos, profiles));
    }

    public static string MoraleKey(Morale m) => "morale." + m;
}
