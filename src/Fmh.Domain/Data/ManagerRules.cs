using System.ComponentModel.DataAnnotations;
using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>Regeln des Trainerkarussells (Datei <c>rules/managers.json</c>).</summary>
public sealed class ManagerRules
{
    /// <summary>Vereinslose Trainer: Anteil an den Ligavereinen, mindestens <see cref="PoolMinimum"/>.</summary>
    [Range(0, 1)]
    public double PoolShare { get; set; } = 0.125;
    [Range(0, 200)]
    public int PoolMinimum { get; set; } = 6;
    /// <summary>Vereinslose Trainer ab diesem Alter gehen zum Saisonende in den Ruhestand.</summary>
    [Range(50, 90)]
    public int RetirementAge { get; set; } = 70;
    /// <summary>Häufigkeit der Spielideen bei neuen Trainern (Gewichte).</summary>
    public Dictionary<ManagerStyle, double> StyleWeights { get; set; } = new()
    {
        [ManagerStyle.Balanced] = 40, [ManagerStyle.Attacking] = 18, [ManagerStyle.Defensive] = 16, [ManagerStyle.Counter] = 16, [ManagerStyle.Pressing] = 10,
    };
    /// <summary>Verschiebung der Grundhaltung je Spielidee (positiv = offensiver).</summary>
    public Dictionary<ManagerStyle, double> MentalityBias { get; set; } = new()
    {
        [ManagerStyle.Attacking] = 0.6, [ManagerStyle.Defensive] = -0.6, [ManagerStyle.Counter] = -0.35,
    };
    public SackingRules InSeason { get; set; } = new();
    public SeasonEndRules SeasonEnd { get; set; } = new();
    /// <summary>Kalibrierung: Entlassungen je Saison in einer 1. Liga (von–bis, Mittel über Saisons und Länder).</summary>
    public double[] SackingsPerTopLeague { get; set; } = { 1, 8 };
}

/// <summary>Entlassung während der Saison: alle Bedingungen müssen zutreffen.</summary>
public sealed class SackingRules
{
    /// <summary>Mindestamtszeit in Tagen.</summary>
    [Range(0, 1000)]
    public int MinTenureDays { get; set; } = 120;
    /// <summary>Frühestens nach diesem Anteil der Spieltage, mindestens nach <see cref="MinMatchday"/>.</summary>
    [Range(0, 1)]
    public double EarliestShare { get; set; } = 1.0 / 3;
    [Range(1, 60)]
    public int MinMatchday { get; set; } = 8;
    /// <summary>Plätze unter dem erwarteten Platz (nach Ruf): mindestens <see cref="MinBelow"/> bzw. dieser Anteil der Liga.</summary>
    [Range(0, 1)]
    public double BelowShare { get; set; } = 0.25;
    [Range(0, 40)]
    public int MinBelow { get; set; } = 4;
    /// <summary>Höchstens so viele Punkte aus den letzten fünf Spielen.</summary>
    [Range(0, 15)]
    public int MaxFormPoints { get; set; } = 3;
    /// <summary>Wer später als so viele Tage nach Saisonbeginn kam, wird in dieser Saison nicht mehr entlassen.</summary>
    [Range(0, 365)]
    public int NewcomerDays { get; set; } = 30;
}

/// <summary>Entlassung zum Saisonende (nur Trainer, die schon vor der Saison im Amt waren).</summary>
public sealed class SeasonEndRules
{
    /// <summary>Absteiger mit mindestens so vielen Plätzen unter der Erwartung.</summary>
    [Range(0, 40)]
    public int RelegatedBelow { get; set; } = 3;
    /// <summary>Sonst: mindestens <see cref="MinBelow"/> Plätze bzw. dieser Anteil der Liga unter der Erwartung.</summary>
    [Range(0, 1)]
    public double BelowShare { get; set; } = 0.5;
    [Range(0, 40)]
    public int MinBelow { get; set; } = 8;
    /// <summary>Ruf +1 für Meister und Vereine mindestens so weit (Anteil der Liga, mind. 2 Plätze) über der Erwartung.</summary>
    [Range(0, 1)]
    public double PraiseShare { get; set; } = 0.25;
}
