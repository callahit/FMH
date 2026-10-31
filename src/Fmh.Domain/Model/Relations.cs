namespace Fmh.Core.Model;

/// <summary>Schwellen und Fristen der Kabine (Must-now E3 / AP 7).</summary>
public static class RelationsRules
{
    public const int TalkCooldownDays = 14;
    public const int UnhappyThreshold = 30;
}

/// <summary>Thema eines Einzelgesprächs (Must-now E3 / AP 7).</summary>
public enum TalkTopic
{
    PlayingTime,
    Performance,
    Contract,
    Praise,
    Criticism,
    Promise,
}

/// <summary>Art eines Versprechens an einen Spieler.</summary>
public enum PromiseKind
{
    /// <summary>Mehr Spielzeit bis zur Frist.</summary>
    PlayingTime,
    /// <summary>Wechsel wird erlaubt bzw. vorangetrieben.</summary>
    Transfer,
    /// <summary>Vertragsverbesserung (Gehalt).</summary>
    Contract,
    /// <summary>Zugesagter Kaderstatus (aus Verhandlung oder Gespräch).</summary>
    SquadRole,
}

public enum PromiseOutcome { Pending, Held, Broken }

/// <summary>Zufriedenheit und Gesprächsstatus eines Spielers (0–100).</summary>
public sealed class PlayerRelations
{
    /// <summary>Zufriedenheit 0–100.</summary>
    public int Satisfaction { get; set; } = 55;
    /// <summary>Sprachschlüssel der aktuellen Gründe (z. B. sat.reason.playingTime.low).</summary>
    public List<string> ReasonKeys { get; set; } = new();
    /// <summary>Letztes Einzelgespräch; null = noch keines.</summary>
    public DateOnly? LastTalk { get; set; }
    /// <summary>Spieler fordert aktiv einen Wechsel.</summary>
    public bool WantsTransfer { get; set; }
    /// <summary>Kaderstatus, den der Spieler aus der Verhandlung erwartet (implizites Versprechen).</summary>
    public SquadStatus? ExpectedStatus { get; set; }
}

/// <summary>Explizites Versprechen mit Frist.</summary>
public sealed class PlayerPromise
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public int ClubId { get; set; }
    public PromiseKind Kind { get; set; }
    public DateOnly MadeOn { get; set; }
    public DateOnly Deadline { get; set; }
    public PromiseOutcome Outcome { get; set; }
    /// <summary>Bei SquadRole: zugesagter Status.</summary>
    public SquadStatus? TargetStatus { get; set; }
    /// <summary>Bei PlayingTime: Mindestanteil Einsätze (0–100) bis zur Frist.</summary>
    public int TargetValue { get; set; }
    /// <summary>Aus Kaderstatus-Verhandlung abgeleitet, nicht aus einem Gespräch.</summary>
    public bool Implicit { get; set; }
}

/// <summary>Führung und Mentoring je Verein.</summary>
public sealed class ClubRelations
{
    public int ClubId { get; set; }
    /// <summary>1–3 Führungsspieler (automatisch gewählt).</summary>
    public List<int> LeaderIds { get; set; } = new();
    /// <summary>Mentor → Mentees (junge Spieler).</summary>
    public List<MentoringGroup> Mentoring { get; set; } = new();
    /// <summary>Durchschnittliche Zufriedenheit des Kaders (Squad Mood).</summary>
    public double SquadMood { get; set; } = 55;
}

public sealed class MentoringGroup
{
    public int MentorId { get; set; }
    public List<int> MenteeIds { get; set; } = new();
}

/// <summary>Ergebnis eines Einzelgesprächs.</summary>
public sealed class TalkResult
{
    public bool Allowed { get; init; }
    public int SatisfactionDelta { get; init; }
    public string ReactionKey { get; init; } = "";
    public PlayerPromise? Promise { get; init; }
}
