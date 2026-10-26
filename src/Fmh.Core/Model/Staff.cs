namespace Fmh.Core.Model;

/// <summary>Personal (Kapitel 8). Scouts haben eine eigene Klasse (Scouting).</summary>
public enum StaffRole { AssistantManager, FitnessCoach, GoalkeepingCoach, YouthCoach, Physio, Doctor, Psychologist, PressOfficer, Groundsman }

/// <summary>Angestellter mit eigenen Attributen 1–20 und Vertrag.</summary>
public sealed class StaffMember
{
    public int Id { get; set; }
    public int? ClubId { get; set; }
    public string Name { get; set; } = "";
    public StaffRole Role { get; set; }
    public int Age { get; set; } = 40;
    /// <summary>Hauptwert der Rolle (Training, Fitness, Torwart, Jugendförderung, Physiotherapie, Medizin, Psychologie, Medien, Rasenpflege).</summary>
    public int Ability { get; set; } = 10;
    /// <summary>Motivation/Menschenführung, wirkt auf Moral.</summary>
    public int Motivation { get; set; } = 10;
    public int WeeklyWage { get; set; }
    public DateOnly ContractUntil { get; set; }
}
