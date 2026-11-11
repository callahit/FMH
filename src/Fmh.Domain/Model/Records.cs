namespace Fmh.Core.Model;

/// <summary>Abschlussbilanz eines Vereins in einer Ligasaison (AP 11b, Historie).</summary>
public sealed class ClubSeason
{
    public int ClubId { get; set; }
    public int SeasonYear { get; set; }
    public string LeagueId { get; set; } = "";
    public int Position { get; set; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int Points { get; set; }
}

/// <summary>Arten von Spielrekorden.</summary>
public static class RecordKinds
{
    /// <summary>Höchster Sieg (größte Tordifferenz, bei Gleichstand mehr eigene Tore).</summary>
    public const string BiggestWin = "biggestWin";
    /// <summary>Höchste Niederlage (nur je Verein).</summary>
    public const string BiggestDefeat = "biggestDefeat";
    /// <summary>Torreichstes Spiel.</summary>
    public const string MostGoals = "mostGoals";
    /// <summary>Höchste Zuschauerzahl.</summary>
    public const string Attendance = "attendance";

    public static readonly string[] All = { BiggestWin, BiggestDefeat, MostGoals, Attendance };
}

/// <summary>
/// Spielrekord (AP 11b): bestes Spiel einer Art je Verein (<c>club:&lt;id&gt;</c>) oder Wettbewerb (<c>comp:&lt;id&gt;</c>).
/// Gezählt werden Pflichtspiele, keine Testspiele.
/// </summary>
public sealed class MatchRecord
{
    public string Scope { get; set; } = "";
    public string Kind { get; set; } = "";
    public int SeasonYear { get; set; }
    public DateOnly Date { get; set; }
    public string CompetitionId { get; set; } = "";
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int Attendance { get; set; }

    /// <summary>Schlüssel im Rekordbuch der Welt: Geltungsbereich und Art.</summary>
    public static string Key(string scope, string kind) => scope + "/" + kind;

    public static string ClubScope(int clubId) => "club:" + clubId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static string CompetitionScope(string competitionId) => "comp:" + competitionId;
}
