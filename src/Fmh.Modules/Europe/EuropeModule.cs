using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Europapokal (AP 5): echte Spiele in UCL/UEL/UECL mit Qualifikation, Ligaphase und K.-o.</summary>
public sealed partial class EuropeModule : GameModule
{
    public override string Id => "europe";
    public override IReadOnlyList<string> DependsOn => new[] { "license", "cups", "matchday" };

    private void ApplyAdvance(Cup cup, CupRound round, EuropeAdvanceResult adv)
    {
        if (adv.PrizeAmount > 0)
            foreach (var id in adv.PrizeClubs.Distinct())
            {
                Finance.Book(World.Clubs[id], TransactionType.European, adv.PrizeAmount,
                    TR("tx.europePrize", TR("europe." + cup.Id)));
                World.Clubs[id].Fans.Mood = Math.Clamp(World.Clubs[id].Fans.Mood + 2, 0, 100);
            }
        if (adv.PrizeClubs.Count > 0)
            EuropeLogic.ApplyStageBonus(World, Data, cup.Id, round.StageId, adv.PrizeClubs);
        foreach (var (clubId, to) in adv.Drops)
            EuropeLogic.ApplyDrop(World, Data, clubId, to, Rng);
        if (adv.ChampionId is { } champ)
        {
            EuropeLogic.ApplyTitleBonus(World, Data, champ);
            var club = World.Clubs[champ];
            if (Rng.Chance(0.5)) club.Reputation = Math.Min(20, club.Reputation + 1);
            World.History.Add(new SeasonRecord
            {
                SeasonYear = World.SeasonYear, LeagueId = cup.Id, LeagueName = cup.Id,
                ChampionId = champ, ChampionName = club.Name, FinalOrder = new List<int> { champ },
            });
            if (champ == HumanClub.Id && !Board.AwaitingNewJob)
            {
                Board.ChangeTrust(HumanClub, 5);
                Notify(MessageType.Cup, "msg.sender.uefa", TR("msg.europeChampion.subject", TR("europe." + cup.Id)),
                    TR("msg.europeChampion.body", club.Name, TR("europe." + cup.Id)));
            }
        }
        if (adv.NextDrawn) NotifyDraw(cup);
    }

    private void NotifyDraw(Cup cup)
    {
        if (Board.AwaitingNewJob) return;
        var own = cup.Fixtures.FirstOrDefault(f => f.Involves(HumanClub.Id) && f.Result is null);
        if (own is null) return;
        var opp = World.Clubs[own.HomeId == HumanClub.Id ? own.AwayId : own.HomeId];
        Notify(MessageType.Cup, "msg.sender.uefa", TR("msg.europeDraw.subject", TR("europe." + cup.Id)),
            TR("msg.europeDraw.body", TR("europe." + cup.Id), opp.Name, TextArg.OfDate(own.Date)));
    }
}
