using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Mehrere Pokale je Land: Verbandspokale als Qualifikation für den Pokal, Supercup, späterer Einstieg höherer Ligen
/// (National Cup, League Cup), Hin- und Rückspiel in einzelnen Runden und Europaplätze für mehrere Pokalsieger.
/// </summary>
public class CupsTests
{
    /// <summary>Rundenplan des National Cup: 72 Starter, 44 Neueinsteiger in Runde 3, danach 64 Plätze bis zum Finale.</summary>
    [Fact]
    public void RoundPlanMakesRoomForLateEntrants()
    {
        var entrants = new Dictionary<int, List<int>> { [1] = Enumerable.Range(0, 72).ToList(), [3] = Enumerable.Range(100, 44).ToList() };
        Assert.Equal(new[] { 80, 40, 64, 32, 16, 8, 4, 2 }, CupEntries.PlanRounds(72, entrants, 0));
        // Ohne späteren Einstieg wie bisher eine Zweierpotenz.
        Assert.Equal(new[] { 64, 32, 16, 8, 4, 2 }, CupEntries.PlanRounds(60, new Dictionary<int, List<int>> { [1] = new() }, 64));
    }

    [Fact]
    public void GermanyHasSupercupAndRegionalCups()
    {
        var w = TestData.NewSession().World;
        var super = w.Cups.Single(c => c.Id == "DE-SC");
        Assert.Equal(2, super.ParticipantIds.Count);
        Assert.All(super.ParticipantIds, id => Assert.Equal("DE1", w.Clubs[id].LeagueId));
        Assert.Single(super.Rounds);
        foreach (var region in new[] { ("N", "Nord"), ("NO", "Nordost"), ("W", "West"), ("SW", "Südwest"), ("S", "Süd") })
        {
            var cup = w.Cups.Single(c => c.Id == "DE-LP-" + region.Item1);
            Assert.InRange(cup.ParticipantIds.Count, 16, 32);
            Assert.All(cup.ParticipantIds.Select(id => w.Clubs[id]), c =>
            {
                Assert.Equal(region.Item2, c.Region);
                Assert.Null(c.ReserveOf);
                Assert.True(CupLogic.Level(w, c.Id) >= 3);
            });
        }
        Assert.Empty(DataValidator.Validate(TestData.Data));
    }

    /// <summary>
    /// Nach einer Saison: Pokal mit den 36 Vereinen der Ebenen 1 und 2 der Vorsaison, den ersten vier der 3. Liga und 24
    /// Plätzen über die Verbandspokale (Nord 5, Nordost 6, West 4, Südwest 7, Süd 2); Supercup zwischen Pokalsieger und Meister.
    /// </summary>
    [Fact]
    public void NextSeasonQualifiesThroughLeagueAndRegionalCups()
    {
        var s = TestData.NewSession(21);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
        var top = w.Leagues.Where(l => l.Level <= 2).SelectMany(l => l.ClubIds).ToList();
        var third = LeagueTable.Compute(w.League("DE3")).Take(4).Select(r => r.ClubId).ToList();
        var table = LeagueTable.Compute(w.League("DE1"));
        var (champion, runnerUp) = (table[0].ClubId, table[1].ClubId);
        var cupWinner = w.Cups.Single(c => c.Id == "DE-CUP").WinnerId!.Value;
        var regional = w.Cups.Where(c => c.Id.StartsWith("DE-LP-")).ToDictionary(c => c.Id, c => CupEntries.Ranking(w, c));
        Assert.All(regional.Values, r => Assert.NotEmpty(r));
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);

