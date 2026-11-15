using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>
/// Privatleben des Managers (Datei <c>rules/privateLife.json</c>, AP 13b): Besitz mit Kaufpreis, laufenden Kosten,
/// Prestige und Erholung, dazu Stressregeln. Prestige wirkt auf den Medienruf, Stress auf das Vorstandsvertrauen.
/// </summary>
public sealed class PrivateLifeRules
{
    /// <summary>Startvermögen in Wochengehältern des Managers.</summary>
    public double StartSavingsWeeks { get; set; } = 20;
    public List<PrivateItem> Homes { get; set; } = new();
    public List<PrivateItem> Cars { get; set; } = new();
    public List<PrivateItem> Hobbies { get; set; } = new();
    public int MaxHobbies { get; set; } = 2;
    /// <summary>Anteil des Kaufpreises, den ein Verkauf zurückbringt.</summary>
    public double ResaleShare { get; set; } = 0.6;
    public StressRules Stress { get; set; } = new();
}

/// <summary>Wohnung, Auto oder Hobby; Text unter <c>private.&lt;id&gt;</c>.</summary>
public sealed class PrivateItem
{
    [Required]
    public string Id { get; set; } = "";
    public long Price { get; set; }
    /// <summary>Laufende Kosten je Monat in Euro.</summary>
    public long Monthly { get; set; }
    /// <summary>Prestige: Medienruf je Monat.</summary>
    public double Prestige { get; set; }
    /// <summary>Erholung: zusätzlicher Stressabbau je Tag.</summary>
    public double Relief { get; set; }
}

public sealed class StressRules
{
    public double Win { get; set; } = -3;
    public double Draw { get; set; } = 1;
    public double Loss { get; set; } = 4;
    /// <summary>Grundabbau je Tag.</summary>
    public double DailyDecay { get; set; } = 0.3;
    /// <summary>Ab hier leidet das Vorstandsvertrauen (je Woche).</summary>
    public double High { get; set; } = 75;
    public double BoardPenaltyPerWeek { get; set; } = 1;
    /// <summary>Stress, wenn das Privatkonto ins Minus rutscht (je Monat).</summary>
    public double Debt { get; set; } = 8;
}
