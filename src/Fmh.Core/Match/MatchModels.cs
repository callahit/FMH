using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Match;

public enum Side { Home = 0, Away = 1 }

public static class SideExtensions
{
    public static Side Other(this Side s) => s == Side.Home ? Side.Away : Side.Home;
}

/// <summary>Ton der Kabinenansprache (Kapitel 6).</summary>
public enum Tone { Calm, Demanding, Praising, Angry, Disappointed, Motivating }

/// <summary>Zurufe von der Seitenlinie (Kapitel 6).</summary>
public enum Shout { None, MoreForward, WasteTime, Concentrate, CalmDown }

public sealed class TeamSetup
{
    public required Club Club { get; init; }
    public required Tactic Tactic { get; init; }
    public required Formation Formation { get; init; }
    /// <summary>Startelf, Index = Formations-Slot.</summary>
    public required List<Player> Starting { get; init; }
    public List<Player> Bench { get; init; } = new();
    /// <summary>Wird die Mannschaft von einem Menschen gesteuert? Sonst trifft die KI Wechsel und Taktikänderungen.</summary>
    public bool HumanControlled { get; init; }
}

public sealed class MatchSetup
{
    public required TeamSetup Home { get; init; }
    public required TeamSetup Away { get; init; }
    public required EngineConfig Config { get; init; }
    public required PositionProfiles Profiles { get; init; }
    public IReadOnlyList<InjuryType> InjuryTypes { get; init; } = Array.Empty<InjuryType>();
    public ulong Seed { get; init; }
    public int RefereeStrictness { get; init; } = 10;
    public Weather Weather { get; init; } = Weather.Sunny;
    public double InjuryFrequency { get; init; } = 1.0;
    public int Substitutions { get; init; } = 5;
    public bool NeutralGround { get; init; }

    public TeamSetup Team(Side s) => s == Side.Home ? Home : Away;
}

public enum MatchEventType
{
    KickOff, SecondHalfKickOff, HalfTime, FullTime, Stoppage,
    Attack, Counter, Dribble, Cross, AerialWon,
    Shot, Goal,
    Foul, YellowCard, SecondYellow, RedCard,
    FreeKick, Penalty, Corner, Offside, GoalKick,
    Injury, Substitution, TacticChange,
}

public enum ShotOutcome { Goal, Saved, Wide, Blocked, Post }

public enum ShotKind { Normal, Header, LongShot, FreeKick, Penalty, OneOnOne }

/// <summary>Ein protokollierter Spielmoment mit Zeitstempel; daraus entstehen Kommentar, Statistik und Noten.</summary>
public sealed class MatchEvent
{
    public int Index { get; init; }
    public int Minute { get; init; }
    /// <summary>Minute der Nachspielzeit (45+2 => Minute 45, Stoppage 2), sonst 0.</summary>
    public int Stoppage { get; init; }
    public MatchEventType Type { get; init; }
    public Side Side { get; init; }
    public int? PlayerId { get; init; }
    public int? OtherPlayerId { get; init; }
    /// <summary>Spielfeldzone aus Sicht der angreifenden Mannschaft (Reihe 0 = eigenes Tor, 6 = gegnerischer Strafraum).</summary>
    public int Row { get; init; }
    public int Col { get; init; }
    public double Xg { get; init; }
    public ShotOutcome? Outcome { get; init; }
    public ShotKind? Kind { get; init; }
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public int Value { get; init; }
    /// <summary>Schlüsselszene für den Ticker-Modus.</summary>
    public bool Key { get; init; }

    public string MinuteLabel => Stoppage > 0 ? $"{Minute}+{Stoppage}'" : $"{Minute}'";
}

public sealed class MatchPlayer
{
    public required Player Player { get; init; }
    public int SlotIndex { get; set; }
    public Position Position { get; set; }
    public double Condition { get; set; }
    public Morale Morale { get; set; }
    /// <summary>Tagesform für dieses Spiel (aus Konstanz), in Attributpunkten.</summary>
    public double DayForm { get; set; }

