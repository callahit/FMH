namespace Fmh.Core.Model;

/// <summary>Spielmodus (AP 12).</summary>
public enum GameMode { Career, QuickSeason, Challenge }

public enum ChallengeOutcome { Open, Won, Lost }

/// <summary>Laufender Spielmodus; fehlt bei einer normalen Karriere.</summary>
public sealed class GameModeState
{
    public GameMode Mode { get; set; }
    /// <summary>Herausforderung (rules/challenges.json); leer bei der Schnellsaison.</summary>
    public string ChallengeId { get; set; } = "";
    public int ClubId { get; set; }
    public int StartSeason { get; set; }
    /// <summary>Ligaebene des Vereins zu Beginn.</summary>
    public int StartLevel { get; set; }
    public ChallengeOutcome Outcome { get; set; }
    /// <summary>Saison, in der das Ergebnis feststand.</summary>
    public int? DecidedSeason { get; set; }
    /// <summary>Grund bei verlorener Herausforderung (Textschlüssel), z. B. Entlassung.</summary>
    public string Reason { get; set; } = "";
}
