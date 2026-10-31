using Fmh.Core.Model;

namespace Fmh.Core.Simulation;

/// <summary>Ein neuer Kalendertag hat begonnen (nach dem Datumswechsel). Module können den Ablauf über <see cref="DayContext.Stop"/> anhalten.</summary>
public sealed record DayStarted(DayContext Day) : IGameEvent;

/// <summary>Ein Spiel ist ausgewertet. <paramref name="HumanInvolved"/>: menschlicher Manager; <paramref name="Participants"/>: eingesetzte Spieler.</summary>
public sealed record MatchPlayed(Fixture Fixture, bool HumanInvolved, IReadOnlySet<int>? Participants = null) : IGameEvent;
