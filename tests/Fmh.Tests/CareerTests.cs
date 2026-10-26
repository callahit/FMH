using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>Phase 2: Pokal, Transfers, Verträge, Training, Scouting, Jugend.</summary>
public class CareerTests
{
    private static Player BuyTarget(GameSession s) =>
        s.World.Players.Values
            .Where(p => p.ClubId is { } c && c != s.HumanClub.Id && s.World.FindLeague(s.World.Clubs[c].LeagueId) is not null)
            .Where(p => !p.IsYouth && p.Loan is null && Negotiations.RefusalReason(p, s.HumanClub, s.World, s.Data) is null)
            .OrderBy(p => s.ValueOf(p))
            .First(p => s.ValueOf(p) > 100_000);

    [Fact]
    public void NewGameHasCupDrawBudgetsAndScouts()
    {
        var s = TestData.NewSession();
        var cup = Assert.Single(s.World.Cups);
        Assert.Equal(64, cup.ParticipantIds.Count);
        Assert.Equal(32, cup.Rounds[0].Ties.Count);
        Assert.Equal(6, cup.Rounds.Count);
        Assert.All(cup.Fixtures, f => Assert.Equal(DayOfWeek.Wednesday, f.Date.DayOfWeek));
        // Unterklassige Vereine haben Heimrecht.
        Assert.All(cup.Rounds[0].Ties, t => Assert.True(CupLogic.Level(s.World, t.HomeId) >= CupLogic.Level(s.World, t.AwayId)));
        Assert.Contains(s.Inbox, m => m.Type == MessageType.Cup);
        Assert.All(s.World.Clubs.Values, c => Assert.True(c.Budget.TransferBudget > 0));
        Assert.Equal(2, s.HumanScouts.Count);
        Assert.True(s.Familiarity(s.HumanClub, s.HumanClub.Tactic.FormationId) >= 70);
    }

    [Fact]
    public void KnockoutMatchesAlwaysProduceAWinner()
    {
        var s = TestData.NewSession(5);
        var engine = new TextMatchEngine();
        var clubs = s.World.Leagues[0].ClubIds;
        var extraTime = 0;
        var shootouts = 0;
        for (var i = 0; i < 200; i++)
        {
            var f = new Fixture { Id = 900_000 + i, CompetitionId = "TEST", Kind = CompetitionKind.Cup, HomeId = clubs[i % 9], AwayId = clubs[9 + i % 9], Date = s.World.Date };
            var setup = s.StartMatch(f, humanControlled: false).Setup;
            var sim = engine.Create(new MatchSetup
            {
                Home = setup.Home, Away = setup.Away, Config = setup.Config, Profiles = setup.Profiles, Roles = setup.Roles,
                InjuryTypes = setup.InjuryTypes, Seed = setup.Seed, Substitutions = setup.Substitutions, Knockout = new KnockoutContext(),
            });
            sim.RunToEnd();
            var h = sim.Home.Goals;
            var a = sim.Away.Goals;
            if (sim.IsExtraTime) extraTime++;
            if (h == a)
            {
                Assert.NotNull(sim.PenaltiesHome);
                Assert.NotEqual(sim.PenaltiesHome, sim.PenaltiesAway);
                shootouts++;
            }
            else Assert.Null(sim.PenaltiesHome);
        }
        Assert.InRange(extraTime, 20, 90);
        Assert.InRange(shootouts, 5, extraTime);
    }

