using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

/// <summary>
/// FM2002-Attributindex auf FMH-Attribute. Namen mit Textbeleg stehen im Katalog;
/// Indizes ohne Beleg sind gekennzeichnet und liefern sonst den neutralen Wert 10.
/// </summary>
public static class AttributeMap
{
    public static Attr? Known(int index) => index switch
    {
        1 => Attr.Passing,
        2 => Attr.Heading,
        3 => Attr.LongShots,
        4 => Attr.Finishing,
        5 => Attr.Acceleration,
        6 => Attr.Pace,
        7 => Attr.Tackling,
        8 => Attr.Dribbling, // Annahme: Zweikampf/Ausspielen
        9 => Attr.Technique,
        10 => Attr.Crossing,
        11 => Attr.Reflexes,
        12 => Attr.Handling,
        13 => Attr.CommandOfArea,
        14 => Attr.OneOnOnes,
        15 => Attr.Kicking, // Annahme
        17 => Attr.Positioning,
        18 => Attr.WorkRate,
        20 => Attr.Composure,
        23 => Attr.Teamwork, // Annahme
        24 => Attr.Teamwork,
        26 => Attr.Agility, // Annahme: Flugparaden
        27 => Attr.Handling,
        28 => Attr.Balance, // Annahme
        29 => Attr.Strength, // Annahme
        30 => Attr.Penalties,
        32 => Attr.LongThrows, // Annahme
        33 => Attr.Flair, // Annahme
        37 => Attr.OffTheBall,
        38 => Attr.Anticipation,
        39 => Attr.Bravery, // Annahme
        45 => Attr.JumpingReach, // Annahme
        48 => Attr.Decisions,
        49 => Attr.Concentration, // Annahme
        _ => null,
    };

    /// <summary>true, wenn der Index nur eine konfigurierbare Annahme ist.</summary>
    public static bool IsAssumption(int index) => index is 8 or 15 or 23 or 26 or 28 or 29 or 32 or 33 or 39 or 45 or 49;

    public static double Value(MatchPlayer player, int index)
    {
        double raw = index == 22
            ? player.Player.Hidden.Sportsmanship
            : Known(index) is { } attr ? player.Player.Attributes[attr] : 10;
        raw += ((int)player.Morale - 3) * 0.35;
        raw += (player.Player.State.Form - 5) * 0.15;
        raw += player.DayForm * 0.35;
        raw *= 0.84 + 0.16 * player.Condition / 100.0;
        return Math.Clamp(raw, 1, 20);
    }
}
