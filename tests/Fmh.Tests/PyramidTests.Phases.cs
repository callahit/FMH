using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>Ligapyramiden: Meister- und Abstiegsrunden, Phasentermine, Zwangsabstieg und Prüfung der Phasengruppen.</summary>
public partial class PyramidTests
{
    /// <summary>Saisonphasen: 2. Testliga mit Meister- und Abstiegsrunde (je 5 Teams, einfache Runde, halbe Punkte abgerundet).</summary>
    [Fact]
    public void ChampionshipAndRelegationRoundCarryHalfThePoints()
    {
        static CountryConfig Copy(CountryConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(System.Text.Json.JsonSerializer.Serialize(c, Json.Compact), Json.Compact)!;
        var tl = Copy(Data.Countries.Single(c => c.Code == "TL"));
        tl.Divisions.Single(d => d.Id == "TL2").Phases.Add(new PhaseConfig
        {
            Id = "split", Name = "Meister- und Abstiegsrunde", Rounds = 1, PointsCarry = 0.5, Rounding = PointsRounding.Down,
            Groups = { new PhaseGroup { Id = "top", Name = "Meisterrunde", Positions = "1-5" }, new PhaseGroup { Id = "bottom", Name = "Abstiegsrunde", Positions = "6-10" } },
        });
        var data = WithCountry(tl);
        Assert.Empty(PyramidValidator.Validate(data));
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = 79, ClubKey = "tl1-5", ManagerName = "Test", Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        var s = new GameSession(world, data, new TextMatchEngine());
        var tl2 = world.League("TL2");
        TestData.PlayUntil(s, _ => tl2.PhaseScheduled, 20_000);
        var regular = LeagueTable.Compute(tl2, tl2.RegularMatchdays);
        Assert.Equal(90, tl2.Fixtures.Count(f => f.Matchday <= tl2.RegularMatchdays));
        Assert.Equal(20, tl2.Fixtures.Count(f => f.Matchday > tl2.RegularMatchdays));
        Assert.False(tl2.IsComplete);

        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
        var final = LeagueTable.Compute(tl2);
        // Die Meisterrunde belegt die Plätze 1–5, unabhängig von den Punkten der Abstiegsrunde.
        Assert.Equal(regular.Take(5).Select(r => r.ClubId).ToHashSet(), final.Take(5).Select(r => r.ClubId).ToHashSet());
        foreach (var row in final)
        {
            var before = regular.First(r => r.ClubId == row.ClubId);
            Assert.True(row.Points >= before.Points / 2 && row.Points <= before.Points / 2 + 12, $"{row.ClubId}: {row.Points} aus {before.Points}");
            Assert.Equal(before.Played + 4, row.Played);
        }
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        AssertConsistent(world);
        Assert.All(world.Leagues, l => Assert.Equal(l.RegularMatchdays, l.MatchdayCount));
    }

    /// <summary>Zwei Folgephasen: die zweite wird erst nach der ersten angesetzt und übernimmt deren Tabelle.</summary>
    [Fact]
    public void SecondPhaseUsesTheTableOfTheFirst()
    {
        static CountryConfig Copy(CountryConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(System.Text.Json.JsonSerializer.Serialize(c, Json.Compact), Json.Compact)!;
        PhaseConfig Phase(string id) => new()
        {
            Id = id, Name = id, Rounds = 1, PointsCarry = 1,
            Groups =
            {
                new PhaseGroup { Id = id + "-top", Name = "Meisterrunde", Positions = "1-5" },
                new PhaseGroup { Id = id + "-bottom", Name = "Abstiegsrunde", Positions = "6-10" },
            },
        };
        var tl = Copy(Data.Countries.Single(c => c.Code == "TL"));
        var div = tl.Divisions.Single(d => d.Id == "TL2");
        div.Phases.Add(Phase("first"));
        div.Phases.Add(Phase("second"));
        var data = WithCountry(tl);
        Assert.Empty(PyramidValidator.Validate(data));
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = 83, ClubKey = "tl1-5", ManagerName = "Test", Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        var s = new GameSession(world, data, new TextMatchEngine());
        var tl2 = world.League("TL2");
        TestData.PlayUntil(s, _ => tl2.PhasesScheduled >= 2, 20_000);
        Assert.Equal(20, tl2.Fixtures.Count(f => f.Phase == 1));
        var phase1End = tl2.Fixtures.Where(f => f.Phase == 1).Max(f => f.Matchday);
        var afterFirst = LeagueTable.Compute(tl2, phase1End);
        var top = afterFirst.Take(5).Select(r => r.ClubId).ToHashSet();
        var phase2 = tl2.Fixtures.Where(f => f.Phase == 2).ToList();
        Assert.Equal(20, phase2.Count);
        Assert.All(phase2, f => Assert.Equal(top.Contains(f.HomeId), top.Contains(f.AwayId)));
        Assert.False(tl2.IsComplete);

        var path = Path.Combine(Path.GetTempPath(), "fmh-phase-" + Guid.NewGuid() + ".fmh");
        try
        {
            Fmh.Core.Persistence.SqliteStore.Save(world, path);
            var loaded = Fmh.Core.Persistence.SqliteStore.Load(path).League("TL2");
            Assert.Equal(2, loaded.PhasesScheduled);
            Assert.Equal(20, loaded.Fixtures.Count(f => f.Phase == 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Termine der zweiten Phase folgen dem Wochenrhythmus des Landes und lassen die Winterpause aus.</summary>
    [Fact]
    public void PhaseDatesSkipTheWinterBreak()
    {
        var season = new SeasonConfig { StartMonth = 7, WinterBreakStart = "12-21", WinterBreakEnd = "01-14", MatchDay = DayOfWeek.Saturday };
        var dates = ScheduleGenerator.WeeklyDates(season, 2026, new DateOnly(2026, 12, 15), 3);
        Assert.Equal(new[] { new DateOnly(2026, 12, 19), new DateOnly(2027, 1, 16), new DateOnly(2027, 1, 23) }, dates);
    }

    /// <summary>Ein Meister ohne Lizenz steigt zwangsweise ab statt auf; kein Verein steht in zwei Wechseln zugleich.</summary>
    [Fact]
    public void ForcedRelegationBlocksPromotion()
    {
        var s = NewTestland(81);
        var w = s.World;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
        var champion = w.Clubs[LeagueTable.Compute(w.League("TL3"))[0].ClubId];
        champion.License.ForcedRelegation = true;
        champion.License.PromotionAllowed = true;
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        AssertConsistent(w);
        Assert.Equal(4, w.FindLeague(champion.LeagueId)!.Level);
    }

    private static GameData WithCountry(CountryConfig country)
    {
        var data = new GameData
        {
            Packages = Data.Packages, Countries = Data.Countries.Where(c => c.Code != country.Code).Append(country).ToList(),
            Clubs = Data.Clubs, Names = Data.Names, Formations = Data.Formations, PositionProfiles = Data.PositionProfiles,
            Injuries = Data.Injuries, Roles = Data.Roles, Engine = Data.Engine, Buildings = Data.Buildings, Events = Data.Events,
            PressQuestions = Data.PressQuestions, Sponsors = Data.Sponsors, Languages = Data.Languages, DefaultLanguage = Data.DefaultLanguage,
        };
        data.UseLanguage("de");
        return data;
    }

    [Fact]
    public void ValidatorChecksPhaseGroups()
    {
        var tl = System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(
            System.Text.Json.JsonSerializer.Serialize(Data.Countries.Single(c => c.Code == "TL"), Json.Compact), Json.Compact)!;
        tl.Divisions.Single(d => d.Id == "TL2").Phases.Add(new PhaseConfig
        {
            Name = "Lücke", PointsCarry = 1.5,
            Groups = { new PhaseGroup { Positions = "1-5" }, new PhaseGroup { Positions = "7-10" } },
        });
        var errors = PyramidValidator.Validate(WithCountries(tl));
        Assert.Contains(errors, e => e.Contains("nicht alle Plätze genau einmal"));
        Assert.Contains(errors, e => e.Contains("Punkteübernahme"));
    }
}
