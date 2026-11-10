using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Testspiele (Datei <c>rules/friendlies.json</c>, AP 9): Vereinbarung, Einnahmen und Wirkung.</summary>
public sealed class FriendlyRules
{
    /// <summary>Mindestabstand in Tagen zu jedem anderen Spiel beider Vereine.</summary>
    [Range(0, 7)]
    public int MinRestDays { get; set; } = 2;
    /// <summary>Wie weit im Voraus ein Testspiel vereinbart werden kann (Tage).</summary>
    [Range(1, 365)]
    public int MaxDaysAhead { get; set; } = 120;
    /// <summary>Höchstzahl offener (noch nicht gespielter) Testspiele des eigenen Vereins.</summary>
    [Range(1, 20)]
    public int MaxOpen { get; set; } = 6;
    /// <summary>Anteil der üblichen Zuschauerzahl bei einem Testspiel.</summary>
    [Range(0, 1)]
    public double AttendanceFactor { get; set; } = 0.35;
    /// <summary>Auswahl für den Anteil des Gastgebers an den Zuschauereinnahmen (Prozent).</summary>
    public List<int> HomeShares { get; set; } = new() { 100, 70, 50 };
    /// <summary>Taktikvertrautheit je Testspiel (Pflichtspiele: 2,5).</summary>
    [Range(0, 20)]
    public double FamiliarityGain { get; set; } = 5;
    /// <summary>Spielpraxis je 90 Minuten (Pflichtspiele: 22).</summary>
    [Range(0, 50)]
    public double MatchPracticeGain { get; set; } = 25;
    /// <summary>Fortschritt im Konditionstraining je 90 Minuten (Trainingspunkte Physis).</summary>
    [Range(0, 5)]
    public double PhysicalGain { get; set; } = 0.6;
    /// <summary>Verstärkung von Vertrautheit und Physis bei Testspielen im Trainingslager.</summary>
    [Range(1, 3)]
    public double CampBonus { get; set; } = 1.5;
    /// <summary>Ein deutlich bekannterer Gegner (Ruf mehr als so viele Punkte höher) kommt nicht ins eigene Stadion; auswärts sagt er bis zum doppelten Abstand zu.</summary>
    [Range(0, 20)]
    public int AcceptReputationGap { get; set; } = 3;
    /// <summary>Wechsel je Testspiel.</summary>
    [Range(3, 11)]
    public int Substitutions { get; set; } = 9;
    /// <summary>Sommerturniere: vier Vereine, Halbfinale und zwei Tage später Finale und Spiel um Platz 3 (AP 9b).</summary>
    public List<SummerTournamentRule> SummerTournaments { get; set; } = new();
}

/// <summary>Sommerturnier mit vier Vereinen auf neutralem Platz; Prämien je Platz (1 bis 4).</summary>
public sealed class SummerTournamentRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Tag der Halbfinals (MM-TT, in der Vorbereitung).</summary>
    public string Date { get; set; } = "07-20";
    /// <summary>Tage zwischen Halbfinale und Finale.</summary>
    [Range(1, 7)]
    public int DaysToFinal { get; set; } = 2;
    /// <summary>Höchster Rufabstand der eingeladenen Gegner.</summary>
    [Range(0, 20)]
    public int ReputationRange { get; set; } = 2;
    /// <summary>Prämie für Platz 1 bis 4 (Euro).</summary>
    public List<long> Prizes { get; set; } = new() { 250_000, 150_000, 100_000, 75_000 };
}
