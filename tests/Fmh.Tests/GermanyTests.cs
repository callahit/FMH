using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>
/// Deutschland bis zur 4. Liga (Umsetzungskonzept AP 4): 3. Liga, Regionalliga mit fünf Staffeln, Oberliga als Pool
/// unter der untersten Ebene, Staffeln mit eigenen Aufstiegsregeln und zweite Mannschaften.
/// </summary>
public class GermanyTests
{
    private static readonly string[] Groups = { "DE4N", "DE4NO", "DE4W", "DE4SW", "DE4S" };

    [Fact]
    public void WorldHasFourLevelsGroupsAndPool()
    {
        var w = TestData.NewSession().World;
        Assert.Equal(new[] { 1, 2, 3, 4 }, w.Leagues.Select(l => l.Level).Distinct().Order());
        Assert.Equal(20, w.FindLeague("DE3")!.ClubIds.Count);
        Assert.Equal(Groups, w.Leagues.Where(l => l.Level == 4).Select(l => l.Id));
        Assert.All(w.Leagues.Where(l => l.Level == 4), g => Assert.Equal(18, g.ClubIds.Count));
        // Feste Staffeln nach Region.
        Assert.All(w.Leagues.Where(l => l.Level == 4), g =>
            Assert.All(g.ClubIds, id => Assert.Contains(w.Clubs[id].Region!, g.Format.Groups.Single(x => x.Id == g.Id).Regions)));
        Assert.Equal(40, w.Clubs.Values.Count(c => c.LeagueId == "DE-AMA"));
        var reserves = w.Clubs.Values.Where(c => c.ReserveOf is not null).ToList();
        Assert.Equal(5, reserves.Count);
        Assert.All(reserves, r => Assert.Equal(4, w.FindLeague(r.LeagueId)!.Level));
    }

    /// <summary>Pokal mit 64 Plätzen: alle Vereine der Ebenen 1–3, die übrigen Plätze an die Regionalligisten mit dem höchsten Ruf.</summary>
    [Fact]
    public void CupTakesHigherLevelsFirst()
    {
        var w = TestData.NewSession().World;
        var cup = w.Cups.Single(c => c.Id == "DE-CUP");
        Assert.Equal(64, cup.ParticipantIds.Count);
        var upper = w.Leagues.Where(l => l.Level <= 3).SelectMany(l => l.ClubIds).ToList();
        Assert.All(upper, id => Assert.Contains(id, cup.ParticipantIds));
        var regional = cup.ParticipantIds.Except(upper).Select(id => w.Clubs[id]).ToList();
        Assert.Equal(8, regional.Count);
        Assert.All(regional, c => Assert.Equal(4, w.FindLeague(c.LeagueId)!.Level));
        Assert.DoesNotContain(regional, c => c.ReserveOf is not null);
        var left = w.Leagues.Where(l => l.Level == 4).SelectMany(l => l.ClubIds).Select(id => w.Clubs[id])
            .Where(c => c.ReserveOf is null && !cup.ParticipantIds.Contains(c.Id));
        Assert.True(left.Max(c => c.Reputation) <= regional.Min(c => c.Reputation));
    }

    /// <summary>
    /// Eine Saison: Die Meister von West, Südwest und Süd steigen direkt auf, die Meister von Nord und Nordost spielen den
    /// vierten Aufsteiger aus. Die beiden Letzten jeder Staffel fallen in die Oberliga, ebenso viele Oberligisten rücken nach.
    /// </summary>
    [Fact]
    public void SeasonMovesClubsThroughAllLevelsAndThePool()
    {
        var s = TestData.NewSession(seed: 11);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
        var tables = w.Leagues.ToDictionary(l => l.Id, l => LeagueTable.Compute(l).Select(r => r.ClubId).ToList());
        var playoff = w.Cups.Single(c => c.Id == "DE-AUF");
        Assert.Equal(new[] { tables["DE4N"][0], tables["DE4NO"][0] }.Order(), playoff.ParticipantIds.Order());
        var winner = playoff.WinnerId!.Value;
        var bottom = Groups.SelectMany(g => tables[g].Skip(16)).ToList();
        var pool = w.Clubs.Values.Where(c => c.LeagueId == "DE-AMA").Select(c => c.Id).ToList();
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);

        foreach (var l in w.Leagues) Assert.Equal(l.Format.Teams, l.ClubIds.Count);
        Assert.Equal(40, w.Clubs.Values.Count(c => c.LeagueId == "DE-AMA"));
        foreach (var g in new[] { "DE4W", "DE4SW", "DE4S" })
        {
            var champion = w.Clubs[tables[g][0]];
            Assert.True(champion.LeagueId == "DE3" || !champion.License.PromotionAllowed, $"{champion.Name} ist nicht aufgestiegen.");
        }
        Assert.True(w.Clubs[winner].LeagueId == "DE3" || !w.Clubs[winner].License.PromotionAllowed);
        Assert.StartsWith("DE4", w.Clubs[playoff.ParticipantIds.Single(id => id != winner)].LeagueId);
        Assert.All(bottom, id => Assert.Equal("DE-AMA", w.Clubs[id].LeagueId));
        var risen = pool.Where(id => w.Clubs[id].LeagueId != "DE-AMA").ToList();
        Assert.Equal(bottom.Count, risen.Count);
        Assert.All(risen, id => Assert.StartsWith("DE4", w.Clubs[id].LeagueId));
    }

    [Fact]
    public void ValidatorChecksPoolsAndGroupRules()
    {
        static CountryConfig Copy(CountryConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(System.Text.Json.JsonSerializer.Serialize(c, Json.Compact), Json.Compact)!;
        var de = Copy(TestData.Data.Countries.Single(c => c.Code == "DE"));
        GameData With(CountryConfig c) => new() { Countries = new() { c }, Clubs = TestData.Data.Clubs, Names = TestData.Data.Names };
        Assert.Empty(PyramidValidator.Validate(With(de)));

        de.Pools[0].Teams = 8;
        Assert.Contains(PyramidValidator.Validate(With(de)), e => e.Contains("reichen nicht für 10 Absteiger"));
        de.Pools[0].Teams = 40;
        de.Pools[0].Feeds = null;
        Assert.Contains(PyramidValidator.Validate(With(de)), e => e.Contains("unterste Ebene kann keine Absteiger"));
        de.Pools[0].Feeds = "DE4";
        // Eigene Regel einer Staffel: Direktaufstieg auch für den Nordmeister, dann geht die Bilanz nicht mehr auf.
        de.Divisions.Single(d => d.Id == "DE4").Groups.Single(g => g.Id == "DE4N").Movements.Clear();
        Assert.Contains(PyramidValidator.Validate(With(de)), e => e.Contains("4 Absteiger aus Ebene 3, aber 5 Aufsteiger"));
    }
}