        var cup = w.Cups.Single(c => c.Id == "DE-CUP");
        Assert.Equal(64, cup.ParticipantIds.Count);
        Assert.All(top.Concat(third), id => Assert.Contains(id, cup.ParticipantIds));
        var quota = new Dictionary<string, int> { ["DE-LP-N"] = 5, ["DE-LP-NO"] = 6, ["DE-LP-W"] = 4, ["DE-LP-SW"] = 7, ["DE-LP-S"] = 2 };
        var taken = top.Concat(third).ToHashSet();
        foreach (var (id, n) in quota)
        {
            var expected = regional[id].Where(c => !taken.Contains(c)).Take(n).ToList();
            Assert.Equal(n, expected.Count);
            Assert.All(expected, c => Assert.Contains(c, cup.ParticipantIds));
            taken.UnionWith(expected);
        }
        // Supercup: Pokalsieger gegen Meister (Doublesieger: gegen den Zweiten), Heimrecht beim Meister bzw. Zweiten.
        var super = w.Cups.Single(c => c.Id == "DE-SC");
        var opponent = cupWinner == champion ? runnerUp : champion;
        Assert.Equal(new[] { cupWinner, opponent }.Order(), super.ParticipantIds.Order());
        Assert.Equal(opponent, super.Rounds[0].Ties[0].HomeId);
    }

    /// <summary>England: Supercup, National Cup mit Einstieg der beiden oberen Ligen in Runde 3, League Cup mit Erstligisten
    /// ab Runde 2 bzw. 3 (Europapokalteilnehmer) und Halbfinale in Hin- und Rückspiel, League Trophy der unteren Profiligen.</summary>
    [Fact]
    public void EnglandHasAllCups()
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = 3, ClubKey = data.Clubs.First(c => c.Division == "ENG2").Key, ManagerName = "Test",
            Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        var s = new GameSession(world, data, new TextMatchEngine());
        var w = s.World;
        string Div(int id) => w.FindLeague(w.Clubs[id].LeagueId)?.Format.Id ?? w.Clubs[id].LeagueId;
        Assert.Equal(new[] { "ENG-CUP", "ENG-LC", "ENG-SC", "ENG-TR" }, w.Cups.Where(c => c.CountryCode == "ENG").Select(c => c.Id).Order());

        var national = w.Cups.Single(c => c.Id == "ENG-CUP");
        Assert.All(national.Rounds[0].Entrants, id => Assert.Contains(Div(id), new[] { "ENG3", "ENG4", "ENG-NL" }));
        Assert.Equal(72, national.Rounds[0].Entrants.Count);
        Assert.Equal(44, national.Rounds[2].Entrants.Count);
        Assert.Equal(64, national.Rounds[2].Teams);
        Assert.Equal("cup.round.final", national.Rounds[^1].NameKey);

        var league = w.Cups.Single(c => c.Id == "ENG-LC");
        Assert.Equal(72, league.Rounds[0].Entrants.Count);
        Assert.Equal(20, league.Rounds[1].Entrants.Count + league.Rounds[2].Entrants.Count);
        Assert.All(league.Rounds[2].Entrants, id => Assert.Contains(w.EuropeanEntries, e => e.ClubId == id && e.SeasonYear == w.SeasonYear));
        Assert.Equal("cup.round.semi", league.Rounds[^2].NameKey);

        var trophy = w.Cups.Single(c => c.Id == "ENG-TR");
        Assert.Equal(48, trophy.ParticipantIds.Count);
        Assert.Equal(2, w.Cups.Single(c => c.Id == "ENG-SC").ParticipantIds.Count);
    }

    /// <summary>Gewinnt der eigene Verein mehrere Pokale, zählt jeder als Titel der Karrierestation, der Erfolg nur einmal.</summary>
    [Fact]
    public void EveryCupWonCountsAsCareerTitle()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        foreach (var cup in s.World.Cups.Where(c => c.Id is "DE-CUP" or "DE-SC")) cup.WinnerId = club.Id;
        s.CareerSeasonEnd(club, 5, true, false, false);
        var titles = s.HumanManager.Career.Stations[^1].Titles;
        Assert.Contains($"DE-CUP|{s.World.SeasonYear}", titles);
        Assert.Contains($"DE-SC|{s.World.SeasonYear}", titles);
        Assert.Single(s.HumanManager.Career.Achievements, a => a == "cupWin");
    }

    /// <summary>Halbfinale im League Cup mit Hin- und Rückspiel, alle übrigen Runden in einem Spiel.</summary>
    [Fact]
    public void RoundLegsOverrideTheDefault()
    {
        var cfg = TestData.AllData.Countries.Single(c => c.Code == "ENG").Cups.Single(c => c.Id == "ENG-LC");
        Assert.Equal(2, cfg.RoundLegs[5]);
        Assert.Equal(1, cfg.RoundLegs[6]);
        var europe = TestData.AllData.Countries.Single(c => c.Code == "ENG").Europe!;
        Assert.Equal(new[] { ("ENG-CUP", "UEL"), ("ENG-LC", "UECL") }, europe.AllCupPlaces.Select(p => (p.Cup, p.Competition)));
    }
}
