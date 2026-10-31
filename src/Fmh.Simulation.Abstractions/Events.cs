using Fmh.Core.Model;

namespace Fmh.Core.Simulation;

/// <summary>Ein neuer Kalendertag hat begonnen (nach dem Datumswechsel). Module können den Ablauf über <see cref="DayContext.Stop"/> anhalten.</summary>
public sealed record DayStarted(DayContext Day) : IGameEvent;

/// <summary>
/// Ein Spiel ist ausgewertet (Tabelle, Statistik, Finanzen und Spielerzustände sind verbucht).
/// <paramref name="HumanInvolved"/>: der menschliche Manager hat gespielt und sucht nicht gerade einen neuen Verein.
/// <paramref name="Participants"/>: Spieler beider Mannschaften, die eingesetzt wurden (Startelf und Einwechslungen).
/// </summary>
public sealed record MatchPlayed(Fixture Fixture, bool HumanInvolved, IReadOnlySet<int>? Participants = null) : IGameEvent;
