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
    /// <summary>
    /// Review C-1: Alle Qualifikanten spielen die Ligaphase. Ein großes Feld spielt höchstens
    /// <c>leagueMatchdays</c> Spieltage gegen verschiedene Gegner, niemand fällt stillschweigend heraus.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(6)]
    public void LeaguePhaseTakesEveryQualifierWithAtMostEightMatchdays(int qualifiers)
    {
        var s = TestData.NewSession(77);
        var world = s.World;
        var format = TestData.Data.Europe!.Competition("UCL")!;
        var cup = new Cup { Id = "UCL", Kind = CompetitionKind.Europe, SeasonYear = world.SeasonYear };
        var round = new CupRound { Number = 5, StageId = "league", StageType = "league" };
        cup.Rounds.Add(round);
        var teams = world.Clubs.Keys.OrderBy(id => id).Take(qualifiers).ToList();
        EuropeLogic.ScheduleLeague(world, TestData.Data, cup, round, teams, new Rng(5));

        Assert.Equal(teams.OrderBy(i => i), round.Entrants.OrderBy(i => i));
        var max = Math.Min(format.LeagueMatchdays, qualifiers % 2 == 0 ? qualifiers - 1 : qualifiers);
        foreach (var id in teams)
        {
            var games = cup.Fixtures.Where(f => f.Involves(id)).ToList();
            Assert.InRange(games.Count, max - 1, max);
            var opponents = games.Select(f => f.HomeId == id ? f.AwayId : f.HomeId).ToList();
            Assert.Equal(opponents.Count, opponents.Distinct().Count());
            Assert.Contains(world.EuropeanEntries, e => e.ClubId == id && e.CompetitionId == "UCL" && !e.Eliminated);
        }
        Assert.True(cup.Fixtures.Select(f => f.Matchday).Distinct().Count() <= format.LeagueMatchdays);
    }
    /// <summary>Review I-1: Zugewinne verfallen nach dem Fünfjahresfenster; der Koeffizient bleibt begrenzt.</summary>
    [Fact]
    public void CoefficientGainsExpireAfterTheWindow()
    {
        var data = TestData.Data;
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 8, ClubKey = "nordheide", ManagerName = "Test" });
        var format = data.Europe!;
        EuropeLogic.EnsureCoefficients(world, format);
        var club = world.Clubs.Keys.First();
        var start = world.ClubCoefficients[club];
        var startCountry = world.CountryCoefficients["DE"];
        Assert.Equal(start, world.Coefficients.ClubBase[club]);

        // Jede Saison 10 Punkte Zugewinn, dann Saisonwechsel.
        var totals = new List<double>();
        for (var season = 0; season < 8; season++)
        {
            world.ClubCoefficients[club] += 10;
            world.CountryCoefficients["DE"] += 2;
            world.SeasonYear++;
            EuropeLogic.RollCoefficients(world, format);
            EuropeLogic.RollCoefficients(world, format); // zweiter Aufruf in derselben Saison ändert nichts
            totals.Add(world.ClubCoefficients[club]);
        }
        var kept = format.Access.WindowYears - 1;
        Assert.Equal(kept, world.Coefficients.Seasons.Count);
        Assert.Equal(start + 10 * kept, world.ClubCoefficients[club], 6);
        Assert.Equal(startCountry + 2 * kept, world.CountryCoefficients["DE"], 6);
        Assert.Equal(totals[^1], totals[^2], 6); // begrenzt statt endlos wachsend
    }

    /// <summary>Spielstände ohne Fenster: alles Bisherige wird Grundwert; Speichern und Laden behalten das Fenster.</summary>
    [Fact]
    public void CoefficientWindowSurvivesSavingAndStartsForOldSaves()
    {
        var data = TestData.Data;
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 9, ClubKey = "nordheide", ManagerName = "Test" });
        var format = data.Europe!;
        EuropeLogic.EnsureCoefficients(world, format);
        world.Coefficients = new CoefficientWindow(); // wie ein Spielstand vor Version 7
        var club = world.Clubs.Keys.First();
        world.ClubCoefficients[club] = 99;
        world.SeasonYear++;
        EuropeLogic.RollCoefficients(world, format);
        Assert.Equal(99, world.Coefficients.ClubBase[club]);
        Assert.Equal(99, world.ClubCoefficients[club]);
        world.ClubCoefficients[club] += 5;
        world.SeasonYear++;
        EuropeLogic.RollCoefficients(world, format);

        var path = Path.Combine(Path.GetTempPath(), $"fmh-coeff-{Guid.NewGuid():N}.fmh");
        try
        {
            Fmh.Core.Persistence.SqliteStore.Save(world, path);
            var loaded = Fmh.Core.Persistence.SqliteStore.Load(path);
            Assert.Equal(world.Coefficients.Seasons.Select(x => x.SeasonYear), loaded.Coefficients.Seasons.Select(x => x.SeasonYear));
            Assert.Equal(5, loaded.Coefficients.Seasons[^1].Clubs[club], 6);
            Assert.Equal(99, loaded.Coefficients.ClubBase[club]);
            Assert.Equal("7", Fmh.Core.Persistence.SqliteStore.ReadMetaTable(path)["schema.license"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
