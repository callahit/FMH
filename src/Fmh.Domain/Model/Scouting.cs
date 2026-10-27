namespace Fmh.Core.Model;

/// <summary>Scout mit Urteilsvermögen und Regionalkenntnis (Kapitel 7).</summary>
public sealed class Scout
{
    public int Id { get; set; }
    public int ClubId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Urteil Fähigkeit 1–20.</summary>
    public int JudgingAbility { get; set; } = 10;
    /// <summary>Urteil Potenzial 1–20.</summary>
    public int JudgingPotential { get; set; } = 10;
    /// <summary>Kenntnis der eigenen Region/des eigenen Landes 1–20.</summary>
    public int RegionKnowledge { get; set; } = 10;
    public string Region { get; set; } = "";
}

public enum ScoutTask { WatchPlayer, SearchRegion, NextOpponent }

public sealed class ScoutAssignment
{
    public int Id { get; set; }
    public int ScoutId { get; set; }
    public ScoutTask Task { get; set; }
    public int? PlayerId { get; set; }
    /// <summary>Suchkriterien bei Regionssuche.</summary>
    public string? LeagueId { get; set; }
    public Position? Position { get; set; }
    public int MaxAge { get; set; } = 40;
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
}

/// <summary>Scoutbericht: Stärke und Potenzial in Sternen, Empfehlung A–D, Fazit.</summary>
public sealed class ScoutReport
{
    public int PlayerId { get; set; }
    public DateOnly Date { get; set; }
    public double StrengthStars { get; set; }
    public double PotentialStars { get; set; }
    public char Recommendation { get; set; } = 'C';
    /// <summary>Fazit aus Spielständen vor der Mehrsprachigkeit.</summary>
    public string Summary { get; set; } = "";
    public Data.TextRef? SummaryRef { get; set; }

    public string SummaryText(Data.Texts t) => SummaryRef is { } r ? t.Render(r) : Summary;
}
