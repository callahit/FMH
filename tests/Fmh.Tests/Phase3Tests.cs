using Fmh.Core.Game;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>Phase 3: Finanzen, Stadion, Gelände, Personal, Sponsoren, Vorstand, Presse, Ereignisse, Karriere.</summary>
public class Phase3Tests
{
    private static void AdvanceDays(GameSession s, int days)
    {
        var until = s.World.Date.AddDays(days);
        TestData.PlayUntil(s, _ => s.World.Date >= until);
    }

    [Fact]
    public void NewGameHasEconomyForAllLeagueClubs()
    {
        var s = TestData.NewSession();
        foreach (var club in s.World.Leagues.SelectMany(l => l.ClubIds).Select(id => s.World.Clubs[id]))
        {
            Assert.Equal(4, club.Stadium.Stands.Count);
            Assert.True(club.Stadium.Capacity > 1000, club.Name);
            Assert.NotEmpty(club.Board.Members);
            Assert.NotEmpty(club.Rivals);
            Assert.True(club.Budget.WageBudget > 0);
        }
        var human = s.HumanClub;
        Assert.NotEmpty(s.StaffOf(human));
        Assert.NotEmpty(human.Grounds.Buildings);
        Assert.NotEmpty(s.StaffMarket);
        Assert.Single(s.HumanManager.Career.Stations);
        Assert.Contains(s.PendingDecisions, m => m.Action == MessageAction.SeasonGoal);
    }

    [Fact]
    public void MatchdaysBookIncomeAndWeeklyWages()
    {
        var s = TestData.NewSession(3);
        AdvanceDays(s, 60);
        var f = s.HumanClub.Finance;
        Assert.True(f.Season.GetValueOrDefault(TransactionType.Tickets) > 0);
        Assert.True(f.Season.GetValueOrDefault(TransactionType.Catering) > 0);
        Assert.True(f.Season.GetValueOrDefault(TransactionType.Wages) < 0);
        Assert.True(f.Season.GetValueOrDefault(TransactionType.StaffWages) < 0);
        Assert.True(f.Season.GetValueOrDefault(TransactionType.Tv) > 0);
        Assert.NotEmpty(f.History);
        Assert.True(s.HumanClub.Finance.SeasonTickets > 0);
    }

    [Fact]
    public void LoansAndDepositsMoveMoney()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        var before = club.Budget.Balance;
        Assert.Null(s.TakeLoan(1_000_000, 3));
        Assert.Equal(before + 1_000_000, club.Budget.Balance);
        var loan = Assert.Single(club.Finance.Loans);
        Assert.True(loan.MonthlyPayment > 1_000_000 / 36);
        Assert.Equal("fin.error.loanLimit", s.TakeLoan(long.MaxValue / 4, 3));
        Assert.Null(s.RepayLoan(loan));
        Assert.Empty(club.Finance.Loans);

