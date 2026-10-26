using Fmh.Core.Data;

namespace Fmh.Core.Model;

public sealed class League
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public int Level { get; set; } = 1;
    public DivisionConfig Format { get; set; } = new();
    public List<int> ClubIds { get; set; } = new();
    public List<Fixture> Fixtures { get; set; } = new();
    /// <summary>Punktabzüge (Lizenzierung, Insolvenz) je Club-Id.</summary>
    public Dictionary<int, int> Deductions { get; set; } = new();

    public int MatchdayCount => Fixtures.Count == 0 ? 0 : Fixtures.Max(f => f.Matchday);

    public bool IsComplete => Fixtures.Count > 0 && Fixtures.All(f => f.Result is not null);

    /// <summary>Letzter vollständig gespielter Spieltag (0 = noch keiner).</summary>
    public int CurrentMatchday =>
        Fixtures.Where(f => f.Result is not null).Select(f => f.Matchday).DefaultIfEmpty(0).Max();
}

public enum Weather { Sunny, Cloudy, Rain, Snow, Wind }

/// <summary>Art des Wettbewerbs eines Spiels.</summary>
public enum CompetitionKind { League, Cup, Playoff }

public sealed class Fixture
{
    public int Id { get; set; }
    /// <summary>Liga-, Pokal- oder Relegations-Id.</summary>
    public string CompetitionId { get; set; } = "";
    public CompetitionKind Kind { get; set; } = CompetitionKind.League;
    /// <summary>Bei K.-o.-Spielen: Id des Duells und Hin- (1) oder Rückspiel (2).</summary>
    public int? TieId { get; set; }
    public int Leg { get; set; } = 1;
    /// <summary>Spieltag (Liga) bzw. Runde (Pokal).</summary>
    public int Matchday { get; set; }
    public DateOnly Date { get; set; }
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public int RefereeId { get; set; }
    public MatchResult? Result { get; set; }

    public bool Involves(int clubId) => HomeId == clubId || AwayId == clubId;
}

public sealed class MatchResult
{
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    /// <summary>Spiel ging in die Verlängerung (Tore inklusive Verlängerung).</summary>
    public bool ExtraTime { get; set; }
    public int? PenaltiesHome { get; set; }
    public int? PenaltiesAway { get; set; }
    public int HalfTimeHome { get; set; }
    public int HalfTimeAway { get; set; }
    public int Attendance { get; set; }
    public Weather Weather { get; set; }
    public double HomeXg { get; set; }
    public double AwayXg { get; set; }
    public int HomePossession { get; set; } = 50;
    public int HomeShots { get; set; }
    public int AwayShots { get; set; }
    public List<GoalRecord> Goals { get; set; } = new();
    public List<CardRecord> Cards { get; set; } = new();
    public int? PlayerOfTheMatchId { get; set; }
}

public sealed class GoalRecord
{
    public int Minute { get; set; }
    public bool Home { get; set; }
    public int ScorerId { get; set; }
    public int? AssistId { get; set; }
    public bool Penalty { get; set; }
    public bool OwnGoal { get; set; }
}

public sealed class CardRecord
{
    public int Minute { get; set; }
    public bool Home { get; set; }
    public int PlayerId { get; set; }
    public bool Red { get; set; }
}

public sealed class Referee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Strenge 1–20.</summary>
    public int Strictness { get; set; } = 10;
}
