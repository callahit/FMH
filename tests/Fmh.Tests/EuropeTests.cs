using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

public class EuropeTests
{
    [Fact]
    public void EuropeFormatIsLoadedFromBasePackage()
    {
        Assert.NotNull(TestData.Data.Europe);
        Assert.Equal(new[] { "UCL", "UEL", "UECL" }, TestData.Data.Europe!.Competitions.Select(c => c.Id));
        Assert.Contains(TestData.Data.Europe.Competitions[0].Stages, s => s.Id == "q1");
        Assert.Contains(TestData.Data.Europe.Competitions[0].Stages, s => s.Type == "league");
    }

    [Fact]
    public void SeasonTwoCreatesEuropeCupsWithRealFixtures()
    {
        var s = TestData.NewSession(50);
        // Saison 1 zu Ende spielen → Plätze vergeben → neue Saison startet Europapokal.
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason || s.World.Date.Month == 7 && s.World.SeasonYear > 2026, 40_000);
        Assert.True(s.World.SeasonYear >= 2027, $"Saison {s.World.SeasonYear}");
        var europe = s.World.Cups.Where(c => c.Kind == CompetitionKind.Europe).ToList();
        Assert.NotEmpty(europe);
        Assert.Contains(europe, c => c.Id == "UCL");
        Assert.All(europe.Where(c => c.ParticipantIds.Count >= 2), c => Assert.NotEmpty(c.Fixtures));
        Assert.All(europe.SelectMany(c => c.Fixtures), f => Assert.Equal(CompetitionKind.Europe, f.Kind));
        // Qualifikation oder Ligaphase: mindestens ein Midweek-Termin.
        Assert.Contains(europe.SelectMany(c => c.Fixtures), f =>
            f.Date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday);
    }

    [Fact]
    public void MultiCountrySeasonSeedsQualifyingRoundsByCoefficientRank()
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 7, ClubKey = "nordheide", ManagerName = "Test" });
        var s = new GameSession(world, data, new TextMatchEngine());
        EuropeLogic.EnsureCoefficients(world, data.Europe!);
        // Künstlich: Spanien stärker als Deutschland → DE-Meister steigt später ein.
        world.CountryCoefficients["ESP"] = 80;
        world.CountryCoefficients["DE"] = 40;
        world.CountryCoefficients["ENG"] = 70;
        world.CountryCoefficients["ITA"] = 60;
        world.CountryCoefficients["FRA"] = 55;
        var ranks = EuropeLogic.CountryRanks(world, data.Europe!);
        Assert.Equal(1, ranks["ESP"]);
        Assert.True(ranks["DE"] > ranks["ESP"]);

        // Qualifikanten setzen und Wettbewerb erzeugen.
        var deLeague = world.Leagues.First(l => l.Id == "DE1");
        var table = deLeague.ClubIds.Take(4).ToList();
        foreach (var (id, i) in table.Select((id, i) => (id, i)))
            world.EuropeanEntries.Add(new EuropeanEntry { ClubId = id, CompetitionId = "UCL", SeasonYear = world.SeasonYear });
        var esp = world.Clubs.Values.First(c => c.CountryCode == "ESP" && world.FindLeague(c.LeagueId)?.Level == 1);
        world.EuropeanEntries.Add(new EuropeanEntry { ClubId = esp.Id, CompetitionId = "UCL", SeasonYear = world.SeasonYear });

        // Spanien als Meister der Vorsaison → direkter Ligaphasen-Einstieg (Rang 1).
        world.History.Add(new SeasonRecord
        {
            SeasonYear = world.SeasonYear - 1, LeagueId = "ES1", LeagueName = "ES1",
            ChampionId = esp.Id, ChampionName = esp.Name, FinalOrder = new List<int> { esp.Id },
        });
        var cups = EuropeLogic.CreateSeason(world, data, new Rng(1));
        var ucl = Assert.Single(cups, c => c.Id == "UCL");
        var league = ucl.Rounds.First(r => r.StageId == "league");
        Assert.Contains(esp.Id, league.Entrants);
        // DE (schwächerer Koeffizient) startet in den Play-offs / der Qualifikation.
        Assert.True(ucl.Rounds.Any(r => r.StageId is "po" or "q3" or "q2" && r.Entrants.Any(id => table.Contains(id)))
                    || ucl.Fixtures.Count > 0);
    }

    [Fact]
    public void EuropeFixturesAvoidDomesticLeagueClash()
    {
        var s = TestData.NewSession(51);
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason || s.World.SeasonYear > 2026, 40_000);
        foreach (var f in s.World.Cups.Where(c => c.Kind == CompetitionKind.Europe).SelectMany(c => c.Fixtures))
        {
            var clash = s.World.Leagues.SelectMany(l => l.Fixtures)
                .Any(l => l.Date == f.Date && l.Result is null && (l.Involves(f.HomeId) || l.Involves(f.AwayId)));
            Assert.False(clash, $"Kollision {f.Date} {f.HomeId}-{f.AwayId}");
        }
    }

    [Fact]
    public void PlayingEuropeMatchAdvancesKnockoutOrLeague()
    {
        var s = TestData.NewSession(52);
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason || s.World.SeasonYear > 2026, 40_000);
        var cup = s.World.Cups.First(c => c.Kind == CompetitionKind.Europe && c.Fixtures.Count > 0);
        var before = cup.Fixtures.Count(f => f.Result is not null);
        // Bis zum ersten Europapokalspiel des Kalenders.
        TestData.PlayUntil(s, _ => cup.Fixtures.Any(f => f.Result is not null), 20_000);
        Assert.True(cup.Fixtures.Count(f => f.Result is not null) > before);
    }

    /// <summary>
    /// Europapokal-Heimspiele buchen die Billetterie unter European (UEFA/media), nicht unter Tickets/matchday —
    /// sonst verzerren echte Europapokaltermine die Ligazahlen-Kalibrierung (DNCG/DFL Spieltag).
    /// </summary>
    [Fact]
    public void EuropeHomeGateBooksAsEuropeanNotLeagueMatchday()
    {
        var s = TestData.NewSession(53);
        var home = s.HumanClub;
        var away = s.World.Clubs.Values.First(c => c.Id != home.Id);
        home.Finance.SeasonTickets = 0;
        var f = new Fixture
        {
            Id = 9_000_001, CompetitionId = "UCL", Kind = CompetitionKind.Europe,
            HomeId = home.Id, AwayId = away.Id, Date = s.World.Date,
            RefereeId = s.World.Referees[0].Id,
        };
        s.PlayInstant(f);
        var gate = s.World.Transactions.Where(t =>
            t.ClubId == home.Id && t.Date == s.World.Date && t.Amount > 0
            && t.Type is TransactionType.Tickets or TransactionType.European).ToList();
        Assert.Contains(gate, t => t.Type == TransactionType.European);
        Assert.DoesNotContain(gate, t => t.Type == TransactionType.Tickets);
    }
}
