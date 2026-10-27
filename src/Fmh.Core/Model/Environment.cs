using Fmh.Core.Data;

namespace Fmh.Core.Model;

/// <summary>Werbeplätze (Kapitel 8): Trikot, Ärmel, Ausrüster, 10 Banden, Stadionname.</summary>
public enum SponsorSlot { Shirt, Sleeve, Kit, Board, StadiumName }

public sealed class SponsorDeal
{
    public int Id { get; set; }
    public SponsorSlot Slot { get; set; }
    /// <summary>Bandenplatz 0–9.</summary>
    public int Index { get; set; }
    public string Company { get; set; } = "";
    public long PerSeason { get; set; }
    /// <summary>Erfolgsbonus bei Erreichen des Saisonziels.</summary>
    public long Bonus { get; set; }
    public int UntilSeason { get; set; }
    /// <summary>Nur für Angebote: verfällt an diesem Tag.</summary>
    public DateOnly? Expires { get; set; }
}

public enum SeasonGoal { Title, Europe, TopThird, MidTable, Survival, Promotion }

public enum BoardRole { President, Treasurer, SportsDirector, Member }

public enum BoardTrait { Thrifty, Ambitious, Patient, Impatient, Traditionalist, YouthFriendly }

public sealed class BoardMember
{
    public string Name { get; set; } = "";
    public BoardRole Role { get; set; }
    public BoardTrait Trait { get; set; }
}

/// <summary>Ultimatum des Vorstands: aus den nächsten Spielen eine Mindestpunktzahl.</summary>
public sealed class Ultimatum
{
    public int Matches { get; set; } = 3;
    public int PointsNeeded { get; set; } = 6;
    public int MatchesPlayed { get; set; }
    public int Points { get; set; }
}

/// <summary>Vorstand (Kapitel 10): Vertrauen 0–100, Saisonziel, Ultimatum, Mitglieder.</summary>
public sealed class BoardState
{
    public double Trust { get; set; } = 60;
    public SeasonGoal Goal { get; set; } = SeasonGoal.MidTable;
    public bool GoalAgreed { get; set; }
    public Ultimatum? Ultimatum { get; set; }
    public List<BoardMember> Members { get; set; } = new();
    /// <summary>Letzte Anfrage je Art (Sperre gegen Dauerbitten).</summary>
    public Dictionary<string, DateOnly> LastRequests { get; set; } = new();
    public int ManagerContractUntil { get; set; }
    public int ManagerWeeklyWage { get; set; }
}

/// <summary>Fans (Kapitel 10): Stimmung 0–100, Mitglieder, Dauerkarten, Fanclubs.</summary>
public sealed class FanState
{
    public double Mood { get; set; } = 60;
    public int Members { get; set; }
    public int FanClubs { get; set; }
    /// <summary>Publikumslieblinge (Spieler-Ids), deren Verkauf die Fans verärgert.</summary>
    public List<int> Favourites { get; set; } = new();
}

/// <summary>Frage einer Pressekonferenz mit Kategorie; die Antwortoptionen sind die vier Töne.</summary>
public sealed class PressQuestion
{
    public string Key { get; set; } = "";
    public string Category { get; set; } = "";
    public List<TextArg> Args { get; set; } = new();
}

public enum PressAnswer { Evasive, Confident, Aggressive, Modest }

/// <summary>Anstehende Pressekonferenz vor bzw. nach einem Spiel.</summary>
public sealed class PressConference
{
    public int FixtureId { get; set; }
    public bool AfterMatch { get; set; }
    public List<PressQuestion> Questions { get; set; } = new();
    public List<PressAnswer> Answers { get; set; } = new();
}

/// <summary>Ein Zufallsereignis mit Entscheidung, das im Posteingang wartet.</summary>
public sealed class PendingEvent
{
    public int Id { get; set; }
    public string EventId { get; set; } = "";
    public int ClubId { get; set; }
    public int? PlayerId { get; set; }
    public List<TextArg> Args { get; set; } = new();
    public DateOnly Date { get; set; }
    public DateOnly Expires { get; set; }
    public string? Chosen { get; set; }
}

public enum CoachingLicense { None, C, B, A, Pro }

/// <summary>Eine Station der Managerkarriere.</summary>
public sealed class CareerStation
{
    public int ClubId { get; set; }
    public string ClubName { get; set; } = "";
    public DateOnly From { get; set; }
    public DateOnly? To { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public List<string> Titles { get; set; } = new();
    /// <summary>Wie die Station endete: "" laufend, "sacked", "resigned", "moved".</summary>
    public string Exit { get; set; } = "";
}

public sealed class JobOffer
{
    public int ClubId { get; set; }
    public DateOnly Expires { get; set; }
    public int WeeklyWage { get; set; }
    public int Years { get; set; } = 2;
}

/// <summary>Managerkarriere (Kapitel 10): Lizenz, Stationen, Erfolge, Medienruf, Jobangebote.</summary>
public sealed class ManagerCareer
{
    public CoachingLicense License { get; set; } = CoachingLicense.B;
    /// <summary>Lehrgang läuft bis (Sommerpause).</summary>
    public DateOnly? CourseUntil { get; set; }
    public List<CareerStation> Stations { get; set; } = new();
    public List<string> Achievements { get; set; } = new();
    public List<JobOffer> Offers { get; set; } = new();
    /// <summary>Medienruf 0–100.</summary>
    public double Media { get; set; } = 50;
    /// <summary>Gegnerische Trainer, mit denen eine Fehde läuft (Club-Ids).</summary>
    public List<int> Feuds { get; set; } = new();
}
