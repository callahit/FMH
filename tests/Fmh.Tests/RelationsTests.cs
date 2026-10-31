using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;
using Fmh.Core.Persistence;
using Fmh.Core.Simulation;

namespace Fmh.Tests;

/// <summary>Must-now E3 (Kabine): Zufriedenheit, Gespräche, Versprechen, Unzufriedenheit, Führung, Mannschaftsstimmung.</summary>
public class RelationsTests
{
    private static RelationsModule Rel(Fmh.Core.Game.GameSession s) => s.Modules.OfType<RelationsModule>().Single();

    private static void RelationsDay(Fmh.Core.Game.GameSession s)
    {
        Rel(s).OnDay(new DayContext(s.World.Date, Array.Empty<Fixture>()));
    }

    private static void AdvanceToMonday(Fmh.Core.Game.GameSession s)
    {
        while (s.World.Date.DayOfWeek != DayOfWeek.Monday)
            s.World.Date = s.World.Date.AddDays(1);
    }

    [Fact]
    public void SatisfactionReflectsPlayingTimeWageAndClubGoals()
    {
        var s = TestData.NewSession(101);
        var club = s.HumanClub;
        var p = s.World.SquadOf(club).First(x => x.Contract is not null);
        p.Contract!.Status = SquadStatus.Star;
        s.RecordNegotiatedStatus(p, SquadStatus.Star);
        p.SeasonStats.Minutes = 0;
        s.RecalculateSatisfaction(p);
        var lowScore = s.PlayerRelationsOf(p).Satisfaction;
        Assert.Contains(s.PlayerRelationsOf(p).ReasonKeys, k => k.Contains("playingTime"));
        Assert.True(lowScore < 70);

        var matchdays = Math.Max(8, s.World.LeagueOf(club).CurrentMatchday);
        p.SeasonStats.Minutes = 90 * matchdays;
        var peers = s.World.SquadOf(club).Where(x => x.Id != p.Id && x.Contract is not null).ToList();
        p.Contract.WeeklyWage = Math.Max(p.Contract.WeeklyWage + 5000, (int)(peers.Average(x => x.Contract!.WeeklyWage) * 1.5));
        club.Board.Goal = SeasonGoal.Survival;
        s.RecalculateSatisfaction(p);
        var highScore = s.PlayerRelationsOf(p).Satisfaction;
        Assert.True(highScore > lowScore, $"expected {highScore} > {lowScore}, reasons={string.Join(",", s.PlayerRelationsOf(p).ReasonKeys)}");
        Assert.Contains(s.PlayerRelationsOf(p).ReasonKeys, k => k.StartsWith("sat.reason."));
    }

    [Fact]
    public void TalkRespectsCooldownAndPersonality()
    {
        var s = TestData.NewSession(102);
        var p = s.World.SquadOf(s.HumanClub).First();
        p.Hidden.Temperament = 6;
        p.Hidden.Professionalism = 8;
        var first = s.TalkToPlayer(p, TalkTopic.Criticism);
        Assert.True(first.Allowed);
        Assert.True(first.SatisfactionDelta < 0);
        Assert.Equal("talk.reaction.criticism.angry", first.ReactionKey);

        var blocked = s.TalkToPlayer(p, TalkTopic.Praise);
        Assert.False(blocked.Allowed);
        Assert.Equal("talk.error.cooldown", blocked.ReactionKey);

        s.World.Date = s.World.Date.AddDays(14);
        p.Hidden.Professionalism = 16;
        p.Hidden.Temperament = 12;
        var praise = s.TalkToPlayer(p, TalkTopic.Praise);
        Assert.True(praise.Allowed);
        Assert.True(praise.SatisfactionDelta > 0);
    }

    [Fact]
    public void BrokenPromiseLowersSatisfactionALotAndStatusCreatesExpectation()
    {
        var s = TestData.NewSession(103);
        var p = s.World.SquadOf(s.HumanClub).First();
        p.Contract!.Status = SquadStatus.Star;
        s.RecordNegotiatedStatus(p, SquadStatus.Star);
        Assert.Contains(s.World.Promises, x => x.PlayerId == p.Id && x.Implicit && x.Kind == PromiseKind.SquadRole);

        var before = s.PlayerRelationsOf(p).Satisfaction = 60;
        var talk = s.TalkToPlayer(p, TalkTopic.Promise, PromiseKind.PlayingTime, s.World.Date.AddDays(1));
        Assert.NotNull(talk.Promise);
        p.SeasonStats.Minutes = 0;
        s.World.Date = talk.Promise!.Deadline.AddDays(1);
        RelationsDay(s);

        var promise = s.World.Promises.First(x => x.Id == talk.Promise.Id);
        Assert.Equal(PromiseOutcome.Broken, promise.Outcome);
        Assert.True(s.PlayerRelationsOf(p).Satisfaction <= before - 15);
    }