        Assert.Null(s.MakeDeposit(100_000, 12));
        Assert.Equal("fin.error.depositExists", s.MakeDeposit(100_000, 12));
        Assert.NotNull(club.Finance.Deposit);
    }

    [Fact]
    public void TicketPricesAreClampedAndAffectSatisfaction()
    {
        var s = TestData.NewSession();
        var cheap = s.HumanClub.Finance.Prices.Clone();
        cheap.Standing = 1;
        s.SetTicketPrices(cheap);
        var happy = s.HumanClub.Finance.PriceSatisfaction;
        var expensive = cheap.Clone();
        expensive.Standing = 10_000;
        expensive.Seating = 10_000;
        s.SetTicketPrices(expensive);
        Assert.True(s.HumanClub.Finance.Prices.Standing < 10_000);
        Assert.True(s.HumanClub.Finance.PriceSatisfaction < happy);
    }

    [Fact]
    public void StadiumExpansionCompletesAndAddsCapacity()
    {
        var s = TestData.NewSession(5);
        var club = s.HumanClub;
        club.Budget.Balance += 5_000_000;
        var capacity = club.Stadium.Capacity;
        Assert.Null(s.OrderStadiumProject(ProjectKind.Standing, StandSide.North, Contractor.Premium));
        var project = Assert.Single(club.Grounds.Projects);
        Assert.Equal(ProjectState.Building, project.State);
        AdvanceDays(s, 120);
        Assert.Equal(ProjectState.Done, project.State);
        Assert.Equal(capacity + 1000, club.Stadium.Capacity);
    }

    [Fact]
    public void BuildingsAreBuiltUpgradedAndTakeEffect()
    {
        var s = TestData.NewSession(6);
        var club = s.HumanClub;
        club.Budget.Balance += 10_000_000;
        var def = s.Data.Building("physioRoom") ?? s.Data.Buildings.First(b => b.Requires.Count == 0 && !b.Land && b.Effects.Count > 0
                                                                               && club.Grounds.Buildings.All(x => x.Type != b.Id));
        var spot = s.FreeSpot(club, def);
        Assert.NotNull(spot);
        Assert.Null(s.OrderBuilding(def.Id, spot!.Value.x, spot.Value.y, Contractor.Premium));
        Assert.NotNull(s.OrderBuilding(def.Id, spot.Value.x, spot.Value.y, Contractor.Premium));
        AdvanceDays(s, def.BuildDays + 40);
        var building = Assert.Single(club.Grounds.Buildings, b => b.Type == def.Id);
        Assert.False(building.UnderConstruction);
        var key = def.Effects.Keys.First();
        var effect = s.Effect(club, key);
        Assert.True(effect > 0);
        if (def.MaxLevel > 1)
        {
            Assert.Null(s.UpgradeBuilding(building, Contractor.Premium));
            AdvanceDays(s, def.BuildDays + 40);
            Assert.Equal(2, building.Level);
            Assert.True(s.Effect(club, key) > effect);
        }
    }

    [Fact]
    public void StaffCanBeHiredAndFired()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        club.Budget.WageBudget *= 2;
        var own = s.StaffOf(club).First();
        Assert.Null(s.FireStaff(own));
        Assert.Null(own.ClubId);
        var candidate = s.StaffMarket.First(x => x.Role == own.Role);
        Assert.Null(s.HireStaff(candidate));
        Assert.Equal(club.Id, candidate.ClubId);
        Assert.Equal("staff.error.taken", s.HireStaff(candidate));
    }

    [Fact]
    public void EveryEventCanBeTriggeredAndResolved()
    {
        var s = TestData.NewSession(21);
        s.HumanClub.Budget.Balance += 50_000_000;
        var triggered = 0;
        foreach (var def in s.Data.Events)
        {
            foreach (var option in def.Options)
            {
                var pending = s.TriggerRandomEvent(def.Id);
                if (pending is null) break; // kein passender Spieler im Kader
                triggered++;
                if (pending.Chosen is null) Assert.Null(s.ResolveEvent(pending, option.Id));
                Assert.NotNull(pending.Chosen);
            }
        }
        Assert.True(triggered > 150);
        Assert.DoesNotContain(s.PendingDecisions, m => m.Action == MessageAction.Event);
        Assert.All(s.Inbox, m => Assert.DoesNotContain("[", m.SubjectText(TestData.Data.Texts)));
    }

    [Fact]
    public void EventDecisionsBlockUntilAnsweredAndExpire()
    {
        var s = TestData.NewSession(22);
        s.DelegateDecisions();
        var pending = s.TriggerRandomEvent("wantsOut");
        Assert.NotNull(pending);
        Assert.Contains(s.PendingDecisions, m => m.Action == MessageAction.Event && m.ActionRefId == pending!.Id);
        Assert.Equal(StopReason.Decision, s.Advance().Reason);
        Assert.Null(s.ResolveEvent(pending!, "raise"));
        Assert.Equal("event.error.done", s.ResolveEvent(pending!, "refuse"));
        Assert.DoesNotContain(s.PendingDecisions, m => m.Action == MessageAction.Event);
    }

    [Fact]
    public void PressConferenceAnswersChangeMediaAndResolve()
    {
        var s = TestData.NewSession(23);
        s.DelegateDecisions();
        var fixture = s.NextFixture(s.HumanClub)!;
        s.SchedulePress(fixture, after: false);
        var conf = s.World.PendingPress;
        Assert.NotNull(conf);
        Assert.Equal(GameSession.PressQuestionsPerConference, conf!.Questions.Count);
        Assert.Contains(s.PendingDecisions, m => m.Action == MessageAction.PressConference);
        var media = s.HumanManager.Career.Media;
        s.AnswerPress(conf.Questions.Select(_ => PressAnswer.Evasive).ToList());
        Assert.Null(s.World.PendingPress);
        Assert.True(s.HumanManager.Career.Media < media);
        Assert.DoesNotContain(s.PendingDecisions, m => m.Action == MessageAction.PressConference);
    }

    [Fact]
    public void SeasonGoalSetsBudgetAndBoardJudgesTheSeason()
    {
        var s = TestData.NewSession(24);
        var goals = s.GoalChoices(s.HumanClub);
        Assert.True(goals.Count >= 2);
        s.ChooseSeasonGoal(goals[0]);
        Assert.Equal(goals[0], s.HumanClub.Board.Goal);
        Assert.DoesNotContain(s.PendingDecisions, m => m.Action == MessageAction.SeasonGoal);
        Assert.StartsWith("board.request.budget.", s.BoardRequest("budget").Key);
        Assert.Equal("board.request.tooSoon", s.BoardRequest("budget").Key);
    }

    [Fact]
    public void ResigningLeadsToJobOffersWhileTheOldClubPlaysOn()
    {
        var s = TestData.NewSession(25);
        var old = s.HumanClub;
        s.DelegateDecisions();
        s.Resign();
        Assert.True(s.AwaitingNewJob);
        Assert.Null(old.ManagerId);
        var played = old.Id;
        var start = s.World.Date;
        var next = s.NextFixture(old)!;
        for (var i = 0; i < 200 && (s.HumanManager.Career.Offers.Count == 0 || s.World.Date <= next.Date); i++)
        {
            var r = s.Advance();
            Assert.NotEqual(StopReason.HumanMatch, r.Reason);
        }
        Assert.True(s.World.Date > next.Date);
        // Das Spiel des alten Vereins wurde ohne den Menschen ausgetragen.
        Assert.Contains(s.AllFixtures, f => f.Involves(played) && f.Result is not null && f.Date >= start);
        var offer = s.HumanManager.Career.Offers.First();
        Assert.Null(s.AcceptJob(offer));
        Assert.False(s.AwaitingNewJob);
        Assert.Equal(offer.ClubId, s.HumanClub.Id);
        Assert.Equal(s.HumanManager.Id, s.HumanClub.ManagerId);
        Assert.Equal(2, s.HumanManager.Career.Stations.Count);
    }

    [Fact]
    public void InsolventAiClubGetsAPointDeduction()
    {
        var s = TestData.NewSession(26);
        var league = s.World.Leagues.First(l => l.Level == 1);
        var victim = s.World.Clubs[league.ClubIds.First(id => id != s.HumanClub.Id)];
        // Mehr Schulden, als ein Notkredit decken kann.
        victim.Budget.Balance = -10_000_000_000;
        AdvanceDays(s, 35);
        Assert.Equal(9, league.Deductions.GetValueOrDefault(victim.Id));
        Assert.True(victim.Budget.Balance >= -s.CreditLimit(victim));
    }

    /// <summary>KI im Minus: Ist der halbe Kreditrahmen verbraucht, nimmt sie zum Monatsersten einen Kredit (Review F-01).</summary>
    [Fact]
    public void AiClubTakesALoanBeforeInsolvency()
    {
        var s = TestData.NewSession(26);
        var league = s.World.Leagues.First(l => l.Level == 2);
        var club = s.World.Clubs[league.ClubIds.First(id => id != s.HumanClub.Id)];
        club.Budget.Balance = -(long)(s.CreditLimit(club) * 0.8);
        AdvanceDays(s, 40);
        Assert.Single(club.Finance.Loans);
        Assert.False(club.Finance.Insolvent);
        Assert.Equal(0, league.Deductions.GetValueOrDefault(club.Id));
    }

    [Fact]
    public void Phase3StateSurvivesSaveAndLoad()
    {
        var s = TestData.NewSession(27);
        var club = s.HumanClub;
        club.Budget.Balance += 5_000_000;
        s.TakeLoan(500_000, 2);
        s.OrderStadiumProject(ProjectKind.Roof, StandSide.East, Contractor.Cheap);
        s.TriggerRandomEvent("wantsOut");
        AdvanceDays(s, 20);
        s.SyncRng();
        var path = Path.Combine(Path.GetTempPath(), "fmh-p3-" + Guid.NewGuid() + ".db");
        try
        {
            SqliteStore.Save(s.World, path);
            var w = SqliteStore.Load(path);
            var c = w.Clubs[club.Id];
            Assert.Equal(club.Budget.Balance, c.Budget.Balance);
            Assert.Equal(club.Finance.Loans.Count, c.Finance.Loans.Count);
            Assert.Equal(club.Finance.Season, c.Finance.Season);
            Assert.Equal(club.Stadium.Capacity, c.Stadium.Capacity);
            Assert.Equal(club.Grounds.Buildings.Select(b => (b.Type, b.Level)), c.Grounds.Buildings.Select(b => (b.Type, b.Level)));
            Assert.Equal(club.Grounds.Projects.Select(p => p.State), c.Grounds.Projects.Select(p => p.State));
            Assert.Equal(club.Sponsors.Select(x => x.Company), c.Sponsors.Select(x => x.Company));
            Assert.Equal(club.Board.Trust, c.Board.Trust);
            Assert.Equal(club.Fans.Mood, c.Fans.Mood);
            Assert.Equal(club.Rivals, c.Rivals);
            Assert.Equal(s.World.Staff.Count, w.Staff.Count);
            Assert.Equal(s.World.Events.Count, w.Events.Count);
            Assert.Equal(s.World.EventHistory, w.EventHistory);
            Assert.Equal(s.World.NextEventId, w.NextEventId);
            Assert.Equal(s.HumanManager.Career.Stations.Count, w.Managers.First(m => m.Human).Career.Stations.Count);
            Assert.Equal(s.HumanManager.Profile.Difficulty, w.Managers.First(m => m.Human).Profile.Difficulty);

            // Weiterspielen nach dem Laden ergibt dasselbe wie ohne Speichern.
            var b = new GameSession(w, TestData.Data, new TextMatchEngine());
            AdvanceDays(s, 30);
            AdvanceDays(b, 30);
            Assert.Equal(s.HumanClub.Budget.Balance, b.HumanClub.Budget.Balance);
            Assert.Equal(s.World.Messages.Count, b.World.Messages.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
