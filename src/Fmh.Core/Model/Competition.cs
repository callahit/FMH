using Fmh.Core.Data;

namespace Fmh.Core.Model;

/// <summary>
/// K.-o.-Wettbewerb einer Saison: nationaler Pokal oder Relegation (Kapitel 3).
/// Runden werden ausgelost, sobald die vorige Runde entschieden ist.
/// </summary>
public sealed class Cup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public CompetitionKind Kind { get; set; } = CompetitionKind.Cup;
    public int SeasonYear { get; set; }
    public CupConfig Config { get; set; } = new();
    public List<int> ParticipantIds { get; set; } = new();
    public List<CupRound> Rounds { get; set; } = new();
    public List<Fixture> Fixtures { get; set; } = new();
    public int? WinnerId { get; set; }
    /// <summary>Bei der Relegation: Absteiger aus der oberen und Aufsteiger aus der unteren Liga.</summary>
    public string? UpperDivision { get; set; }
    public string? LowerDivision { get; set; }

    public bool IsFinished => WinnerId is not null;
}

public sealed class CupRound
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public List<DateOnly> Dates { get; set; } = new();
    public List<CupTie> Ties { get; set; } = new();
    public bool Drawn => Ties.Count > 0;
    public bool Complete => Drawn && Ties.All(t => t.WinnerId is not null);
}

public sealed class CupTie
{
    public int Id { get; set; }
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public int Legs { get; set; } = 1;
    public List<int> FixtureIds { get; set; } = new();
    public int? WinnerId { get; set; }
    /// <summary>Wie das Duell entschieden wurde: Regular, ExtraTime, Penalties, AwayGoals, Bye.</summary>
    public string Decision { get; set; } = "";
}