    [Fact]
    public void UnhappyPlayersRequestTransferAndDemandHigherWages()
    {
        var s = TestData.NewSession(104);
        var p = s.World.SquadOf(s.HumanClub).First();
        p.Hidden.Ambition = 18;
        p.Hidden.Loyalty = 5;
        var rel = s.PlayerRelationsOf(p);
        rel.Satisfaction = 55;
        var happyWage = Negotiations.WageDemand(p, s.HumanClub, p.Contract!.Status, s.World, s.Data);
        rel.Satisfaction = 15;
        var unhappyWage = Negotiations.WageDemand(p, s.HumanClub, p.Contract.Status, s.World, s.Data);
        Assert.True(unhappyWage > happyWage);

        var requested = false;
        for (var i = 0; i < 12 && !requested; i++)
        {
            AdvanceToMonday(s);
            RelationsDay(s);
            requested = rel.WantsTransfer || p.Transfer.Listed
                        || s.World.Messages.Any(m => m.SubjectRef?.Key == "sat.transferRequest.subject");
            s.World.Date = s.World.Date.AddDays(1);
        }
        Assert.True(requested);
    }

    [Fact]
    public void LeadersAndMentoringRaiseYoungProfessionalism()
    {
        var s = TestData.NewSession(105);
        var club = s.HumanClub;
        Rel(s).EnsureLeaders(club);
        var leaders = s.SquadLeaders(club);
        Assert.InRange(leaders.Count, 1, 3);

        var mentee = s.World.SquadOf(club).OrderBy(p => p.Age(s.World.Date)).First();
        mentee.BirthDate = s.World.Date.AddYears(-20);
        mentee.Hidden.Professionalism = 8;
        var mentor = s.World.Players[leaders[0]];
        mentor.Hidden.Professionalism = 18;
        mentor.Attributes[Attr.Leadership] = 18;
        Rel(s).EnsureLeaders(club);
        Assert.Contains(Rel(s).ClubOf(club).Mentoring, g => g.MenteeIds.Contains(mentee.Id));

        var before = mentee.Hidden.Professionalism;
        for (var i = 0; i < 50; i++)
        {
            AdvanceToMonday(s);
            RelationsDay(s);
            s.World.Date = s.World.Date.AddDays(1);
        }
        Assert.True(mentee.Hidden.Professionalism > before);
    }

    [Fact]
    public void SquadMoodAffectsMatchDayFormAndReportLine()
    {
        var s = TestData.NewSession(106);
        var club = s.HumanClub;
        s.ClubRelationsOf(club).SquadMood = 20;
        Assert.Equal("match.mood.apathetic", Rel(s).MoodReportKey(club));

        var fixture = s.World.AllFixtures.First(f => f.Result is null && f.Involves(club.Id));
        var simLow = s.StartMatch(fixture, humanControlled: false);
        var sideLow = fixture.HomeId == club.Id ? Side.Home : Side.Away;
        var avgLow = simLow.Team(sideLow).OnPitch.Average(mp => mp.DayForm);

        s.ClubRelationsOf(club).SquadMood = 90;
        Assert.Equal("match.mood.motivated", Rel(s).MoodReportKey(club));
        var fixture2 = s.World.AllFixtures.Where(f => f.Result is null && f.Involves(club.Id) && f.Id != fixture.Id).First();
        var simHigh = s.StartMatch(fixture2, humanControlled: false);
        var sideHigh = fixture2.HomeId == club.Id ? Side.Home : Side.Away;
        var avgHigh = simHigh.Team(sideHigh).OnPitch.Average(mp => mp.DayForm);
        Assert.True(avgHigh > avgLow);

        s.ClubRelationsOf(club).SquadMood = 20;
        s.PlayInstant(fixture);
        Assert.Contains(fixture.Result!.NoteKeys, k => k is "match.mood.apathetic" or "match.mood.motivated");
    }

    [Fact]
    public void RelationsSectionSurvivesSaveAndLoad()
    {
        var s = TestData.NewSession(107);
        var p = s.World.SquadOf(s.HumanClub).First();
        s.TalkToPlayer(p, TalkTopic.Promise, PromiseKind.Transfer, s.World.Date.AddDays(30));
        s.RecalculateClubSatisfaction(s.HumanClub);
        Rel(s).EnsureLeaders(s.HumanClub);
        s.SyncRng();
        var path = Path.Combine(Path.GetTempPath(), $"fmh-rel-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            Assert.NotEmpty(loaded.PlayerRelations);
            Assert.NotEmpty(loaded.Promises);
            Assert.NotEmpty(loaded.ClubRelations);
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.relations"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
