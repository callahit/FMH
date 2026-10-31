using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

public sealed partial class EuropeModule
{
    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e =>
        {
            if (e.Fixture.Kind != CompetitionKind.Europe) return;
            if (World.FindCup(e.Fixture.CompetitionId) is not { } cup) return;
            EuropeLogic.ApplyMatchCoefficients(World, Data, e.Fixture);
            if (e.Fixture.TieId is { } tieId)
                OnKnockoutPlayed(cup, e.Fixture, tieId);
            else
                OnLeaguePlayed(cup, e.Fixture);
        });
    }

    private void OnKnockoutPlayed(Cup cup, Fixture f, int tieId)
    {
        if (!CupLogic.ResolveTie(cup, f)) return;
        var round = cup.Rounds.First(r => r.Ties.Any(t => t.Id == tieId));
        var tie = round.Ties.First(t => t.Id == tieId);
        if (tie.HomeId == HumanClub.Id || tie.AwayId == HumanClub.Id)
        {
            var won = tie.WinnerId == HumanClub.Id;
            Notify(MessageType.Cup, "msg.sender.uefa",
                TR(won ? "msg.europe.won.subject" : "msg.europe.out.subject", TR("europe." + cup.Id), Round(round)),
                TR(won ? "msg.europe.won.body" : "msg.europe.out.body", TR("europe." + cup.Id), Round(round), CupLogic.TieScore(cup, tie)));
        }
        var adv = EuropeLogic.AdvanceAfterKnockout(World, Data, cup, round, tie, Rng);
        ApplyAdvance(cup, round, adv);
    }

    private void OnLeaguePlayed(Cup cup, Fixture f)
    {
        var round = cup.Rounds.FirstOrDefault(r => r.StageType == "league" && f.Matchday / 100 == r.Number);
        if (round is null || !EuropeLogic.LeagueRoundComplete(cup, round)) return;
        var adv = EuropeLogic.AdvanceAfterLeague(World, Data, cup, round, Rng);
        ApplyAdvance(cup, round, adv);
        if (HumanClub.Id is var hid && round.Entrants.Contains(hid) && !Board.AwaitingNewJob)
        {
            var table = EuropeLogic.LeagueTableOf(cup, round);
            var row = table.FirstOrDefault(r => r.ClubId == hid);
            if (row is not null)
                Notify(MessageType.Cup, "msg.sender.uefa", TR("msg.europeLeague.subject", TR("europe." + cup.Id)),
                    TR("msg.europeLeague.body", TR("europe." + cup.Id), row.Position, row.Points));
        }
    }
}