    [Fact]
    public void BuyingAPlayerMovesHimAndBooksTheFee()
    {
        var s = TestData.NewSession(7);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        var balance = s.HumanClub.Budget.Balance;
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        var ask = Negotiations.AskingPrice(p, seller, s.World, s.Data);
        var r = s.SubmitClubOffer(n!, new ClubOffer { Fee = ask });
        Assert.Equal(ResponseKind.Accept, r.Kind);
        Assert.Equal(NegotiationStage.PlayerTalks, n!.Stage);
        r = s.SubmitContract(n, n.Contract);
        Assert.Equal(ResponseKind.Accept, r.Kind);
        Assert.Equal(NegotiationStage.Completed, n.Stage);
        Assert.Equal(s.HumanClub.Id, p.ClubId);
        Assert.Contains(p.Id, s.HumanClub.PlayerIds);
        Assert.DoesNotContain(p.Id, seller.PlayerIds);
        Assert.True(s.HumanClub.Budget.Balance < balance - ask * 0.99);
        Assert.Equal(new DateOnly(s.World.SeasonYear + n.Contract.Years, 6, 30), p.Contract!.Until);
        Assert.Contains(s.World.Transfers, t => t.PlayerId == p.Id && t.Fee == ask);
        Assert.Equal(100, s.Knowledge(p));
    }

