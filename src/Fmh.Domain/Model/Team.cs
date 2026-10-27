namespace Fmh.Core.Model;

/// <summary>
/// Mannschaft eines Vereins (Umsetzungskonzept 4.3): gehört genau einer Welt (<see cref="WorldId"/>: "men", "women")
/// und einem Wettbewerbsstrang. Mannschaftsbezogene Daten (Kader, Taktik, Training, Liga) liegen hier,
/// vereinsweite (Finanzen, Gelände, Stadion, Vorstand, Sponsoren, Personal) beim <see cref="Club"/>.
/// </summary>
public sealed class Team
{
    public const string MenWorld = "men";

    /// <summary>Kennung; die erste Mannschaft trägt die Kennung ihres Vereins.</summary>
    public int Id { get; set; }
    public int ClubId { get; set; }
    public string WorldId { get; set; } = MenWorld;
    public string LeagueId { get; set; } = "";
    public List<int> PlayerIds { get; set; } = new();
    public Tactic Tactic { get; set; } = new();
    /// <summary>Bis zu drei gespeicherte Taktikvarianten, im Spiel umschaltbar.</summary>
    public List<Tactic> SavedTactics { get; set; } = new();
    /// <summary>Taktikvertrautheit 0–100 je Formation; steigt durch Taktiktraining und Spiele.</summary>
    public Dictionary<string, double> Familiarity { get; set; } = new();
    public TrainingPlan Training { get; set; } = new();
    public TrainingCamp? Camp { get; set; }
    public List<int> YouthIds { get; set; } = new();
    /// <summary>Beobachtungsliste des Managers.</summary>
    public List<int> Shortlist { get; set; } = new();
}
