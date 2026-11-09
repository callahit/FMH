using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Regeln der Kabine (Datei <c>rules/relations.json</c>, AP 7): Zufriedenheit, Wechselwünsche, Versprechen, Vertrauen, Hierarchie.</summary>
public sealed class RelationsConfig
{
    /// <summary>Unter dieser Zufriedenheit (0–100) gilt ein Spieler als unzufrieden.</summary>
    [Range(0, 100)]
    public int UnhappyThreshold { get; set; } = 30;
    /// <summary>Tage zwischen zwei Einzelgesprächen mit demselben Spieler.</summary>
    [Range(0, 120)]
    public int TalkCooldownDays { get; set; } = 14;
    /// <summary>Einsatzzeit zählt erst ab diesem Spieltag (vorher hat noch niemand genug gespielt).</summary>
    [Range(0, 20)]
    public int GraceMatchdays { get; set; } = 4;
    /// <summary>Ab dieser Zufriedenheit erlischt ein Wechselwunsch wieder.</summary>
    [Range(0, 100)]
    public int WantsTransferResetAt { get; set; } = 40;
    /// <summary>Einsatzzeit: Zufriedenheitspunkte je 100 % Abweichung vom erwarteten Anteil, begrenzt auf Min … Max.</summary>
    [Range(0, 100)]
    public double PlayingTimeSlope { get; set; } = 20;
    [Range(-100, 0)]
    public int PlayingTimeMin { get; set; } = -25;
    [Range(0, 100)]
    public int PlayingTimeMax { get; set; } = 20;
    /// <summary>Saisonziel: Zufriedenheitspunkte je Tabellenplatz Abstand zum Zielplatz, begrenzt auf Min … Max.</summary>
    [Range(0, 20)]
    public double GoalsPerPlace { get; set; } = 2.5;
    [Range(-100, 0)]
    public int GoalsMin { get; set; } = -15;
    [Range(0, 100)]
    public int GoalsMax { get; set; } = 12;
    public TransferRequestRules TransferRequest { get; set; } = new();
    public PromiseRules Promises { get; set; } = new();
    public TrustRules Trust { get; set; } = new();
    public HierarchyRules Hierarchy { get; set; } = new();
    /// <summary>
    /// Wirkung der Moral auf den Trainingsfortschritt je Stufe über bzw. unter „Okay“ (sieben Stufen; 0,0167 = ±5 % an den
    /// Enden). Zufriedenere Kader entwickeln sich schneller; zu viel davon lässt die Ligastärke wachsen (Drift).
    /// </summary>
    [Range(0, 0.1)]
    public double TrainingMoralePerStep { get; set; } = 0.0167;
    /// <summary>Kalibrierung: Anteil unzufriedener Spieler je Kader in Prozent (von–bis, Mittel über die Saison).</summary>
    public double[] UnhappyShare { get; set; } = { 5, 20 };
}

/// <summary>Wöchentliche Chance eines unzufriedenen Spielers, einen Wechsel zu fordern.</summary>
public sealed class TransferRequestRules
{
    [Range(0, 1)] public double Base { get; set; } = 0.12;
    /// <summary>Zuschlag je Zufriedenheitspunkt unter 20.</summary>
    [Range(0, 1)] public double PerPointBelow20 { get; set; } = 0.01;
    /// <summary>Zuschlag je Punkt Ehrgeiz (1–20).</summary>
    [Range(0, 1)] public double PerAmbition { get; set; } = 0.008;
    /// <summary>Faktor für sehr treue Spieler (Loyalität ab 15).</summary>
    [Range(0, 1)] public double LoyalFactor { get; set; } = 0.45;
    [Range(0, 1)] public double Min { get; set; } = 0.05;
    [Range(0, 1)] public double Max { get; set; } = 0.55;
}

/// <summary>Wirkung gehaltener und gebrochener Versprechen auf die Zufriedenheit des Spielers.</summary>
public sealed class PromiseRules
{
    [Range(0, 100)] public int HeldBonus { get; set; } = 12;
    [Range(0, 100)] public int ImplicitHeldBonus { get; set; } = 4;
    [Range(0, 100)] public int BrokenPenalty { get; set; } = 25;
    [Range(0, 100)] public int ImplicitBrokenPenalty { get; set; } = 14;
}

/// <summary>Vertrauen des Kaders in den Trainer (0–100): sinkt mit gebrochenen Versprechen, erholt sich langsam.</summary>
public sealed class TrustRules
{
    [Range(0, 100)] public double Start { get; set; } = 60;
    /// <summary>Vertrauensverlust je gebrochenem (ausdrücklichem) Versprechen; implizite zählen halb.</summary>
    [Range(0, 100)] public double BrokenPromise { get; set; } = 8;
    [Range(0, 100)] public double HeldPromise { get; set; } = 3;
    /// <summary>Wöchentliche Erholung Richtung Startwert.</summary>
    [Range(0, 10)] public double WeeklyRecovery { get; set; } = 0.5;
    /// <summary>Zufriedenheitspunkte je 4 Punkte Vertrauen über bzw. unter dem Startwert (begrenzt auf −8 … +5).</summary>
    [Range(0, 5)] public double SatisfactionPer4 { get; set; } = 1;
}

/// <summary>Hierarchie im Kader: Führungsspieler, Etablierte, Mitläufer; Mannschaftsrat.</summary>
public sealed class HierarchyRules
{
    /// <summary>Mindestwert Führungsqualität für Führungsspieler.</summary>
    [Range(1, 20)] public int LeaderMinLeadership { get; set; } = 10;
    /// <summary>Anteil Etablierter am Kader (ohne Führungsspieler).</summary>
    [Range(0, 1)] public double EstablishedShare { get; set; } = 0.35;
    /// <summary>Mitglieder im Mannschaftsrat (Führungsspieler plus Etablierte mit der höchsten Führungsqualität).</summary>
    [Range(1, 10)] public int CouncilSize { get; set; } = 4;
    /// <summary>Wöchentliche Wirkung der Ratsstimmung auf Mitläufer: ab 70 bzw. bis 35 Zufriedenheit des Rats.</summary>
    [Range(0, 10)] public int CouncilPull { get; set; } = 2;
    /// <summary>Unter dieser mittleren Zufriedenheit meldet sich der Rat beim Trainer (höchstens monatlich).</summary>
    [Range(0, 100)] public int CouncilConcernBelow { get; set; } = 40;
    /// <summary>Mentoring: wöchentliche Chance auf +1 Professionalität (Basis, je Punkt Professionalität und Führung des Mentors).</summary>
    [Range(0, 1)] public double MentorBase { get; set; } = 0.12;
    [Range(0, 1)] public double MentorPerProfessionalism { get; set; } = 0.015;
    [Range(0, 1)] public double MentorPerLeadership { get; set; } = 0.008;
}
