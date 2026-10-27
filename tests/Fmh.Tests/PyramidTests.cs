using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Ligapyramiden (Umsetzungskonzept AP 3, Abschnitt 6): Testland mit vier Ebenen, geteilter 4. Liga (drei Staffeln),
/// Relegation, Aufstiegsturnier und zweiten Mannschaften.
/// </summary>
public class PyramidTests
{
    private static readonly Lazy<GameData> Lazy = new(() => PackageLoader.Load(new[]
    {
        PackageLoader.FindDataRoot(),
        Path.Combine(TestData.RepoRoot(), "tests", "Fmh.Tests", "Packages"),
    }));

    private static GameData Data => Lazy.Value;

    private static GameSession NewTestland(ulong seed = 77)
    {
        var world = WorldGenerator.Create(Data, new NewGameOptions
        {
            Seed = seed, ClubKey = "tl1-5", ManagerName = "Test",
            Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        return new GameSession(world, Data, new TextMatchEngine());
    }

    [Fact]
    public void TestlandPassesValidation()
    {
        Assert.Empty(DataValidator.Validate(Data));
    }

    [Fact]
    public void OldFormatIsMappedToMovements()
    {
        var de = Data.Countries.Single(c => c.Code == "DE");
        var top = de.Divisions.Single(d => d.Id == "DE1");
        var rule = Assert.Single(top.Movements);
        Assert.Equal((MovementDirection.Down, "17-18"), (rule.To, rule.Positions));
        var playoff = Assert.Single(de.Playoffs);
        Assert.Equal(new[] { ("DE1", 16), ("DE2", 3) }, playoff.Entrants.Select(e => (e.Division, e.Position)));
        var tl3 = Data.Countries.Single(c => c.Code == "TL").Divisions.Single(d => d.Id == "TL3");
        Assert.Equal((2, 3), (tl3.Promotion, tl3.Relegation));
    }

    [Fact]
    public void WorldHasGroupsAndReserves()
    {
        var s = NewTestland();
        var w = s.World;
        Assert.All(w.Countries, c => Assert.Equal("TL", c.Code));
        var groups = w.Leagues.Where(l => l.Level == 4).ToList();
        Assert.Equal(new[] { "TL4N", "TL4M", "TL4S" }, groups.Select(g => g.Id));
        Assert.All(groups, g => Assert.Equal(8, g.ClubIds.Count));
        var reserves = w.Clubs.Values.Where(c => c.ReserveOf is not null).ToList();
        Assert.Equal(3, reserves.Count);
        // Spielrecht: höchstens drei Spieler über der Altersgrenze im Kader der zweiten Mannschaft.
        Assert.All(reserves, r => Assert.True(w.SquadOf(r).Count(p => p.Age(w.Date) > 23) <= 3));
        // Zweite Mannschaften spielen nicht im Pokal.
        var cup = Assert.Single(w.Cups);
        Assert.DoesNotContain(cup.ParticipantIds, id => w.Clubs[id].ReserveOf is not null);
        Assert.Contains(cup.ParticipantIds, id => groups.Any(g => g.ClubIds.Contains(id)));
    }

    /// <summary>Abnahme AP 3: fünf Saisons ohne Validierungsfehler, jede Liga behält ihre Teamzahl, Regeln für zweite Mannschaften gelten.</summary>
    [Fact]
    public void FiveSeasonsKeepTeamCountsConsistent()
    {
        var s = NewTestland();
        var w = s.World;
        var start = w.SeasonYear;
        var tournaments = 0;
        for (var season = 1; season <= 5; season++)
        {
            TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
            tournaments += w.Cups.Count(c => c.Kind == CompetitionKind.Playoff && c.Rounds.Count == 2 && c.WinnerId is not null);
            TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
            Assert.Equal(start + season, w.SeasonYear);
            AssertConsistent(w);
        }
        Assert.Empty(DataValidator.Validate(Data));
        Assert.Equal(5, tournaments);
    }

    private static void AssertConsistent(World w)
    {
        var inLeagues = w.Leagues.SelectMany(l => l.ClubIds).ToList();
        Assert.Equal(inLeagues.Count, inLeagues.Distinct().Count());
        Assert.Equal(54, inLeagues.Count);
        foreach (var l in w.Leagues)
        {
            Assert.Equal(l.Format.Teams, l.ClubIds.Count);
            Assert.All(l.ClubIds, id => Assert.Equal(l.Id, w.Clubs[id].LeagueId));
        }
        var gap = w.Country("TL").Reserves.MinLevelGap;
        foreach (var r in w.Clubs.Values.Where(c => c.ReserveOf is not null))
        {
            var level = w.FindLeague(r.LeagueId)!.Level;
            var parent = w.FindLeague(w.Clubs[r.ReserveOf!.Value].LeagueId)!.Level;
            Assert.True(level >= parent + gap || level == w.Leagues.Max(l => l.Level), $"{r.Name} spielt auf Ebene {level}, erste Mannschaft auf {parent}.");
        }
    }

    [Fact]
    public void ReservesAndRegionsSurviveSaving()
    {
        var s = NewTestland();
        var path = Path.Combine(Path.GetTempPath(), $"fmh-tl-{Guid.NewGuid():N}.fmh");
        try
        {
            Fmh.Core.Persistence.SqliteStore.Save(s.World, path);
            var w = Fmh.Core.Persistence.SqliteStore.Load(path);
            foreach (var c in s.World.Clubs.Values)
                Assert.Equal((c.ReserveOf, c.Region, c.Latitude, c.Longitude, c.LeagueId),
                    (w.Clubs[c.Id].ReserveOf, w.Clubs[c.Id].Region, w.Clubs[c.Id].Latitude, w.Clubs[c.Id].Longitude, w.Clubs[c.Id].LeagueId));
            Assert.Equal(3, w.League("TL4N").Format.Groups.Count);
            Assert.Equal(s.World.Leagues.Select(l => l.Id), w.Leagues.Select(l => l.Id));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Migrationstest: Abschnitt "world" Version 6 (ohne Spalten für zweite Mannschaften) lädt und wird angehoben.</summary>
    [Fact]
    public void WorldSectionVersionSixMigrates()
    {
        var s = TestData.NewSession();
        var path = Path.Combine(Path.GetTempPath(), $"fmh-v6-{Guid.NewGuid():N}.fmh");
        try
        {
            Fmh.Core.Persistence.SqliteStore.Save(s.World, path);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = """
                    ALTER TABLE clubs DROP COLUMN reserve_of; ALTER TABLE clubs DROP COLUMN region;
                    ALTER TABLE clubs DROP COLUMN latitude; ALTER TABLE clubs DROP COLUMN longitude;
                    UPDATE meta SET value = '6' WHERE key = 'schema.world';
                    """;
                cmd.ExecuteNonQuery();
            }
            var w = Fmh.Core.Persistence.SqliteStore.Load(path);
            Assert.Equal(s.World.Clubs.Count, w.Clubs.Count);
            Assert.All(w.Clubs.Values, c => Assert.Null(c.ReserveOf));
            Assert.Equal("7", Fmh.Core.Persistence.SqliteStore.ReadMetaTable(path)["schema.world"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GeographicAndRotatingAssignmentFillAllGroups()
    {
        var s = NewTestland();
        var w = s.World;
        var groups = w.Leagues.Where(l => l.Level == 4).ToList();
        var ids = groups.SelectMany(g => g.ClubIds).ToHashSet();
        foreach (var mode in new[] { GroupAssignment.Geographic, GroupAssignment.Rotating })
        {
            groups[0].Format.Assignment = mode;
            Pyramid.AssignGroups(w, groups, new List<int>(), new Fmh.Core.Rng(3));
            Assert.All(groups, g => Assert.Equal(8, g.ClubIds.Count));
            Assert.Equal(ids, groups.SelectMany(g => g.ClubIds).ToHashSet());
            Assert.All(groups, g => Assert.All(g.ClubIds, id => Assert.Equal(g.Id, w.Clubs[id].LeagueId)));
        }
        // Geografisch: die westlichste Staffel enthält die Vereine mit den kleinsten Längengraden.
        groups[0].Format.Assignment = GroupAssignment.Geographic;
        Pyramid.AssignGroups(w, groups, new List<int>(), new Fmh.Core.Rng(3));
        Assert.True(groups[0].ClubIds.Max(id => w.Clubs[id].Longitude!.Value) <= groups[1].ClubIds.Min(id => w.Clubs[id].Longitude!.Value));
        groups[0].Format.Assignment = GroupAssignment.Fixed;
    }

    [Fact]
    public void ValidatorFindsUnbalancedPyramidsAndBrokenTransitions()
    {
        static CountryConfig Copy(CountryConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(System.Text.Json.JsonSerializer.Serialize(c, Json.Compact), Json.Compact)!;
        var tl = Data.Countries.Single(c => c.Code == "TL");

        var unbalanced = Copy(tl);
        unbalanced.Divisions.Single(d => d.Id == "TL2").Movements.Single(m => m.To == MovementDirection.Down).Positions = "10";
        var errors = PyramidValidator.Validate(WithCountries(unbalanced));
        Assert.Contains(errors, e => e.Contains("Absteiger aus Ebene 2"));

        // Reform: Ebene 4 ab 2028 mit zwei Staffeln à 12 Vereinen; ohne Übergangsregel fehlen nichts, mit einer falschen schon.
        var reform = Copy(tl);
        reform.ValidFrom = 2028;
        var tl4 = reform.Divisions.Single(d => d.Id == "TL4");
        tl4.Teams = 12;
        tl4.Groups.RemoveAt(2);
        tl4.Movements = new() { new MovementRule { Positions = "1", To = MovementDirection.Up } };
        reform.Divisions.Single(d => d.Id == "TL3").Movements.Single(m => m.To == MovementDirection.Down).Positions = "9-10";
        var before = Copy(tl);
        before.ValidTo = 2027;
        Assert.DoesNotContain(PyramidValidator.Validate(WithCountries(before, reform)), e => e.Contains("Ebene"));
        before.Divisions.Single(d => d.Id == "TL3").Movements.Add(new MovementRule { Positions = "7", To = MovementDirection.Down, OnlySeason = 2027 });
        Assert.Contains(PyramidValidator.Validate(WithCountries(before, reform)), e => e.Contains("Ebene 4 hat 24 Teams"));
    }

    private static GameData WithCountries(params CountryConfig[] countries) => new()
    {
        Countries = countries.ToList(),
        Clubs = Data.Clubs,
        Names = Data.Names,
    };

    /// <summary>Formatwechsel zur neuen Saison: Die 4. Liga hat ab 2027 zwei Staffeln à 12 Vereine; die Vereine werden neu verteilt.</summary>
    [Fact]
    public void FormatVersionSwitchesAtNewSeason()
    {
        static CountryConfig Copy(CountryConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(System.Text.Json.JsonSerializer.Serialize(c, Json.Compact), Json.Compact)!;
        var tl = Data.Countries.Single(c => c.Code == "TL");
        var before = Copy(tl);
        before.ValidTo = 2026;
        var reform = Copy(tl);
        reform.ValidFrom = 2027;
        var tl4 = reform.Divisions.Single(d => d.Id == "TL4");
        tl4.Teams = 12;
        tl4.Groups = new() { new GroupConfig { Id = "TL4W", Name = "Regionalliga West" }, new GroupConfig { Id = "TL4O", Name = "Regionalliga Ost" } };
        tl4.Assignment = GroupAssignment.Geographic;
        reform.Divisions.Single(d => d.Id == "TL3").Movements.Single(m => m.To == MovementDirection.Down).Positions = "9-10";
        var data = new GameData
        {
            Packages = Data.Packages, Countries = Data.Countries.Where(c => c.Code != "TL").Append(before).Append(reform).ToList(),
            Clubs = Data.Clubs, Names = Data.Names, Formations = Data.Formations, PositionProfiles = Data.PositionProfiles,
            Injuries = Data.Injuries, Roles = Data.Roles, Engine = Data.Engine, Buildings = Data.Buildings, Events = Data.Events,
            PressQuestions = Data.PressQuestions, Sponsors = Data.Sponsors, Languages = Data.Languages, DefaultLanguage = Data.DefaultLanguage,
        };
        data.UseLanguage("de");
        Assert.Empty(PyramidValidator.Validate(data));

        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = 78, ClubKey = "tl1-5", ManagerName = "Test", Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        var s = new GameSession(world, data, new TextMatchEngine());
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        Assert.Equal(2027, world.Country("TL").ValidFrom);
        var groups = world.Leagues.Where(l => l.Level == 4).ToList();
        Assert.Equal(new[] { "TL4W", "TL4O" }, groups.Select(g => g.Id));
        Assert.All(groups, g => Assert.Equal(12, g.ClubIds.Count));
        AssertConsistent(world);
        Assert.All(groups, g => Assert.NotEmpty(g.Fixtures));
    }

    [Fact]
    public void PlayoffWithSeveralPlacesIsRejected()
    {
        var tl = System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(
            System.Text.Json.JsonSerializer.Serialize(Data.Countries.Single(c => c.Code == "TL"), Json.Compact), Json.Compact)!;
        tl.Playoffs.Single(p => p.Id == "TL-AUF").Places = 2;
        Assert.Contains(PyramidValidator.Validate(WithCountries(tl)), e => e.Contains("unterstützt wird genau einer"));
    }

    /// <summary>Spielrecht gilt auch, wenn der Mensch eine zweite Mannschaft führt: Validierung meldet es, die Reparatur hält es ein.</summary>
    [Fact]
    public void ReserveEligibilityAppliesToHumanLineups()
    {
        var s = NewTestland();
        var w = s.World;
        var reserve = w.Clubs.Values.First(c => c.ReserveOf is not null);
        var start = new DateOnly(w.SeasonYear, w.Country("TL").Season.StartMonth, 1);
        var eligible = AiTactics.ReserveEligibility(reserve, w, Data)!;
        // Vier Ältere in die Startelf: einer zu viel.
        var older = w.SquadOf(reserve).Where(p => p.Age(start) > 23).Take(3).ToList();
        var extra = PlayerGenerator.Create(w, Data, new Fmh.Core.Rng(9), Position.MC, 8, 30, "TL");
        extra.ClubId = reserve.Id;
        w.Players[extra.Id] = extra;
        reserve.PlayerIds.Add(extra.Id);
        eligible = AiTactics.ReserveEligibility(reserve, w, Data)!;
        var outsider = older.Append(extra).First(p => !eligible(p));
        reserve.Tactic.Lineup[5] = outsider.Id;
        Assert.Contains("lineup.reserveAge", LineupSelector.Validate(reserve, w, Data, eligible));
        LineupSelector.Repair(reserve, w, Data, 7, eligible);
        Assert.DoesNotContain("lineup.reserveAge", LineupSelector.Validate(reserve, w, Data, eligible));
        Assert.True(reserve.Tactic.Lineup.Concat(reserve.Tactic.Bench.Select(b => (int?)b)).Count(id => id is { } v && w.Players[v].Age(start) > 23) <= 3);
    }

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

    /// <summary>Termine der zweiten Phase folgen dem Wochenrhythmus des Landes und lassen die Winterpause aus.</summary>
    [Fact]
    public void PhaseDatesSkipTheWinterBreak()
    {
        var season = new SeasonConfig { StartMonth = 7, WinterBreakStart = "12-21", WinterBreakEnd = "01-14", MatchDay = DayOfWeek.Saturday };
        var dates = ScheduleGenerator.WeeklyDates(season, 2026, new DateOnly(2026, 12, 15), 3);
        Assert.Equal(new[] { new DateOnly(2026, 12, 19), new DateOnly(2027, 1, 16), new DateOnly(2027, 1, 23) }, dates);
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
