namespace Fmh.Core.Model;

/// <summary>Positionen. Anzeige deutsch über die Sprachdatei (TW, IV, LV ...).</summary>
public enum Position { GK, DL, DC, DR, DM, ML, MC, MR, AML, AMC, AMR, ST }

/// <summary>Eignung für eine Position (Kapitel 4: Natural, Gut, Ausreichend, Ungeeignet).</summary>
public enum Suitability { Unsuitable = 0, Adequate = 1, Good = 2, Natural = 3 }

/// <summary>Aufgabe eines Spielers in der Formation.</summary>
public enum Duty { Defend, Support, Attack }

public static class PositionInfo
{
    public static readonly Position[] All = Enum.GetValues<Position>();

    public static string TextKey(Position p) => "pos." + p;

    public static double SuitabilityFactor(Suitability s) => s switch
    {
        Suitability.Natural => 1.0,
        Suitability.Good => 0.93,
        Suitability.Adequate => 0.82,
        _ => 0.55,
    };

    public static bool IsDefender(Position p) => p is Position.DL or Position.DC or Position.DR;
    public static bool IsMidfielder(Position p) => p is Position.DM or Position.ML or Position.MC or Position.MR;
    public static bool IsAttacker(Position p) => p is Position.AML or Position.AMC or Position.AMR or Position.ST;
    public static bool IsWide(Position p) => p is Position.DL or Position.DR or Position.ML or Position.MR or Position.AML or Position.AMR;

    /// <summary>Verwandte Positionen, die ein Spieler oft zusätzlich beherrscht.</summary>
    public static IReadOnlyList<Position> Neighbours(Position p) => p switch
    {
        Position.GK => Array.Empty<Position>(),
        Position.DL => new[] { Position.ML, Position.DC },
        Position.DR => new[] { Position.MR, Position.DC },
        Position.DC => new[] { Position.DM, Position.DL, Position.DR },
        Position.DM => new[] { Position.MC, Position.DC },
        Position.ML => new[] { Position.AML, Position.DL, Position.MC },
        Position.MR => new[] { Position.AMR, Position.DR, Position.MC },
        Position.MC => new[] { Position.DM, Position.AMC, Position.ML, Position.MR },
        Position.AML => new[] { Position.ML, Position.ST, Position.AMC },
        Position.AMR => new[] { Position.MR, Position.ST, Position.AMC },
        Position.AMC => new[] { Position.MC, Position.ST },
        Position.ST => new[] { Position.AMC, Position.AML, Position.AMR },
        _ => Array.Empty<Position>(),
    };
}
