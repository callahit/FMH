using Fmh.Core.Game;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>AP 11a: Bericht des Co-Trainers und Urlaub mit Delegation.</summary>
public sealed class AssistantTests
{
    private static IEnumerable<Player> Eleven(GameSession s) =>
        s.HumanClub.Tactic.Lineup.Where(id => id is not null).Select(id => s.World.Players[id!.Value]);

    [Fact]
    public void ReportReactsToFatigueAndLowFamiliarity()
    {
        var s = TestData.NewSession(1101);
        var club = s.HumanClub;
        club.Training = new TrainingPlan { Intensity = TrainingIntensity.Hard };
        foreach (var p in Eleven(s)) { p.State.Condition = 60; p.State.MatchPractice = 90; }
        club.Familiarity[club.Tactic.FormationId] = 20;

        var report = s.TrainingReport();
        var keys = report.Findings.Select(f => f.Key).ToList();
        Assert.Contains("assistant.lowCondition", keys);
        Assert.Contains("assistant.lowFamiliarity", keys);
        Assert.DoesNotContain("assistant.allGood", keys);
        Assert.True(report.Changes);
        Assert.Equal(TrainingIntensity.Normal, report.Suggested.Intensity);
        Assert.True(report.Suggested.Sessions.Count(b => b == TrainingBlock.Recovery) > club.Training.Sessions.Count(b => b == TrainingBlock.Recovery));
        Assert.True(report.Suggested.Sessions.Count(b => b == TrainingBlock.Tactics) > club.Training.Sessions.Count(b => b == TrainingBlock.Tactics));

        s.ApplyTrainingSuggestion();
        Assert.Equal(report.Suggested.Sessions, club.Training.Sessions);
        Assert.Equal(report.Suggested.Intensity, club.Training.Intensity);
    }

    [Fact]
    public void ReportIsCalmWhenEverythingIsFine()
    {
        var s = TestData.NewSession(1102);
        var club = s.HumanClub;
        foreach (var p in s.World.SquadOf(club)) { p.State.Condition = 100; p.State.MatchPractice = 100; p.State.Injury = null; }
        club.Familiarity[club.Tactic.FormationId] = 100;
        var keys = s.TrainingReport().Findings.Select(f => f.Key).ToList();
        Assert.DoesNotContain("assistant.lowCondition", keys);
        Assert.DoesNotContain("assistant.lowFamiliarity", keys);
        Assert.DoesNotContain("assistant.lowPractice", keys);
        Assert.DoesNotContain("assistant.manyInjured", keys);
    }

    /// <summary>Abnahme AP 11: Urlaub bis Datum – Spiele laufen, Entscheidungen delegiert, eigene Einstellungen bleiben.</summary>
    [Fact]
    public void HolidayPlaysMatchesDelegatesAndRestoresSettings()
    {
        var s = TestData.NewSession(1103);
        var w = s.World;
        var club = s.HumanClub;
        var formation = club.Tactic.FormationId;
        var plan = club.Training.Clone();
        var until = w.AllFixtures.Where(f => f.Involves(club.Id) && f.Kind == CompetitionKind.League).OrderBy(f => f.Date).Skip(2).First().Date.AddDays(1);
        var expected = w.AllMatches.Count(f => f.Involves(club.Id) && f.Date >= w.Date && f.Date < until);

        var report = s.TakeHoliday(until, new HolidayOptions { Lineup = true, Training = true });
        Assert.Null(report.StoppedKey);
        Assert.Equal(until, w.Date);
        Assert.Equal(expected, report.Matches.Count);
        Assert.All(report.Matches, f => Assert.NotNull(f.Result));
        Assert.Equal(report.Matches.Count(f => f.Kind != CompetitionKind.Friendly), report.Won + report.Drawn + report.Lost);
        Assert.Empty(s.PendingDecisions);
        Assert.Equal(formation, club.Tactic.FormationId);
        Assert.Equal(plan.Sessions, club.Training.Sessions);
        Assert.Equal(plan.Intensity, club.Training.Intensity);
    }

    [Fact]
    public void HolidayEndsWhenTheManagerIsOutOfAJob()
    {
        var s = TestData.NewSession(1104);
        s.HumanManager.Career.Stations.Last().To = s.World.Date;
        var start = s.World.Date;
        var report = s.TakeHoliday(start.AddDays(30), new HolidayOptions());
        Assert.Equal("holiday.stopped.sacked", report.StoppedKey);
        Assert.Equal(start, s.World.Date);
    }
}
