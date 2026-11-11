using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Spielmodi (AP 12a): Schnellsaison und Herausforderungen.</summary>
public sealed partial class GameSession
{
    public GameModeState? Mode => World.Mode;

    /// <summary>Laufende oder abgeschlossene Herausforderung.</summary>
    public ChallengeRule? ActiveChallenge => World.Mode is { Mode: GameMode.Challenge } m ? Challenges.Find(Data, m.ChallengeId) : null;

    /// <summary>Schnellsaison aktiv (und der Manager noch im Amt)?</summary>
    public bool InQuickSeason => World.Mode is { Mode: GameMode.QuickSeason } && !_board.AwaitingNewJob;

    private GameModeState NewMode(GameMode mode, string challengeId) => new()
    {
        Mode = mode,
        ChallengeId = challengeId,
        ClubId = HumanClub.Id,
        StartSeason = World.SeasonYear,
        StartLevel = Challenges.Level(World, HumanClub.Id),
    };

    /// <summary>Neues Spiel als Schnellsaison: eine Saison ohne Halt bei Entscheidungen und eigenen Spielen.</summary>
    public void StartQuickSeason() => World.Mode = NewMode(GameMode.QuickSeason, "");

    /// <summary>
    /// Neues Spiel als Herausforderung: Der Verein muss der aus den Daten gewählte sein; Kontostand und Transferbudget
    /// werden nach den Faktoren der Herausforderung angepasst. Fehlertext-Schlüssel oder null.
    /// </summary>
    public string? StartChallenge(string id)
    {
        if (Challenges.Find(Data, id) is not { } rule) return "challenge.error.unknown";
        if (Challenges.PickClub(Data, rule)?.Key != HumanClub.Key) return "challenge.error.club";
        var budget = HumanClub.Budget;
        budget.Balance = (long)Math.Round(budget.Balance * rule.BalanceFactor);
        budget.TransferBudget = (long)Math.Round(budget.TransferBudget * rule.TransferBudgetFactor);
        World.Mode = NewMode(GameMode.Challenge, id);
        return null;
    }

    /// <summary>Nach dem Ergebnis einer Herausforderung als normale Karriere weiterspielen.</summary>
    public void ContinueAsCareer()
    {
        if (World.Mode is { Outcome: not ChallengeOutcome.Open } or { Mode: GameMode.QuickSeason }) World.Mode = null;
    }

    /// <summary>Schnellsaison: Entscheidungen delegieren und eigene Spiele sofort berechnen (Aufstellung vom Co-Trainer).</summary>
    private void QuickSeasonStep()
    {
        if (!InQuickSeason) return;
        var rule = Data.Challenges.QuickSeason;
        if (rule.DelegateDecisions && PendingDecisions.Count > 0) DelegateDecisions();
        if (rule.InstantMatches && PendingHumanFixture is { } f)
        {
            _matchday.AiLineupForHuman = true;
            try { PlayInstant(f); }
            finally { _matchday.AiLineupForHuman = false; }
        }
    }
}
