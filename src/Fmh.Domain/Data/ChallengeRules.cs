using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Spielmodi (Datei <c>rules/challenges.json</c>, AP 12): Schnellsaison und Herausforderungen.</summary>
public sealed class ChallengeRules
{
    public QuickSeasonRule QuickSeason { get; set; } = new();
    public List<ChallengeRule> Challenges { get; set; } = new();
}

/// <summary>Schnellsaison: eine Saison, Entscheidungen und Spiele laufen ohne Halt.</summary>
public sealed class QuickSeasonRule
{
    /// <summary>Pflicht-Entscheidungen übernimmt der Assistent sofort.</summary>
    public bool DelegateDecisions { get; set; } = true;
    /// <summary>Eigene Spiele werden sofort berechnet; der Co-Trainer füllt Lücken in der Aufstellung.</summary>
    public bool InstantMatches { get; set; } = true;
}

/// <summary>Herausforderung: fester Verein (nach Land, Ebene und Auswahlregel), Startbedingungen, Ziel und Frist.</summary>
public sealed class ChallengeRule
{
    /// <summary>Kennung; Texte unter <c>challenge.&lt;id&gt;.name</c> und <c>challenge.&lt;id&gt;.info</c>.</summary>
    [Required]
    public string Id { get; set; } = "";
    /// <summary>Ländercode des Vereins.</summary>
    [Required]
    public string Country { get; set; } = "DE";
    /// <summary>Ligaebene (1 = höchste Liga).</summary>
    [Range(1, 10)]
    public int Level { get; set; } = 1;
    /// <summary>Auswahl in dieser Ebene: <c>weakest</c>, <c>median</c> oder <c>strongest</c> (nach Ruf).</summary>
    [RegularExpression("weakest|median|strongest")]
    public string Pick { get; set; } = "weakest";
    /// <summary>Frist in Saisons.</summary>
    [Range(1, 10)]
    public int Seasons { get; set; } = 1;
    /// <summary>Faktor auf den Kontostand zu Beginn.</summary>
    [Range(0, 5)]
    public double BalanceFactor { get; set; } = 1;
    /// <summary>Faktor auf das Transferbudget zu Beginn.</summary>
    [Range(0, 5)]
    public double TransferBudgetFactor { get; set; } = 1;
    public ChallengeGoal Goal { get; set; } = new();
}

/// <summary>
/// Ziel: <c>avoidRelegation</c> (bis zur Frist nie abgestiegen), <c>promotion</c> (bis zur Frist aufgestiegen),
/// <c>finishAtLeast</c> (Platz ≤ <see cref="Value"/> in der Startliga oder höher), <c>winCup</c> (einen Pokal oder
/// Europapokal gewonnen), <c>balanceAtLeast</c> (Kontostand ≥ <see cref="Value"/> Euro zum Saisonende).
/// </summary>
public sealed class ChallengeGoal
{
    [RegularExpression("avoidRelegation|promotion|finishAtLeast|winCup|balanceAtLeast")]
    public string Type { get; set; } = "avoidRelegation";
    public long Value { get; set; }
}
