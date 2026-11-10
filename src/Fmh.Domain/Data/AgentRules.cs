using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Regeln der Spielerberater (Datei <c>rules/agents.json</c>, AP 7).</summary>
public sealed class AgentRules
{
    /// <summary>Profis je Berater (bestimmt die Zahl der Berater je Land, mindestens <see cref="MinPerCountry"/>).</summary>
    [Range(1, 500)]
    public int PlayersPerAgent { get; set; } = 30;
    [Range(1, 100)]
    public int MinPerCountry { get; set; } = 4;
    /// <summary>Honorar: Aufschlag je Punkt Gier über 10 (negativ darunter).</summary>
    [Range(0, 0.2)]
    public double FeePerGreed { get; set; } = 0.03;
    /// <summary>Gehaltsforderung: Aufschlag je Punkt Gier über 10.</summary>
    [Range(0, 0.1)]
    public double WagePerGreed { get; set; } = 0.012;
    /// <summary>Wechselwunsch unzufriedener Mandanten: Aufschlag auf die Chance je Punkt Einfluss über 10.</summary>
    [Range(0, 0.2)]
    public double RequestPerInfluence { get; set; } = 0.04;
    public UnsettleRules Unsettle { get; set; } = new();
}

/// <summary>Einflussreiche Berater bringen bei Mandanten, die für ihren Verein zu gut sind, einen Wechsel ins Gespräch.</summary>
public sealed class UnsettleRules
{
    /// <summary>Mindest-Einfluss des Beraters.</summary>
    [Range(1, 20)] public int MinInfluence { get; set; } = 13;
    /// <summary>Mandant hat mindestens so viel mehr Ruf als sein Verein.</summary>
    [Range(0, 20)] public int MinReputationGap { get; set; } = 4;
    /// <summary>Wöchentliche Chance.</summary>
    [Range(0, 1)] public double WeeklyChance { get; set; } = 0.06;
    /// <summary>Zufriedenheitsverlust.</summary>
    [Range(0, 50)] public int SatisfactionHit { get; set; } = 6;
}
