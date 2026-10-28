using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Länderpaket Frankreich (Umsetzungskonzept AP 4): Ligue 1 bis 3 (je 18), National 1 in drei Gruppen, National 2 als Pool,
/// Barrages 16. gegen 3. zwischen Ligue 1/2 und 2/3, Coupe mit gestaffeltem Einstieg und Trophée des Champions.
/// </summary>
public class FranceTests
{
    private static GameSession NewFrance(ulong seed = 5)
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = seed, ClubKey = data.Clubs.First(c => c.Division == "FRA2").Key, ManagerName = "Test",
            Activation = new()
            {
                ["DE"] = CountryActivation.Inactive, ["ENG"] = CountryActivation.Inactive, ["ESP"] = CountryActivation.Inactive,
                ["ITA"] = CountryActivation.Inactive,
            },
        });
        return new GameSession(world, data, new TextMatchEngine());
    }

    [Fact]
    public void PyramidWithGroupsAndReserves()
    {
        var w = NewFrance().World;
        Assert.Equal(new[] { ("FRA1", 18), ("FRA2", 18), ("FRA3", 18), ("FRA4A", 16), ("FRA4B", 16), ("FRA4C", 16) },
            w.Leagues.Where(l => l.CountryCode == "FRA").Select(l => (l.Id, l.ClubIds.Count)));
        Assert.Equal(50, w.Clubs.Values.Count(c => c.LeagueId == "FRA-N2"));
        Assert.Equal(3, w.Clubs.Values.Count(c => c.CountryCode == "FRA" && c.ReserveOf is not null));
        // Ohne Meldeliste: Alle Spieler sind spielberechtigt.
        Assert.All(w.SquadOf(w.Clubs[w.League("FRA1").ClubIds[0]]), p => Assert.True(Registration.IsEligible(p, w)));
    }

    /// <summary>Coupe: Ligue 1 ab Runde 3, Ligue 2 ab Runde 2, alle übrigen ohne zweite Mannschaften ab Runde 1.</summary>
    [Fact]
    public void CupLetsProfessionalClubsEnterLater()
    {
        var w = NewFrance().World;
        var cup = w.Cups.Single(c => c.Id == "FRA-CUP");
        Assert.Equal(8, cup.Rounds.Count);
        Assert.All(cup.Rounds[2].Entrants, id => Assert.Equal("FRA1", w.Clubs[id].LeagueId));
        Assert.Equal(18, cup.Rounds[2].Entrants.Count);
        Assert.Equal(18, cup.Rounds[1].Entrants.Count);
        Assert.All(cup.Rounds[0].Entrants, id => Assert.Null(w.Clubs[id].ReserveOf));
    }

    /// <summary>Eine Saison: Barrages 16. gegen 3., danach volle Ligen; der Trophée hat zwei Teilnehmer.</summary>
    [Fact]
    public void SeasonWithBarragesAndTrophy()
    {
        var s = NewFrance(7);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 30_000);
        var l1 = LeagueTable.Compute(w.League("FRA1")).Select(r => r.ClubId).ToList();
        var l2 = LeagueTable.Compute(w.League("FRA2")).Select(r => r.ClubId).ToList();
        var barrage = w.Cups.Single(c => c.Id == "FRA-BAR1");
        Assert.Equal(new[] { l1[15], l2[2] }.Order(), barrage.ParticipantIds.Order());
        var winner = barrage.WinnerId!.Value;
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 30_000);
        foreach (var l in w.Leagues.Where(l => l.CountryCode == "FRA")) Assert.Equal(l.Format.Teams, l.ClubIds.Count);
        Assert.True(w.Clubs[winner].LeagueId == "FRA1" || !w.Clubs[winner].License.PromotionAllowed);
        Assert.Equal(2, w.Cups.Single(c => c.Id == "FRA-SC").ParticipantIds.Count);
    }

    /// <summary>Trophée: Gewinnt der Meister auch die Coupe, spielt er gegen den Zweiten der Ligue 1, nicht gegen den Finalisten.</summary>
    [Fact]
    public void DoubleWinnerMeetsTheLeagueRunnerUp()
    {
        var w = NewFrance().World;
        var league = w.League("FRA1").ClubIds;
        int champion = league[0], runnerUp = league[1], finalist = league[5];
        w.History.Add(new SeasonRecord { SeasonYear = w.SeasonYear - 1, LeagueId = "FRA1", FinalOrder = league.ToList() });
        w.History.Add(new SeasonRecord { SeasonYear = w.SeasonYear - 1, LeagueId = "FRA-CUP", FinalOrder = new() { champion, finalist } });
        var country = w.Country("FRA");
        var entrants = CupEntries.Resolve(w, country, country.Cups.Single(c => c.Id == "FRA-SC"));
        Assert.Equal(new[] { champion, runnerUp }, entrants[1]);
    }
}
