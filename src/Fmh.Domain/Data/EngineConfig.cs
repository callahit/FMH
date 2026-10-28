namespace Fmh.Core.Data;

/// <summary>
/// Gewichte und Wahrscheinlichkeiten der Matchengine. Werden aus rules/engine.json geladen und sind
/// im Editor bzw. per Datenpaket bearbeitbar. Die Standardwerte sind auf die Zielwerte aus Kapitel 6
/// kalibriert (siehe fmh-calibrate).
/// </summary>
public sealed class EngineConfig
{
    // Zeit
    public double StepSeconds { get; set; } = 6.0;
    public double RestartSeconds { get; set; } = 6.0;
    public double FoulSeconds { get; set; } = 25.0;
    public double GoalSeconds { get; set; } = 50.0;
    public double SetPieceSeconds { get; set; } = 20.0;
    public int MaxStepsPerPhase { get; set; } = 7;

    // Duelle
    /// <summary>Steigung der logistischen Duellfunktion je Attributpunkt Differenz.</summary>
    public double DuelSlope { get; set; } = 0.14;
    /// <summary>Heimvorteil auf alle Duellwerte (+4 %).</summary>
    public double HomeAdvantage { get; set; } = 0.04;
    /// <summary>Grundwahrscheinlichkeit eines erfolgreichen Passes in die Zielreihe 0–6.</summary>
    public double[] PassBaseByRow { get; set; } = { 0.97, 0.96, 0.94, 0.89, 0.76, 0.50, 0.36 };
    public double LongPassPenalty { get; set; } = 0.12;
    public double DribbleBase { get; set; } = 0.52;
    public double CrossBase { get; set; } = 0.42;
    public double AerialBase { get; set; } = 0.45;

    /// <summary>Gewichte für Pass, Dribbling, Flanke, Schuss je Reihe 0–6.</summary>
    public double[][] ActionWeights { get; set; } =
    {
        new[] { 0.92, 0.08, 0.00, 0.00 },
        new[] { 0.88, 0.12, 0.00, 0.00 },
        new[] { 0.82, 0.18, 0.00, 0.00 },
        new[] { 0.72, 0.22, 0.06, 0.00 },
        new[] { 0.58, 0.22, 0.16, 0.025 },
        new[] { 0.42, 0.22, 0.24, 0.065 },
        new[] { 0.45, 0.15, 0.00, 0.30 },
    };

    // Schüsse
    public double[] XgCentralByRow { get; set; } = { 0, 0, 0, 0.015, 0.03, 0.069, 0.168 };
    public double[] XgWideByRow { get; set; } = { 0, 0, 0, 0.01, 0.02, 0.052, 0.126 };
    public double HeaderXg { get; set; } = 0.10;
    public double OneOnOneXg { get; set; } = 0.38;
    public double OneOnOneChance { get; set; } = 0.22;
    public double PenaltyXg { get; set; } = 0.76;
    public double FreeKickXg { get; set; } = 0.06;
    public double DirectFreeKickShare { get; set; } = 0.45;
    public double BlockChance { get; set; } = 0.22;
    public double WallBlockChance { get; set; } = 0.25;
    public double OnTargetBase { get; set; } = 0.48;
    public double PostChance { get; set; } = 0.03;
    public double FinishingSlope { get; set; } = 0.05;
    public double KeeperSlope { get; set; } = 0.045;
    public double PressureSlope { get; set; } = 0.03;

    // Standards
    public double CornerFromBlock { get; set; } = 0.45;
    public double CornerFromSave { get; set; } = 0.25;
    public double CornerFromCross { get; set; } = 0.32;
    public double CornerHeaderChance { get; set; } = 0.35;
    public double CornerHeaderXg { get; set; } = 0.09;

    // Fouls und Karten
    public double FoulPerDuel { get; set; } = 0.17;
    public double YellowPerFoul { get; set; } = 0.148;
    public double RedPerFoul { get; set; } = 0.0025;
    /// <summary>Anteil der Fouls in Reihe 6 (zentral), die im Strafraum passieren.</summary>
    public double BoxFoulShare { get; set; } = 0.3;
    /// <summary>Anteil der Fouls in Reihe 5 (zentral, Strafraumgrenze), die noch im Strafraum passieren.</summary>
    public double EdgeFoulShare { get; set; } = 0.08;

    // Abseits
    public double OffsideBase { get; set; } = 0.032;

    // Verletzungen
    public double InjuryPerFoul { get; set; } = 0.012;
    public double InjuryPerPhase { get; set; } = 0.0003;

    // Kondition
    public double ConditionPerSecond { get; set; } = 0.0055;
    public double ConditionPerAction { get; set; } = 0.12;

    // Konter und Gegenpressing
    public double CounterChance { get; set; } = 0.45;
    public double CounterBonus { get; set; } = 1.2;
    public double GegenpressingRegain { get; set; } = 0.15;
    public double RecycleChance { get; set; } = 0.5;

    // KI im Spiel
    public double AiSubCondition { get; set; } = 66;
    public int AiEarliestSubMinute { get; set; } = 56;

    // Noten (1,0–6,0)
    public double GradeBase { get; set; } = 3.6;
    public double GradeScale { get; set; } = 1.0;
    public int GradeMinMinutes { get; set; } = 30;
}