    public int MinuteOn { get; set; }
    public int? MinuteOff { get; set; }
    public bool Started { get; set; }

    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int Passes { get; set; }
    public int PassesCompleted { get; set; }
    public int DuelsWon { get; set; }
    public int DuelsLost { get; set; }
    public int Saves { get; set; }
    public int Fouls { get; set; }
    public bool Yellow { get; set; }
    public bool Red { get; set; }
    public bool Injured { get; set; }
    public string? InjuryType { get; set; }
    public int InjuryDays { get; set; }
    public double DistanceKm { get; set; }

    /// <summary>Interne Leistungspunkte; daraus wird die Note berechnet.</summary>
    public double RatingScore { get; set; }
    /// <summary>Note 1,0–6,0 (Kicker-Stil); null bei zu kurzer Einsatzzeit.</summary>
    public double? Grade { get; set; }

    public int MinutesPlayed(int currentMinute) => Math.Max(0, (MinuteOff ?? currentMinute) - MinuteOn);
}

public sealed class TeamMatchStats
{
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public double Xg { get; set; }
    public double PossessionSeconds { get; set; }
    public int Passes { get; set; }
    public int PassesCompleted { get; set; }
    public int DuelsWon { get; set; }
    public int DuelsLost { get; set; }
    public int Corners { get; set; }
    public int Fouls { get; set; }
    public int Offsides { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int Injuries { get; set; }
    public double DistanceKm { get; set; }
    /// <summary>Aktionen je Zone (Reihe * 5 + Spalte) aus Angriffssicht, für die Text-Heatmap.</summary>
    public int[] Heat { get; } = new int[Grid.Rows * Grid.Cols];

    public int PassAccuracy => Passes == 0 ? 0 : (int)Math.Round(100.0 * PassesCompleted / Passes);
    public int DuelRate => DuelsWon + DuelsLost == 0 ? 50 : (int)Math.Round(100.0 * DuelsWon / (DuelsWon + DuelsLost));
}

public sealed class MatchTeamState
{
    public required Side Side { get; init; }
    public required Club Club { get; init; }
    public required Tactic Tactic { get; init; }
    public required Formation Formation { get; set; }
    public bool HumanControlled { get; set; }
    public List<MatchPlayer> OnPitch { get; } = new();
    public List<MatchPlayer> Bench { get; } = new();
    /// <summary>Ausgewechselte oder vom Platz gestellte Spieler.</summary>
    public List<MatchPlayer> Off { get; } = new();
    public int SubstitutionsUsed { get; set; }
    public int Goals { get; set; }
    public Shout Shout { get; set; }
    public TeamMatchStats Stats { get; } = new();

    public IEnumerable<MatchPlayer> AllPlayers => OnPitch.Concat(Off).Concat(Bench);
    public IEnumerable<MatchPlayer> Participants => OnPitch.Concat(Off);
}

public sealed record TeamTalkReaction(int PlayerId, int MoraleDelta);

/// <summary>Austauschbare Matchengine (Kapitel 12, Modularität).</summary>
public interface IMatchEngine
{
    string Name { get; }
    IMatchSimulation Create(MatchSetup setup);
}

/// <summary>Ein laufendes Spiel. Wird Aktion für Aktion (Phase für Phase) simuliert.</summary>
public interface IMatchSimulation
{
    MatchSetup Setup { get; }
    MatchTeamState Home { get; }
    MatchTeamState Away { get; }
    MatchTeamState Team(Side side);
    IReadOnlyList<MatchEvent> Events { get; }
    int Minute { get; }
    int StoppageMinute { get; }
    bool IsHalfTime { get; }
    bool IsFinished { get; }

    /// <summary>Simuliert die nächste Spielphase und gibt die neu entstandenen Ereignisse zurück.</summary>
    IReadOnlyList<MatchEvent> Step();
    void RunToEnd();

    bool Substitute(Side side, int outPlayerId, int inPlayerId, out string? errorKey);
    void ChangeMentality(Side side, Mentality mentality);
    void ChangeInstructions(Side side, TeamInstructions instructions);
    void ChangeFormation(Side side, Formation formation);
    void SetShout(Side side, Shout shout);
    IReadOnlyList<TeamTalkReaction> TeamTalk(Side side, Tone tone, int? playerId = null);

    int? PlayerOfTheMatchId { get; }
}
