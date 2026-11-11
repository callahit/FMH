using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>AP 11c: Datenzentrale – Statistiken je Liga und Saison, Gegneranalyse.</summary>
public sealed class DataCenterTests
{
    [Fact]
    public void StatisticsFollowTheLeagueOfTheClubInThatSeason()
    {
        var w = new World { SeasonYear = 2027 };
        var league = new League { Id = "liga" };
        league.ClubIds.AddRange(new[] { 1, 2 });
        w.Leagues.Add(league);
        // 2026 spielte Verein 3 in der Liga, Verein 1 noch nicht.
        w.ClubSeasons.Add(new ClubSeason { ClubId = 3, SeasonYear = 2026, LeagueId = "liga", Position = 1, Played = 2, Won = 2, GoalsFor = 5, Points = 6 });
        w.ClubSeasons.Add(new ClubSeason { ClubId = 1, SeasonYear = 2026, LeagueId = "unten", Position = 1, Points = 6 });
        var p = new Player { Id = 9 };
        p.History.Add(new PlayerSeasonStats { SeasonYear = 2026, ClubId = 1, Appearances = 10, Goals = 4 });
        p.SeasonStats = new PlayerSeasonStats { SeasonYear = 2027, ClubId = 1, Appearances = 3, Goals = 1 };
        var q = new Player { Id = 10 };
        q.History.Add(new PlayerSeasonStats { SeasonYear = 2026, ClubId = 3, Appearances = 2, Goals = 2 });
        w.Players[p.Id] = p;
        w.Players[q.Id] = q;

        Assert.Equal(new[] { 2027, 2026 }, DataCenter.SeasonYears(w));
        Assert.Equal(new[] { 10 }, DataCenter.PlayerStats(w, "liga", 2026).Select(r => r.Player.Id));
        Assert.Equal(1, DataCenter.PlayerStats(w, "liga", 2027).Single().Stats.Goals);
        Assert.Equal(new[] { 4 }, DataCenter.PlayerStats(w, "unten", 2026).Select(r => r.Stats.Goals));
        var team = DataCenter.TeamStats(w, "liga", 2026).Single();
        Assert.Equal((3, 6, 5), (team.ClubId, team.Points, team.GoalsFor));
        Assert.Equal(2, DataCenter.TeamStats(w, "liga", 2027).Count);
    }

    [Fact]
    public void OpponentAnalysisCoversTheLastFiveCompetitiveMatches()
    {
        var s = TestData.NewSession(1102);
        for (var i = 0; i < 400 && s.World.AllFixtures.Count(f => f.Result is not null && f.Involves(s.HumanClub.Id)) < 7; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
        var w = s.World;
        var opponentId = DataCenter.NextOpponent(w, s.HumanClub.Id);
        Assert.NotNull(opponentId);
        var next = w.AllFixtures.Where(f => f.Result is null && f.Involves(s.HumanClub.Id)).OrderBy(f => f.Date).First();
        Assert.True(next.Involves(opponentId!.Value));

        var club = s.HumanClub;
        var report = DataCenter.Analyse(w, club);
        Assert.Equal(5, report.LastMatches.Count);
        Assert.True(report.LastMatches.Zip(report.LastMatches.Skip(1)).All(t => t.First.Date >= t.Second.Date));
        Assert.Equal(5, report.Won + report.Drawn + report.Lost);
        Assert.Equal(report.LastMatches.Sum(f => f.HomeId == club.Id ? f.Result!.HomeGoals : f.Result!.AwayGoals), report.GoalsFor);
        Assert.DoesNotContain(report.LastMatches, f => f.Kind == CompetitionKind.Friendly);
        Assert.Equal(11, report.Regulars.Count);
        Assert.All(report.Scorers, p => Assert.True(p.SeasonStats.Goals > 0));
        Assert.Equal(club.Tactic.FormationId, report.FormationId);
    }
}