    [Fact]
    public void LowOffersAreCounteredAndFailedTalksLockThePlayer()
    {
        var s = TestData.NewSession(8);
        var p = BuyTarget(s);
        var (n, _) = s.StartNegotiation(p, DealType.Transfer);
        for (var i = 0; i < Negotiations.MaxRounds; i++)
        {
            var r = s.SubmitClubOffer(n!, new ClubOffer { Fee = 1_000 });
            if (i < Negotiations.MaxRounds - 1) Assert.Equal(ResponseKind.Counter, r.Kind);
        }
        Assert.Equal(NegotiationStage.Failed, n!.Stage);
        var (again, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(again);
        Assert.Equal("neg.error.locked", error);
    }

    [Fact]
    public void ReleaseClauseForcesTheSale()
    {
        var s = TestData.NewSession(9);
        var p = BuyTarget(s);
        p.Contract!.ReleaseClause = s.ValueOf(p) / 2;
        var (n, _) = s.StartNegotiation(p, DealType.Transfer);
        var r = s.SubmitClubOffer(n!, new ClubOffer { Fee = p.Contract.ReleaseClause.Value });
        Assert.Equal(ResponseKind.Accept, r.Kind);
        Assert.Equal("neg.club.releaseClause", r.TextKey);
    }

    [Fact]
    public void LoanSharesWagesAndReturnsAtSeasonEnd()
    {
        var s = TestData.NewSession(10);
        var p = BuyTarget(s);
        var parent = s.World.Clubs[p.ClubId!.Value];
        p.Transfer.LoanListed = true;
        var (n, error) = s.StartNegotiation(p, DealType.Loan);
        Assert.Null(error);
        var r = s.SubmitClubOffer(n!, new ClubOffer { LoanMonths = 12, WageSharePercent = 60 });
        Assert.Equal(ResponseKind.Accept, r.Kind);
        Assert.Equal(s.HumanClub.Id, p.ClubId);
        Assert.Equal(parent.Id, p.Loan!.ParentClubId);
        var wage = p.Contract!.WeeklyWage;
        var billWith = Budgets.WageBill(s.HumanClub, s.World);
        s.HumanClub.PlayerIds.Remove(p.Id);
        Assert.Equal(wage * 60 / 100, billWith - Budgets.WageBill(s.HumanClub, s.World));
        s.HumanClub.PlayerIds.Add(p.Id);

        var year = s.World.SeasonYear;
        TestData.PlayUntil(s, _ => s.World.SeasonYear > year);
        Assert.Null(p.Loan);
        Assert.NotEqual(s.HumanClub.Id, p.ClubId);
    }

    [Fact]
    public void IncomingOfferBlocksUntilDecided()
    {
        var s = TestData.NewSession(12);
        var year = s.World.SeasonYear;
        AdvanceResult? r = null;
        bool HasOffer() => s.PendingDecisions.Any(m => m.Action == MessageAction.IncomingOffer);
        for (var i = 0; i < 400 && !(r?.Reason == StopReason.Decision && HasOffer()); i++)
        {
            // Andere Pflichtentscheidungen (Saisonziel, Presse, Ereignisse) delegieren.
            if (s.PendingDecisions.Count > 0 && !HasOffer()) s.DelegateDecisions();
            r = s.Advance();
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            Assert.Equal(year, s.World.SeasonYear);
        }
        Assert.Equal(StopReason.Decision, r!.Reason);
        var date = s.World.Date;
        Assert.Equal(StopReason.Decision, s.Advance().Reason);
        Assert.Equal(date, s.World.Date);

        var msg = s.PendingDecisions.First(m => m.Action == MessageAction.IncomingOffer);
        var n = s.World.Negotiations.First(x => x.Id == msg.ActionRefId);
        var player = s.World.Players[n.PlayerId];
        var balance = s.HumanClub.Budget.Balance;
        s.RespondIncoming(n, ResponseKind.Accept);
        Assert.DoesNotContain(s.PendingDecisions, m => m.Action == MessageAction.IncomingOffer);
        if (n.Stage == NegotiationStage.Completed)
        {
            Assert.Equal(n.BuyerId, player.ClubId);
            Assert.True(s.HumanClub.Budget.Balance > balance);
        }
        // Weitere Pflichttermine (z. B. Pressekonferenz) halten nur kurz auf, der Kalender läuft weiter.
        for (var i = 0; i < 10 && s.World.Date == date; i++)
        {
            s.DelegateDecisions();
            var next = s.Advance();
            if (next.Reason == StopReason.HumanMatch && next.Fixture is { } f) s.PlayInstant(f);
        }
        Assert.True(s.World.Date > date);
    }

    [Fact]
    public void ContractRenewalExtendsTheContract()
    {
        var s = TestData.NewSession(13);
        var p = s.World.SquadOf(s.HumanClub).OrderBy(x => x.Contract!.Until).First();
        var (n, error) = s.StartNegotiation(p, DealType.Renewal);
        Assert.Null(error);
        n!.Contract.Years = 3;
        n.Contract.WeeklyWage = Negotiations.WageDemand(p, s.HumanClub, n.Contract.Status, s.World, s.Data) * 2;
        s.HumanClub.Budget.WageBudget += n.Contract.WeeklyWage;
        Assert.Equal(ResponseKind.Accept, s.SubmitContract(n, n.Contract).Kind);
        Assert.Equal(new DateOnly(s.World.SeasonYear + 3, 6, 30), p.Contract!.Until);
    }

    [Fact]
    public void TrainingDevelopsYoungPlayersAndTacticsFamiliarity()
    {
        var s = TestData.NewSession(14);
        var club = s.HumanClub;
        var young = s.World.SquadOf(club).Where(p => p.Age(s.World.Date) <= 21 && p.Hidden.PotentialAbility - p.Hidden.CurrentAbility > 20).ToList();
        Assert.NotEmpty(young);
        var before = young.ToDictionary(p => p.Id, p => p.Hidden.CurrentAbility);
        var other = club.Tactic.FormationId == "3511" ? "5221" : "3511";
        var famBefore = s.Familiarity(club, other);
        club.Tactic.FormationId = other;
        club.Training.Intensity = TrainingIntensity.Hard;
        TestData.PlayUntil(s, _ => s.World.Date >= new DateOnly(s.World.SeasonYear, 12, 15));
        Assert.True(young.Average(p => p.Hidden.CurrentAbility - before[p.Id]) > 1);
        Assert.True(s.Familiarity(club, other) > famBefore + 20);
    }

    [Fact]
    public void ScoutingNarrowsAttributeRangesAndWritesAReport()
    {
        var s = TestData.NewSession(15);
        var p = s.World.Players.Values.First(x => x.ClubId is { } c && s.World.Clubs[c].LeagueId != s.HumanClub.LeagueId && x.Reputation < 15);
        Assert.Equal(10, s.Knowledge(p));
        foreach (var a in AttrInfo.All)
        {
            var (lo, hi) = s.AttributeRange(p, a);
            Assert.InRange(p.Attributes[a], lo, hi);
            Assert.InRange(hi - lo, 0, 8);
        }
        var scout = s.HumanScouts[0];
        Assert.Null(s.AssignScout(scout, ScoutTask.WatchPlayer, p.Id));
        TestData.PlayUntil(s, _ => s.ReportFor(p) is not null, 60);
        Assert.True(s.Knowledge(p) >= 50);
        Assert.Contains(s.Inbox, m => m.Action == MessageAction.ScoutReport && m.ActionRefId == p.Id);
        Assert.Contains(s.ReportFor(p)!.Recommendation, "ABCD");
        Assert.Null(s.AssignmentOf(scout));
    }

    [Fact]
    public void YouthIntakeArrivesInMarchAndCanBePromoted()
    {
        var s = TestData.NewSession(16);
        var count = s.HumanClub.YouthIds.Count;
        TestData.PlayUntil(s, _ => s.World.Date >= new DateOnly(s.World.SeasonYear + 1, 3, 16));
        Assert.True(s.HumanClub.YouthIds.Count > count);
        Assert.Contains(s.Inbox, m => m.Action == MessageAction.YouthIntake);
        var talent = s.YouthOf(s.HumanClub).OrderByDescending(p => p.Age(s.World.Date)).First();
        while (s.HumanClub.PlayerIds.Count >= s.World.Country(s.HumanClub.CountryCode).Squad.MaxSquad)
            s.HumanClub.PlayerIds.RemoveAt(0);
        if (talent.Age(s.World.Date) >= 16)
        {
            Assert.Null(s.PromoteYouth(talent));
            Assert.False(talent.IsYouth);
            Assert.Contains(talent.Id, s.HumanClub.PlayerIds);
            Assert.False(talent.Contract!.Youth);
        }
    }

    [Fact]
    public void CareerDataSurvivesSaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-career-" + Guid.NewGuid());
        try
        {
            var s = TestData.NewSession(17);
            var p = BuyTarget(s);
            p.Transfer.LoanListed = true;
            var (n, _) = s.StartNegotiation(p, DealType.Loan);
            s.SubmitClubOffer(n!, new ClubOffer { LoanMonths = 12, WageSharePercent = 80, RecallInWinter = true });
            s.ToggleShortlist(s.World.Players.Values.First(x => x.ClubId != s.HumanClub.Id));
            s.AssignScout(s.HumanScouts[0], ScoutTask.SearchRegion, leagueId: "DE2", position: Position.ST, maxAge: 25);
            s.HumanClub.Training.Sessions[0] = TrainingBlock.Finishing;
            s.HumanClub.SavedTactics.Add(s.HumanClub.Tactic.Clone());

            var path = Path.Combine(dir, "career.fmh");
            Fmh.Core.Persistence.SqliteStore.Save(s.World, path);
            var w = Fmh.Core.Persistence.SqliteStore.Load(path);
            var lp = w.Players[p.Id];
            Assert.Equal(80, lp.Loan!.WageSharePercent);
            Assert.True(lp.Loan.RecallInWinter);
            Assert.Equal(s.World.Cups[0].Fixtures.Count, w.Cups[0].Fixtures.Count);
            Assert.Equal(s.World.Cups[0].Rounds[0].Ties.Count, w.Cups[0].Rounds[0].Ties.Count);
            var hc = w.Clubs[s.HumanClub.Id];
            Assert.Equal(s.HumanClub.Shortlist, hc.Shortlist);
            Assert.Equal(TrainingBlock.Finishing, hc.Training.Sessions[0]);
            Assert.Single(hc.SavedTactics);
            Assert.Equal(s.HumanClub.Budget.Balance, hc.Budget.Balance);
            Assert.Equal(s.World.Scouts.Count, w.Scouts.Count);
            Assert.Single(w.ScoutAssignments);
            Assert.Equal(s.World.Negotiations.Count, w.Negotiations.Count);
            Assert.Equal(s.World.Transfers.Count, w.Transfers.Count);
            Assert.Equal(s.HumanClub.PlayerIds, hc.PlayerIds);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
