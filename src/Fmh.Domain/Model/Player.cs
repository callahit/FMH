namespace Fmh.Core.Model;

/// <summary>Moral in sieben Stufen von "Sehr schlecht" bis "Super" (Kapitel 4).</summary>
public enum Morale { VeryBad, Bad, Poor, Okay, Good, VeryGood, Superb }

public enum PreferredFoot { Left, Right, Both }

public sealed class Player
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public List<string> Nationalities { get; set; } = new();
    public int Height { get; set; }
    public int Weight { get; set; }
    /// <summary>Stärke des linken bzw. rechten Fußes, je 1–20.</summary>
    public int LeftFoot { get; set; } = 8;
    public int RightFoot { get; set; } = 16;

    public Position MainPosition { get; set; }
    /// <summary>Eignung je Position. Fehlende Positionen gelten als ungeeignet.</summary>
    public Dictionary<Position, Suitability> Positions { get; set; } = new();

    public int? ClubId { get; set; }
    public Contract? Contract { get; set; }
    /// <summary>Reputation 1–20.</summary>
    public int Reputation { get; set; } = 5;

    public PlayerAttributes Attributes { get; set; } = new();
    public HiddenAttributes Hidden { get; set; } = new();
    public PlayerState State { get; set; } = new();
    public PlayerSeasonStats SeasonStats { get; set; } = new();
    public List<PlayerSeasonStats> History { get; set; } = new();
    public TransferStatus Transfer { get; set; } = new();
    public Loan? Loan { get; set; }
    public PlayerTraining Training { get; set; } = new();
    /// <summary>Jugendspieler (U19/U17) des Vereins, noch nicht im Profikader.</summary>
    public bool IsYouth { get; set; }
    public bool Retired { get; set; }
    /// <summary>Vorvertrag: Wechsel ablösefrei zum 1. Juli (Kapitel 7).</summary>
    public PreContract? PreContract { get; set; }

    public string Name => $"{FirstName} {LastName}";
    public string ShortName => LastName;

    public int Age(DateOnly today)
    {
        var age = today.Year - BirthDate.Year;
        if (today < BirthDate.AddYears(age)) age--;
        return age;
    }

    public Suitability SuitabilityFor(Position p) =>
        Positions.TryGetValue(p, out var s) ? s : Suitability.Unsuitable;

    public PreferredFoot Foot =>
        Math.Abs(LeftFoot - RightFoot) <= 3 ? PreferredFoot.Both : LeftFoot > RightFoot ? PreferredFoot.Left : PreferredFoot.Right;

    public bool IsAvailable => State.Injury is null && State.SuspendedMatches == 0;
}

public sealed class PreContract
{
    public int ClubId { get; set; }
    public Contract Contract { get; set; } = new();
}

public sealed class Contract
{
    /// <summary>Grundgehalt intern pro Woche gespeichert (Kapitel 7); Anzeige je Managerprofil.</summary>
    public int WeeklyWage { get; set; }
    public DateOnly Until { get; set; }
    public DateOnly Start { get; set; }
    public long SigningFee { get; set; }
    public int AppearanceBonus { get; set; }
    public int GoalBonus { get; set; }
    public long? ReleaseClause { get; set; }
    public int YearlyRaisePercent { get; set; }
    /// <summary>Abstiegsklausel älterer Spielstände: Gehalt −30 % nach Abstieg (neu: <see cref="RelegationPercent"/>).</summary>
    public bool RelegationClause { get; set; }
    /// <summary>
    /// Gehaltsänderung bei Abstieg (−%) bzw. Aufstieg (+%) des Vereins, je Spielklasse. null = Vertrag aus älterem
    /// Spielstand: Gehalt folgt dem Gehaltsniveau der neuen Spielklasse.
    /// </summary>
    public int? RelegationPercent { get; set; }
    public int? PromotionPercent { get; set; }
    public int ExtensionOptionYears { get; set; }
    public SquadStatus Status { get; set; } = SquadStatus.Rotation;
    /// <summary>Jugendvertrag (vor dem ersten Profivertrag).</summary>
    public bool Youth { get; set; }
}

public sealed class PlayerState
{
    /// <summary>Kondition 0–100 %.</summary>
    public double Condition { get; set; } = 100;
    /// <summary>Spielpraxis 0–100 %.</summary>
    public double MatchPractice { get; set; } = 70;
    public Morale Morale { get; set; } = Morale.Okay;
    /// <summary>Noten der letzten Spiele (1,0–6,0), neueste zuletzt, höchstens 5.</summary>
    public List<double> RecentGrades { get; set; } = new();
    public Injury? Injury { get; set; }
    public int SuspendedMatches { get; set; }
    public int YellowCardsAccumulated { get; set; }

    /// <summary>Form 1–10 aus den letzten 5 Noten; ohne Noten neutral 5.</summary>
    public int Form
    {
        get
        {
            if (RecentGrades.Count == 0) return 5;
            var avg = RecentGrades.Average();
            // Note 1,0 => 10, Note 3,5 => 5, Note 6,0 => 1
            return (int)Math.Clamp(Math.Round(10 - (avg - 1.0) * 1.8), 1, 10);
        }
    }

    public void AddGrade(double grade)
    {
        RecentGrades.Add(grade);
        while (RecentGrades.Count > 5) RecentGrades.RemoveAt(0);
    }
}

public sealed class Injury
{
    /// <summary>Schlüssel der Verletzungsart (rules/injuries.json).</summary>
    public string Type { get; set; } = "";
    public int DaysRemaining { get; set; }
    public int TotalDays { get; set; }
}

public sealed class PlayerSeasonStats
{
    public int SeasonYear { get; set; }
    public int? ClubId { get; set; }
    public int Appearances { get; set; }
    public int Minutes { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int GradedMatches { get; set; }
    public double GradeSum { get; set; }

    public double? AverageGrade => GradedMatches == 0 ? null : GradeSum / GradedMatches;
}
