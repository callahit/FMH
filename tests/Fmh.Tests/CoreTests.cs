using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

public class RngTests
{
    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new Rng(123);
        var b = new Rng(123);
        for (var i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void StateRoundTripContinuesSequence()
    {
        var a = new Rng(7);
        for (var i = 0; i < 10; i++) a.NextDouble();
        var b = Rng.FromState(a.GetState());
        for (var i = 0; i < 100; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void NextStaysInRange()
    {
        var r = new Rng(1);
        for (var i = 0; i < 10000; i++)
        {
            var v = r.Next(3, 7);
            Assert.InRange(v, 3, 7);
            Assert.InRange(r.NextDouble(), 0, 0.9999999999);
        }
    }
}

public class ScheduleTests
{
    [Fact]
    public void DoubleRoundRobinPlaysEveryOpponentHomeAndAway()
    {
        var ids = Enumerable.Range(1, 18).ToList();
        var days = ScheduleGenerator.RoundRobin(ids, 2, new Rng(5));
        Assert.Equal(34, days.Count);
        foreach (var day in days)
        {
            Assert.Equal(9, day.Count);
            Assert.Equal(18, day.SelectMany(p => new[] { p.home, p.away }).Distinct().Count());
        }
        var pairs = days.SelectMany(d => d).ToList();
        foreach (var a in ids)
            foreach (var b in ids.Where(b => b != a))
                Assert.Single(pairs, p => p.home == a && p.away == b);
    }

    [Fact]
    public void HomeAndAwayAreBalanced()
    {
        var ids = Enumerable.Range(1, 18).ToList();
        var days = ScheduleGenerator.RoundRobin(ids, 2, new Rng(9));
        foreach (var id in ids)
        {
            var seq = days.Select(d => d.Any(p => p.home == id)).ToList();
            Assert.Equal(17, seq.Count(h => h));
            var longest = 1;
            var run = 1;
            for (var i = 1; i < seq.Count; i++)
            {
                run = seq[i] == seq[i - 1] ? run + 1 : 1;
                longest = Math.Max(longest, run);
            }
            Assert.True(longest <= 3, $"Team {id} hat {longest} Heim- oder Auswärtsspiele in Folge.");
        }
    }

    [Fact]
    public void OddTeamCountGivesByes()
    {
        var days = ScheduleGenerator.RoundRobin(Enumerable.Range(1, 5).ToList(), 2, new Rng(1));
        Assert.Equal(10, days.Count);
        Assert.All(days, d => Assert.Equal(2, d.Count));
    }

    [Fact]
    public void MatchdaysAreWeeklyAndSkipWinterBreak()
    {
        var cfg = new SeasonConfig { FirstMatchday = "08-15", WinterBreakStart = "12-21", WinterBreakEnd = "01-14", MatchDay = DayOfWeek.Saturday };
        var dates = ScheduleGenerator.MatchdayDates(cfg, 2026, 34);
        Assert.Equal(34, dates.Count);
        Assert.All(dates, d => Assert.Equal(DayOfWeek.Saturday, d.DayOfWeek));
        Assert.True(dates[0] >= new DateOnly(2026, 8, 15));
        Assert.DoesNotContain(dates, d => d >= new DateOnly(2026, 12, 21) && d <= new DateOnly(2027, 1, 14));
        Assert.True(dates[^1] < new DateOnly(2027, 6, 1));
    }
}

public class TableTests
{
    private static League LeagueWith(params (int h, int a, int hg, int ag)[] results)
    {
        var league = new League
        {
            Id = "T",
            ClubIds = new List<int> { 1, 2, 3, 4 },
            Format = new DivisionConfig { Teams = 4, TieBreakers = new List<TieBreaker> { TieBreaker.HeadToHead, TieBreaker.GoalDifference } },
        };
        var id = 1;
        foreach (var (h, a, hg, ag) in results)
            league.Fixtures.Add(new Fixture { Id = id++, Matchday = id, HomeId = h, AwayId = a, Result = new MatchResult { HomeGoals = hg, AwayGoals = ag } });
        return league;
    }

    [Fact]
    public void PointsAndGoalsAreCounted()
    {
        var table = LeagueTable.Compute(LeagueWith((1, 2, 3, 0), (3, 4, 1, 1)));
        var first = table[0];
        Assert.Equal(1, first.ClubId);
        Assert.Equal(3, first.Points);
        Assert.Equal(3, first.GoalDifference);
        Assert.Equal(1, table.First(r => r.ClubId == 3).Points);
        Assert.Equal(new[] { 1, 2, 3, 4 }, table.Select(r => r.Position));
    }

    [Fact]
    public void HeadToHeadBeatsGoalDifferenceWhenConfiguredFirst()
    {
        // 1 und 2 punktgleich; 2 hat die bessere Tordifferenz, aber 1 gewann den direkten Vergleich.
        var league = LeagueWith((1, 2, 1, 0), (3, 1, 0, 1), (1, 4, 0, 1), (2, 3, 6, 0), (2, 4, 2, 0));
        var table = LeagueTable.Compute(league);
        var p1 = table.First(r => r.ClubId == 1);
        var p2 = table.First(r => r.ClubId == 2);
        Assert.Equal(p1.Points, p2.Points);
        Assert.True(p2.GoalDifference > p1.GoalDifference);
        Assert.True(p1.Position < p2.Position);

        league.Format.TieBreakers = new List<TieBreaker> { TieBreaker.GoalDifference };
        table = LeagueTable.Compute(league);
        Assert.True(table.First(r => r.ClubId == 2).Position < table.First(r => r.ClubId == 1).Position);
    }
}

public class RatingTests
{
    [Fact]
    public void StrengthIsBetweenOneAndThirteen()
    {
        Assert.Equal(1.0, Ratings.StrengthFromRating(1));
        Assert.Equal(13.0, Ratings.StrengthFromRating(20));
        var s = TestData.NewSession();
        foreach (var p in s.World.Players.Values)
            Assert.InRange(Ratings.Strength(p, TestData.Data.PositionProfiles), 1.0, 13.0);
    }

    [Fact]
    public void BetterClubsHaveStrongerSquads()
    {
        var s = TestData.NewSession();
        var clubs = s.World.Clubs.Values.OrderByDescending(c => c.Reputation).ToList();
        var top = AiTactics.TeamStrength(clubs[0], s.World, TestData.Data);
        var bottom = AiTactics.TeamStrength(clubs[^1], s.World, TestData.Data);
        Assert.True(top > bottom + 3, $"Top {top:0.0} vs. unten {bottom:0.0}");
    }

    [Fact]
    public void FormFollowsRecentGrades()
    {
        var st = new PlayerState();
        Assert.Equal(5, st.Form);
        for (var i = 0; i < 7; i++) st.AddGrade(1.5);
        Assert.Equal(5, st.RecentGrades.Count);
        Assert.True(st.Form >= 9);
    }
}

public class LineupTests
{
    [Fact]
    public void PickBestFillsAllSlotsWithAvailablePlayersAndAKeeper()
    {
        var s = TestData.NewSession();
        foreach (var club in s.World.Clubs.Values)
        {
            Assert.Empty(LineupSelector.Validate(club, s.World, TestData.Data));
            var formation = TestData.Data.Formation(club.Tactic.FormationId);
            var gkSlot = formation.Slots.FindIndex(x => x.Position == Position.GK);
            Assert.Equal(Position.GK, s.World.Players[club.Tactic.Lineup[gkSlot]!.Value].MainPosition);
            Assert.Contains(club.Tactic.Bench, id => s.World.Players[id].MainPosition == Position.GK);
        }
    }

    [Fact]
    public void RepairReplacesOnlyUnavailablePlayers()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        var before = club.Tactic.Lineup.ToList();
        var injured = s.World.Players[before[5]!.Value];
        injured.State.Injury = new Injury { Type = "strain", DaysRemaining = 5, TotalDays = 5 };
        Assert.Contains("lineup.injured", LineupSelector.Validate(club, s.World, TestData.Data));

        LineupSelector.Repair(club, s.World, TestData.Data, 9);
        Assert.Empty(LineupSelector.Validate(club, s.World, TestData.Data));
        for (var i = 0; i < before.Count; i++)
            if (i != 5) Assert.Equal(before[i], club.Tactic.Lineup[i]);
        Assert.NotEqual(injured.Id, club.Tactic.Lineup[5]);
    }

    [Fact]
    public void AdvisorFlagsUnsuitablePlayer()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        var formation = TestData.Data.Formation(club.Tactic.FormationId);
        var gk = s.World.SquadOf(club).First(p => p.MainPosition == Position.GK && !club.Tactic.Lineup.Contains(p.Id));
        var stSlot = formation.Slots.FindIndex(x => x.Position == Position.ST);
        club.Tactic.Lineup[stSlot] = gk.Id;
        Assert.Contains(TacticAdvisor.Analyse(club, s.World, TestData.Data), a => a.Key == "advice.unsuitable");
    }
}
