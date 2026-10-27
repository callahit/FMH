using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Länderpaket England (Umsetzungskonzept AP 4, Welle 1): vier Profiligen (20/24/24/24), National League als Pool,
/// Englische Wochen für 46 Spieltage und Play-offs mit gesetzten Halbfinals in Hin- und Rückspiel und einem Finale.
/// </summary>
public class EnglandTests
{
    private static GameSession NewEngland(ulong seed = 5)
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = seed, ClubKey = data.Clubs.First(c => c.Division == "ENG2").Key, ManagerName = "Test",
            Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        return new GameSession(world, data, new TextMatchEngine());
    }

    [Fact]
    public void AllPackagesPassValidation()
    {
        Assert.Empty(DataValidator.Validate(TestData.AllData));
        Assert.Contains(TestData.AllData.Packages, p => p.Id == "england" && p.DependsOn.Contains("base"));
    }

    /// <summary>46 Spieltage bis Anfang Mai: Wochenenden plus Englische Wochen am Dienstag, kein Verein spielt zweimal am Tag.</summary>
    [Fact]
    public void FourLevelsWithMidweekRounds()
    {
        var w = NewEngland().World;
        Assert.Equal(new[] { ("ENG1", 20), ("ENG2", 24), ("ENG3", 24), ("ENG4", 24) }, w.Leagues.Select(l => (l.Id, l.ClubIds.Count)));
        Assert.Equal(24, w.Clubs.Values.Count(c => c.LeagueId == "ENG-NL"));
        var ch = w.League("ENG2");
        Assert.Equal(46, ch.Fixtures.Max(f => f.Matchday));
        var dates = ch.Fixtures.GroupBy(f => f.Matchday).Select(g => g.Select(f => f.Date).Distinct().Single()).Order().ToList();
        Assert.Equal(46, dates.Distinct().Count());
        Assert.True(dates[^1] <= new DateOnly(2027, 5, 2));
        Assert.Contains(dates, d => d.DayOfWeek == DayOfWeek.Tuesday);
        Assert.All(dates, d => Assert.Contains(d.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Tuesday }));
        // Die Premier Division kommt mit 38 Spieltagen ohne Englische Wochen aus.
        Assert.All(w.League("ENG1").Fixtures, f => Assert.Equal(DayOfWeek.Saturday, f.Date.DayOfWeek));
        Assert.Equal("EUR", w.Country("ENG").Currency);
    }

    /// <summary>
    /// Eine Saison: Play-offs 3. gegen 6. und 4. gegen 5. in Hin- und Rückspiel (Rückspiel beim Besserplatzierten), Finale
    /// in einem Spiel; danach behalten alle Ligen ihre Teamzahl und je zwei Vereine wechseln zwischen Third Division und Pool.
    /// </summary>
    [Fact]
    public void SeasonWithSeededPlayoffsAndNonLeaguePool()
    {
        var s = NewEngland(7);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
        var table = LeagueTable.Compute(w.League("ENG2")).Select(r => r.ClubId).ToList();
        var po = w.Cups.Single(c => c.Id == "ENG-PO2");
        var semis = po.Rounds[0].Ties;
        Assert.Equal(new[] { (table[5], table[2]), (table[4], table[3]) }.Order(), semis.Select(t => (t.HomeId, t.AwayId)).Order());
        Assert.All(semis, t => Assert.Equal(2, t.Legs));
        Assert.Equal(1, Assert.Single(po.Rounds[1].Ties).Legs);
        var winner = po.WinnerId!.Value;
        // Der Sieger des League Cup spielt europäisch (Conference League, sofern nicht schon über Liga oder Pokal dabei).
        var leagueCup = w.Clubs[w.Cups.Single(c => c.Id == "ENG-LC").WinnerId!.Value];
        if (leagueCup.License.UefaLicense && !leagueCup.License.ForcedRelegation)
            Assert.Contains(w.EuropeanEntries, e => e.ClubId == leagueCup.Id && e.SeasonYear == w.SeasonYear + 1);
        var bottom = LeagueTable.Compute(w.League("ENG4")).Skip(22).Select(r => r.ClubId).ToList();
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        foreach (var l in w.Leagues.Where(l => l.CountryCode == "ENG")) Assert.Equal(l.Format.Teams, l.ClubIds.Count);
        Assert.True(w.Clubs[winner].LeagueId == "ENG1" || !w.Clubs[winner].License.PromotionAllowed);
        Assert.All(bottom, id => Assert.Equal("ENG-NL", w.Clubs[id].LeagueId));
        Assert.Equal(24, w.Clubs.Values.Count(c => c.LeagueId == "ENG-NL"));
    }
}
