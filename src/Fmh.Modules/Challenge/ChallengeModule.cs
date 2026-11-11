using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Spielmodi (AP 12a): wertet Herausforderungen am Saisonende bzw. beim Saisonwechsel aus und beendet die
/// Schnellsaison mit einem Bericht. Danach geht es als normale Karriere weiter.
/// </summary>
public sealed class ChallengeModule : GameModule
{
    public override string Id => "challenge";
    public override IReadOnlyList<string> DependsOn => new[] { "season" };

    private GameModeState? State => World.Mode;

    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        // Nach dem Saisonmodul angemeldet: Auf- und Abstieg sind beim Aufruf schon vollzogen.
        ctx.Bus.Subscribe<DayStarted>(e =>
        {
            if (e.Day.Stop == StopReason.NewSeason) SeasonStarted(World.SeasonYear - 1);
        });
    }

    public override void OnDay(DayContext day)
    {
        if (State is not { Outcome: ChallengeOutcome.Open } state) return;
        if (state.Mode == GameMode.Challenge && (Board.AwaitingNewJob || HumanClub.Id != state.ClubId))
        {
            Decide(state, false, Board.AwaitingNewJob ? "challenge.reason.sacked" : "challenge.reason.leftClub", World.SeasonYear);
            return;
        }
        if (day.Stop != StopReason.SeasonEnd) return;
        if (state.Mode == GameMode.QuickSeason)
        {
            FinishQuickSeason();
            return;
        }
        if (Challenges.Find(Data, state.ChallengeId) is not { } rule || Challenges.IsLeagueGoal(rule)) return;
        if (Challenges.SeasonGoalMet(World, state, rule, World.SeasonYear)) Decide(state, true, "", World.SeasonYear);
        else if (World.SeasonYear >= Challenges.LastSeason(state, rule)) Decide(state, false, "challenge.reason.goal", World.SeasonYear);
    }

    /// <summary>Ligaziele: neue Ligaebene nach Auf- und Abstieg der Saison <paramref name="finished"/>.</summary>
    private void SeasonStarted(int finished)
    {
        if (State is not { Mode: GameMode.Challenge, Outcome: ChallengeOutcome.Open } state) return;
        if (Challenges.Find(Data, state.ChallengeId) is not { } rule || !Challenges.IsLeagueGoal(rule)) return;
        var level = Challenges.Level(World, state.ClubId);
        var last = finished >= Challenges.LastSeason(state, rule);
        if (rule.Goal.Type == "avoidRelegation")
        {
            if (level > state.StartLevel) Decide(state, false, "challenge.reason.relegated", finished);
            else if (last) Decide(state, true, "", finished);
        }
        else if (level < state.StartLevel) Decide(state, true, "", finished);
        else if (last) Decide(state, false, "challenge.reason.goal", finished);
    }

    private void Decide(GameModeState state, bool won, string reason, int season)
    {
        state.Outcome = won ? ChallengeOutcome.Won : ChallengeOutcome.Lost;
        state.DecidedSeason = season;
        state.Reason = reason;
        var name = TR("challenge." + state.ChallengeId + ".name");
        if (won)
            Notify(MessageType.Career, "msg.sender.board", TR("challenge.won.subject", name), TR("challenge.won.body", name));
        else
            Notify(MessageType.Career, "msg.sender.board", TR("challenge.lost.subject", name), TR("challenge.lost.body", name, TR(reason)));
    }

    /// <summary>Schnellsaison beendet: Bericht mit Platz und Punkten, danach normale Karriere.</summary>
    private void FinishQuickSeason()
    {
        var season = World.ClubSeasons.FirstOrDefault(s => s.ClubId == HumanClub.Id && s.SeasonYear == World.SeasonYear);
        Notify(MessageType.Season, "msg.sender.board", TR("quickSeason.done.subject"),
            TR("quickSeason.done.body", HumanClub.Name, season?.Position ?? 0, season?.Points ?? 0, season?.GoalsFor ?? 0, season?.GoalsAgainst ?? 0));
        World.Mode = null;
    }
}
