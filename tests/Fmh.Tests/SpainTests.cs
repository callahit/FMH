using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Länderpaket Spanien (Umsetzungskonzept AP 4): Primera und Segunda División (20/22), Primera Federación in zwei und
/// Segunda Federación in fünf Gruppen, Tercera Federación als Pool, Filiales als zweite Mannschaften, Play-offs je Gruppe,
/// Copa Nacional mit Einstieg der besten vier ab der Runde der letzten 32 und Supercopa mit vier Teilnehmern.
/// </summary>
public class SpainTests
{
    private static GameSession NewSpain(ulong seed = 5)
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = seed, ClubKey = data.Clubs.First(c => c.Division == "ES2").Key, ManagerName = "Test",
            Activation = new() { ["DE"] = CountryActivation.Inactive, ["ENG"] = CountryActivation.Inactive },
        });
        return new GameSession(world, data, new TextMatchEngine());
    }

    [Fact]
    public void PyramidWithGroupsAndReserves()
    {
        var w = NewSpain().World;
        var leagues = w.Leagues.Where(l => l.CountryCode == "ESP").Select(l => (l.Id, l.ClubIds.Count)).ToList();
        Assert.Equal(new[] { ("ES1", 20), ("ES2", 22), ("ES3A", 20), ("ES3B", 20), ("ES4A", 18), ("ES4B", 18), ("ES4C", 18), ("ES4D", 18), ("ES4E", 18) }, leagues);
        Assert.Equal(50, w.Clubs.Values.Count(c => c.LeagueId == "ES-TER"));
        // Filiales spielen mindestens eine Ebene unter ihrer ersten Mannschaft.
        var reserves = w.Clubs.Values.Where(c => c.CountryCode == "ESP" && c.ReserveOf is not null).ToList();
        Assert.Equal(15, reserves.Count);
        Assert.All(reserves, r => Assert.True(w.FindLeague(r.LeagueId)!.Level > w.FindLeague(w.Clubs[r.ReserveOf!.Value].LeagueId)!.Level));
        // 42 Spieltage in der Segunda División mit Englischen Wochen am Dienstag (Pokalspiele liegen mittwochs), die Primera kommt ohne aus.
        var dates = w.League("ES2").Fixtures.GroupBy(f => f.Matchday).Select(g => g.Select(f => f.Date).Distinct().Single()).ToList();
        Assert.Equal(42, dates.Distinct().Count());
        Assert.Contains(dates, d => d.DayOfWeek == DayOfWeek.Tuesday);
        Assert.All(w.League("ES1").Fixtures, f => Assert.Equal(DayOfWeek.Saturday, f.Date.DayOfWeek));
        // Tabellen: direkter Vergleich vor der Tordifferenz.
        Assert.Equal(TieBreaker.HeadToHead, w.Country("ESP").Divisions.First().TieBreakers[0]);
    }

    /// <summary>Copa Nacional: 112 Vereine in Runde 1 ohne Filiales, die vier Besten der Primera steigen in Runde 3 ein.</summary>
    [Fact]
    public void CupLetsTheTopFourEnterLater()
    {
        var w = NewSpain().World;
        var cup = w.Cups.Single(c => c.Id == "ES-CUP");
        var first = cup.Rounds[0].Ties.SelectMany(t => new[] { t.HomeId, t.AwayId }).ToList();
        Assert.Equal(112, first.Count);
        Assert.All(first, id => Assert.Null(w.Clubs[id].ReserveOf));
        var top = w.League("ES1").ClubIds.Select(id => w.Clubs[id]).OrderByDescending(c => c.Reputation).ThenBy(c => c.Id).Take(4).Select(c => c.Id);
        Assert.All(top, id => Assert.DoesNotContain(id, first));
    }

    /// <summary>
    /// Eine Saison: Play-off der Segunda (3. gegen 6., 4. gegen 5., auch das Finale mit Hin- und Rückspiel), Play-offs je
    /// Gruppe der Federación; danach haben alle Ligen ihre Teamzahl, die Gruppensieger der Primera Federación sind aufgestiegen
    /// und die Supercopa der neuen Saison hat vier Teilnehmer.
    /// </summary>
    [Fact]
    public void SeasonWithGroupPlayoffsAndSupercup()
    {
        var s = NewSpain(7);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 30_000);
        var table = LeagueTable.Compute(w.League("ES2")).Select(r => r.ClubId).ToList();
        var po = w.Cups.Single(c => c.Id == "ES-PO2");
        Assert.Equal(new[] { (table[5], table[2]), (table[4], table[3]) }.Order(), po.Rounds[0].Ties.Select(t => (t.HomeId, t.AwayId)).Order());
        Assert.Equal(2, Assert.Single(po.Rounds[1].Ties).Legs);
        var champions = new[] { "ES3A", "ES3B" }.Select(g => LeagueTable.Compute(w.League(g))[0].ClubId).ToList();
        var groupPlayoffs = w.Cups.Where(c => c.Id.StartsWith("ES-PO3") || c.Id.StartsWith("ES-PO4")).ToList();
        Assert.Equal(7, groupPlayoffs.Count);
        Assert.All(groupPlayoffs, c => Assert.NotNull(c.WinnerId));
        // Segunda Federación: Einzelspiele beim Besserplatzierten (2. gegen 5., 3. gegen 4.).
        var group = LeagueTable.Compute(w.League("ES4A")).Select(r => r.ClubId).ToList();
        Assert.Equal(new[] { (group[1], group[4]), (group[2], group[3]) }.Order(),
            w.Cups.Single(c => c.Id == "ES-PO4A").Rounds[0].Ties.Select(t => (t.HomeId, t.AwayId)).Order());
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 30_000);
        foreach (var l in w.Leagues.Where(l => l.CountryCode == "ESP")) Assert.Equal(l.Format.Teams, l.ClubIds.Count);
        Assert.Equal(50, w.Clubs.Values.Count(c => c.LeagueId == "ES-TER"));
        Assert.All(champions.Select(id => w.Clubs[id]), c => Assert.True(c.LeagueId == "ES2" || c.ReserveOf is not null || !c.License.PromotionAllowed));
        Assert.All(w.Clubs.Values.Where(c => c.CountryCode == "ESP" && c.ReserveOf is { } p && w.Clubs[p].CountryCode == "ESP"),
            r => Assert.True(w.FindLeague(r.LeagueId) is not { } l || w.FindLeague(w.Clubs[r.ReserveOf!.Value].LeagueId) is not { } pl || l.Level > pl.Level));
        var supercup = w.Cups.Single(c => c.Id == "ES-SC");
        Assert.Equal(4, supercup.Rounds[0].Ties.SelectMany(t => new[] { t.HomeId, t.AwayId }).Distinct().Count());
    }
}
