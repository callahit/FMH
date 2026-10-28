using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Länderpaket Italien (Umsetzungskonzept AP 4): Serie A und B (je 20), Serie C in drei Gruppen mit Under-23-Mannschaften,
/// Serie D als Pool, Play-off und Play-out der Serie B, Play-off der Serie C, Coppa mit gestaffeltem Einstieg und Supercoppa.
/// </summary>
public class ItalyTests
{
    private static GameSession NewItaly(ulong seed = 5)
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = seed, ClubKey = data.Clubs.First(c => c.Division == "ITA2").Key, ManagerName = "Test",
            Activation = new() { ["DE"] = CountryActivation.Inactive, ["ENG"] = CountryActivation.Inactive, ["ESP"] = CountryActivation.Inactive, ["FRA"] = CountryActivation.Inactive },
        });
        return new GameSession(world, data, new TextMatchEngine());
    }

    [Fact]
    public void PyramidWithGroupsAndUnder23Teams()
    {
        var w = NewItaly().World;
        Assert.Equal(new[] { ("ITA1", 20), ("ITA2", 20), ("ITA3A", 20), ("ITA3B", 20), ("ITA3C", 20) },
            w.Leagues.Where(l => l.CountryCode == "ITA").Select(l => (l.Id, l.ClubIds.Count)));
        Assert.Equal(50, w.Clubs.Values.Count(c => c.LeagueId == "ITA-D"));
        var reserves = w.Clubs.Values.Where(c => c.CountryCode == "ITA" && c.ReserveOf is not null).ToList();
        Assert.Equal(3, reserves.Count);
        Assert.All(reserves, r => Assert.Equal(1, w.FindLeague(w.Clubs[r.ReserveOf!.Value].LeagueId)!.Level));
    }

    /// <summary>Coppa: 8 Vereine in Runde 1, 28 ab Runde 2, die besten acht der Serie A ab dem Achtelfinale (Runde 4).</summary>
    [Fact]
    public void CupStaggersTheEntry()
    {
        var w = NewItaly().World;
        var cup = w.Cups.Single(c => c.Id == "ITA-CUP");
        Assert.Equal(7, cup.Rounds.Count);
        Assert.Equal(new[] { 8, 28, 0, 8 }, cup.Rounds.Take(4).Select(r => r.Entrants.Count));
        Assert.All(cup.Rounds[0].Entrants, id => Assert.True(w.Clubs[id].LeagueId is "ITA2" or "ITA3A" or "ITA3B" or "ITA3C"));
        Assert.All(cup.Rounds[3].Entrants, id => Assert.Equal("ITA1", w.Clubs[id].LeagueId));
    }

    /// <summary>
    /// Eine Saison: Play-off der Serie B 3.–6., Play-out 16. gegen 17. (Verlierer in die Serie C), Play-off der Serie C mit
    /// den drei Gruppenzweiten und einem Dritten; danach haben alle Ligen ihre Teamzahl, und die Supercoppa hat vier Teilnehmer.
    /// </summary>
    [Fact]
    public void SeasonWithPlayoffPlayoutAndSupercup()
    {
        var s = NewItaly(7);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 30_000);
        var table = LeagueTable.Compute(w.League("ITA2")).Select(r => r.ClubId).ToList();
        var playout = w.Cups.Single(c => c.Id == "ITA-PL2");
        Assert.Equal(new[] { table[15], table[16] }.Order(), playout.ParticipantIds.Order());
        var loser = playout.ParticipantIds.Single(id => id != playout.WinnerId);
        Assert.Single(w.Cups, c => c.Id.StartsWith("ITA-PO3"));
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 30_000);
        foreach (var l in w.Leagues.Where(l => l.CountryCode == "ITA")) Assert.Equal(l.Format.Teams, l.ClubIds.Count);
        Assert.StartsWith("ITA3", w.Clubs[loser].LeagueId);
        Assert.Equal(4, w.Cups.Single(c => c.Id == "ITA-SC").ParticipantIds.Count);
    }

    /// <summary>
    /// Liegt der Termin einer späteren Play-off-Runde vor dem Saisonende, rückt die Runde beim Auslosen nach: Das Finale
    /// spielt nach dem Halbfinale, Hinspiel vor Rückspiel.
    /// </summary>
    [Fact]
    public void LaterPlayoffRoundsMoveBehindTheLastMatchday()
    {
        var s = NewItaly(9);
        var w = s.World;
        w.Country("ITA").Playoffs.Single(p => p.Id == "ITA-PO2").Dates[1] = "04-01,04-05";
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 30_000);
        var po = w.Cups.Single(c => c.Id == "ITA-PO2");
        var semis = po.Fixtures.Where(f => f.Matchday == 1).Max(f => f.Date);
        var final = po.Fixtures.Where(f => f.Matchday == 2).OrderBy(f => f.Leg).ToList();
        Assert.True(final[0].Date > semis && final[1].Date > final[0].Date);
        Assert.NotNull(po.WinnerId);
    }
}
