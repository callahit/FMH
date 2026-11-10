using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 8b: Turniere (EM, WM) mit Qualifikation in den Fenstern und Endrunde im Sommer.</summary>
public sealed class TournamentTests
{
    private static void AdvanceTo(GameSession s, DateOnly date)
    {
        for (var i = 0; i < 20_000 && s.World.Date < date; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
    }

    [Fact]
    public void TournamentRulesAreConsistent()
    {
        var rules = TestData.Data.Internationals;
        Assert.Contains(rules.Tournaments, t => t.Id == "EM");
        Assert.Contains(rules.Tournaments, t => t.Id == "WM");
        Assert.Empty(InternationalTournaments.Validate(rules));
        // Fehlerhafte Daten fallen auf
        var bad = new Fmh.Core.Data.InternationalRules
        {
            Windows = rules.Windows,
            Tournaments = { new Fmh.Core.Data.TournamentRule { Id = "X", Teams = 12, GroupSize = 4, Advance = 2, QualificationGroupSize = 8 } },
        };
        Assert.Equal(3, InternationalTournaments.Validate(bad).Count());
    }

    [Fact]
    public void StandingsDrawAndBracketFollowTheRules()
    {
        var group = new TournamentGroup { Name = "A", Codes = { "AAA", "BBB", "CCC" } };
        InternationalFixture F(string h, string a, int hg, int ag) => new() { HomeCode = h, AwayCode = a, Result = new MatchResult { HomeGoals = hg, AwayGoals = ag } };
        var table = InternationalTournaments.Standings(new[] { F("AAA", "BBB", 1, 1), F("CCC", "AAA", 0, 2), F("BBB", "CCC", 3, 0) }, group);
        Assert.Equal(new[] { "BBB", "AAA", "CCC" }, table.Select(r => r.Code)); // gleiche Punkte, bessere Tordifferenz
        Assert.Equal(4, table[0].Points);

        // Jeder gegen jeden mit Rückrunde: jedes Paar zweimal, je einmal Heimrecht
        var rounds = InternationalTournaments.RoundRobin(new[] { "A", "B", "C", "D", "E" }, twice: true);
        Assert.Equal(10, rounds.Count);
        var games = rounds.SelectMany(r => r).ToList();
        Assert.Equal(20, games.Count);
        Assert.Equal(games.Count, games.Distinct().Count());
        Assert.All(rounds, r => Assert.Equal(r.Count * 2, r.SelectMany(m => new[] { m.Home, m.Away }).Distinct().Count()));

        // Töpfe: die beiden Stärksten landen in verschiedenen Gruppen
        var drawn = InternationalTournaments.Draw(new[] { ("A", 15.0), ("B", 14.0), ("C", 10.0), ("D", 9.0) }, 2, new Rng(1));
        Assert.All(drawn, g => Assert.Equal(2, g.Codes.Count));
        Assert.DoesNotContain(drawn, g => g.Codes.Contains("A") && g.Codes.Contains("B"));

        // Überkreuz: A1–B2, B1–A2
        var tables = new List<List<TournamentRow>>
        {
            new() { new("A1", 3, 3, 0, 0, 5, 0), new("A2", 3, 2, 0, 1, 3, 2) },
            new() { new("B1", 3, 3, 0, 0, 5, 0), new("B2", 3, 2, 0, 1, 3, 2) },
        };
        Assert.Equal(new[] { "A1", "B2", "B1", "A2" }, InternationalTournaments.KnockoutEntrants(tables, 2));
        // Restplätze an die besten Dritten nach Punkten je Spiel
        var three = new List<List<TournamentRow>>
        {
            new() { new("X1", 4, 4, 0, 0, 8, 1), new("X2", 4, 2, 0, 2, 4, 4), new("X3", 4, 1, 1, 2, 3, 5) },
            new() { new("Y1", 4, 3, 1, 0, 7, 2), new("Y2", 4, 2, 1, 1, 5, 4), new("Y3", 4, 0, 2, 2, 2, 6) },
            new() { new("Z1", 4, 3, 0, 1, 6, 3), new("Z2", 4, 2, 0, 2, 4, 5), new("Z3", 4, 1, 2, 1, 3, 3) },
        };
        Assert.Equal(new[] { "X1", "X2", "Y1", "Y2", "Z1", "Z2", "Z3", "X3" }, InternationalTournaments.Qualified(three, 8));
    }

    [Fact]
    public void LateTournamentStillGetsPlayedInFull()
    {
        // Endrunde schon vor Spielbeginn fällig (wie ein älterer Spielstand im Juni): Sie rückt nach hinten und läuft durch.
        var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base" });
        data.Internationals.Tournaments = new() { new TournamentRule { Id = "EM", Name = "EM", FirstYear = 2027, Start = "07-02", Teams = 8, GroupSize = 4 } };
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 822, ClubKey = "nordheide", ManagerName = "Test" });
        var s = new GameSession(world, data, new TextMatchEngine());
        AdvanceTo(s, world.Date.AddDays(2));
        var t = world.Tournaments.Single();
        Assert.Equal(TournamentPhase.Finals, t.Phase);
        Assert.True(t.FinalsStart > world.Date);
        Assert.All(world.InternationalFixtures.Where(f => f.TournamentId == t.Id), f => Assert.True(f.Date > world.Date.AddDays(-2)));
        AdvanceTo(s, t.FinalsStart.AddDays(30));
        Assert.Equal(TournamentPhase.Done, t.Phase);
        Assert.NotEqual("", t.Champion);
    }

    /// <summary>
    /// Abnahme AP 8b: Die EM 2028 läuft vollständig durch – Qualifikation 2027/28 in den Fenstern, Endrunde im Juni.
    /// Ein Spielstand mitten in der Qualifikation wird gespeichert und weitergespielt; Nominierte fehlen dem Verein.
    /// </summary>
    [Fact]
    public void EuropeanChampionshipRunsFromQualificationToTheFinal()
    {
        var s = TestData.NewSession(821);
        AdvanceTo(s, new DateOnly(2027, 11, 20));
        var em = s.World.Tournaments.Single(t => t.Id == "EM-2028");
        Assert.Equal(TournamentPhase.Qualification, em.Phase);
        Assert.Contains(s.World.InternationalFixtures, f => f.TournamentId == em.Id && f.Stage.StartsWith("Q-") && f.Result is not null);

        var path = Path.Combine(Path.GetTempPath(), $"fmh-tour-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(s.World.Tournaments), System.Text.Json.JsonSerializer.Serialize(loaded.Tournaments));
            s = new GameSession(loaded, TestData.Data, new TextMatchEngine());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }

        var w = s.World;
        em = w.Tournaments.Single(t => t.Id == "EM-2028");
        AdvanceTo(s, em.FinalsStart.AddDays(-6));
        Assert.Equal(TournamentPhase.Finals, em.Phase);
        Assert.Equal(8, em.Qualified.Count);
        // Berufene fehlen dem Verein
        var called = w.NationalTeams.SelectMany(t => t.Squad).Select(id => w.Players[id]).ToList();
        Assert.NotEmpty(called);
        Assert.All(called, p => Assert.False(p.IsAvailable));
        Assert.All(w.NationalTeams.Where(t => !em.Qualified.Contains(t.Code)), t => Assert.Empty(t.Squad));

        AdvanceTo(s, new DateOnly(2028, 7, 2));
        Assert.Equal(TournamentPhase.Done, em.Phase);
        var fixtures = w.InternationalFixtures.Where(f => f.TournamentId == em.Id && !f.Stage.StartsWith("Q-")).ToList();
        Assert.All(fixtures, f => Assert.NotNull(f.Result));
        Assert.Equal(12, fixtures.Count(f => f.Stage.StartsWith("G-")));
        Assert.Equal(3, fixtures.Count(f => f.Stage.StartsWith("KO")));
        var final = fixtures.Single(f => f.Stage == "KO2");
        Assert.Equal(final.WinnerCode, em.Champion);
        Assert.Contains(em.Champion, em.Qualified);
        Assert.All(fixtures.Where(f => f.Stage.StartsWith("KO")), f => Assert.Contains(f.WinnerCode, new[] { f.HomeCode, f.AwayCode }));
        // Alle zurück im Verein
        Assert.All(w.NationalTeams, t => Assert.Empty(t.Squad));
        Assert.DoesNotContain(w.Players.Values, p => p.State.OnNationalDuty);
        Assert.Equal(2028, w.SeasonYear);
        // AP 8c: Nach dem Turnier ist jeder verbliebene Trainer bis zum nächsten Turnier verlängert, freie Posten sind neu besetzt.
        Assert.All(w.NationalTeams.Where(t => !t.Background), t =>
        {
            Assert.NotNull(t.CoachId);
            Assert.True(t.CoachUntil >= 2030, $"{t.Code} Vertrag bis {t.CoachUntil}");
        });
    }
}
