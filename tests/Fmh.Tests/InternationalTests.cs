using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Modules;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 8: Länderspielfenster, Nominierung durch die KI, Länderspiele, Belastung und Spielstand.</summary>
public sealed class InternationalTests
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

    private static GameSession AllCountries(ulong seed)
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = seed, ClubKey = "nordheide", ManagerName = "Test" });
        return new GameSession(world, data, new TextMatchEngine());
    }

    [Fact]
    public void WindowsComeFromTheDataAndStartOnMondays()
    {
        var windows = InternationalCalendar.Windows(TestData.Data.Internationals, 2026);
        Assert.Equal(TestData.Data.Internationals.Windows.Count, windows.Count);
        Assert.All(windows, w =>
        {
            Assert.Equal(DayOfWeek.Monday, w.Start.DayOfWeek);
            Assert.Equal(2, w.MatchDates.Count);
            Assert.All(w.MatchDates, d => Assert.True(w.Contains(d)));
        });
        Assert.Equal(new DateOnly(2026, 9, 7), windows[0].Start);
        Assert.Equal(2027, windows[^1].Start.Year);
    }

    [Fact]
    public void ClubFixturesAvoidTheWindowsInEveryCountry()
    {
        var s = AllCountries(811);
        var w = s.World;
        Assert.NotEmpty(w.InternationalWindows);
        var clash = w.AllFixtures.Where(f => w.InInternationalWindow(f.Date)).ToList();
        Assert.True(clash.Count == 0, string.Join(", ", clash.Take(5).Select(f => $"{f.CompetitionId} {f.Date}")));
    }

    [Fact]
    public void LeaguesStillEndInMayDespiteTheWindows()
    {
        // Ohne letzten Spieltag liefe die 3. Liga mit 38 Spieltagen bis Ende Juni; mit ihm gibt es Englische Wochen.
        var w = AllCountries(815).World;
        Assert.All(w.Leagues, l => Assert.True(l.Fixtures.Max(f => f.Date) <= new DateOnly(w.SeasonYear + 1, 5, 31), $"{l.Id} endet {l.Fixtures.Max(f => f.Date)}"));
        Assert.Contains(w.League("DE3").Fixtures, f => f.Date.DayOfWeek == DayOfWeek.Tuesday);
    }

    [Fact]
    public void LateSeasonEndStillStartsTheNextSeason()
    {
        // Endet ein Wettbewerb erst nach dem 1. Juli, beginnt die neue Saison sofort statt ein Jahr später.
        var s = TestData.NewSession(816);
        var w = s.World;
        TestData.PlayUntil(s, _ => w.SeasonFinished, 5000);
        var year = w.SeasonYear;
        w.Date = new DateOnly(year + 1, 7, 3);
        s.Advance(1);
        Assert.Equal(year + 1, w.SeasonYear);
        Assert.False(w.SeasonFinished);
    }

    [Fact]
    public void AiNominatesTheBestHealthyNationals()
    {
        var s = TestData.NewSession(812);
        var w = s.World;
        NationalSquads.Ensure(w, s.Data);
        var de = NationalSquads.TeamOf(w, "DE")!;
        var injured = w.Players.Values.Where(p => p.Nationalities.Contains("DE") && !p.IsYouth && p.ClubId is not null)
            .OrderByDescending(p => NationalSquads.Score(p, s.Data)).First();
        injured.State.Injury = new Injury { Type = "muscle", DaysRemaining = 10, TotalDays = 10 };

        var squad = NationalSquads.Nominate(w, s.Data, de);
        Assert.Equal(s.Data.Internationals.SquadSize, squad.Count);
        Assert.Equal(squad.Count, squad.Distinct().Count());
        Assert.DoesNotContain(injured.Id, squad);
        Assert.All(squad, id => Assert.Contains("DE", w.Players[id].Nationalities));
        Assert.True(squad.Count(id => w.Players[id].MainPosition == Position.GK) >= 2);
        // Der beste gesunde Feldspieler ist dabei, ein schwacher Spieler nicht
        var eligible = w.Players.Values.Where(p => NationalSquads.Eligible(p, "DE", w, s.Data)).OrderByDescending(p => NationalSquads.Score(p, s.Data)).ToList();
        Assert.Contains(eligible.First(p => p.MainPosition != Position.GK).Id, squad);
        Assert.DoesNotContain(eligible[^1].Id, squad);
        // Background-Kader sind reproduzierbar und gehören nicht zur Welt
        var bg = w.NationalTeams.First(t => t.Background);
        var a = NationalSquads.BackgroundSquad(w, s.Data, bg);
        var b = NationalSquads.BackgroundSquad(w, s.Data, bg);
        Assert.Equal(a.Select(p => (p.Id, p.Name)), b.Select(p => (p.Id, p.Name)));
        Assert.All(a, p => Assert.False(w.Players.ContainsKey(p.Id)));
    }

    [Fact]
    public void CalledUpPlayersMissNoClubGamesButReturnTired()
    {
        var s = AllCountries(813);
        var w = s.World;
        var window = w.InternationalWindows[0];
        AdvanceTo(s, window.Start.AddDays(1));
        var called = w.NationalTeams.Where(t => !t.Background).SelectMany(t => t.Squad).ToHashSet();
        Assert.True(called.Count >= 5 * 16, $"nur {called.Count} Nominierte");
        Assert.Contains(s.Inbox, m => m.SubjectRef?.Key == "intl.callup.subject" || w.NationalTeams.All(t => t.Squad.All(id => w.Players[id].ClubId != s.HumanClub.Id)));

        // Kein Vereinsspiel eines Nominierten im Fenster
        var clubs = called.Select(id => w.Players[id].ClubId!.Value).ToHashSet();
        Assert.DoesNotContain(w.AllFixtures, f => window.Contains(f.Date) && (clubs.Contains(f.HomeId) || clubs.Contains(f.AwayId)));

        AdvanceTo(s, window.End.AddDays(1));
        var fixtures = w.InternationalFixtures.Where(f => f.WindowId == window.Id).ToList();
        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, f => Assert.NotNull(f.Result));
        Assert.All(w.NationalTeams, t => Assert.Empty(t.Squad));
        var played = called.Where(id => w.Caps.TryGetValue(id, out var c) && c.Caps > 0).ToList();
        Assert.True(played.Count >= fixtures.Count * 11);
        Assert.All(played, id => Assert.Equal(w.Players[id].Nationalities[0], w.Caps[id].Nation));

        // Belastung: Nationalspieler sind nach der Rückkehr müder als ihre Mannschaftskameraden
        var mates = w.Players.Values.Where(p => p.ClubId is { } c && clubs.Contains(c) && !called.Contains(p.Id) && !p.IsYouth && p.State.Injury is null).ToList();
        var back = played.Select(id => w.Players[id]).Where(p => p.State.Injury is null).ToList();
        Assert.True(back.Average(p => p.State.Condition) < mates.Average(p => p.State.Condition) - 3,
            $"Nationalspieler {back.Average(p => p.State.Condition):0.0}, übrige {mates.Average(p => p.State.Condition):0.0}");
    }

    [Fact]
    public void DualNationalsJoinOnlyOneSquadAndSkipClubTraining()
    {
        var s = AllCountries(817);
        var w = s.World;
        var window = w.InternationalWindows[0];
        // Der beste Spieler aus Deutschland bekommt zusätzlich die englische Staatsangehörigkeit.
        var star = w.Players.Values.Where(p => NationalSquads.Eligible(p, "DE", w, s.Data)).OrderByDescending(p => NationalSquads.Score(p, s.Data)).First();
        star.Nationalities.Add("ENG");
        AdvanceTo(s, window.Start.AddDays(1));
        Assert.Equal(1, w.NationalTeams.Count(t => t.Squad.Contains(star.Id)));

        // Nominierte trainieren nicht im Verein: ihr Trainingsfortschritt bleibt im Fenster stehen.
        var called = w.NationalTeams.SelectMany(t => t.Squad).Select(id => w.Players[id]).ToList();
        static double Progress(Player p) => p.Training.Physical + p.Training.Technical + p.Training.Mental + p.Training.Goalkeeping;
        var before = called.ToDictionary(p => p.Id, Progress);
        AdvanceTo(s, window.Start.AddDays(2));
        Assert.All(called, p => Assert.Equal(before[p.Id], Progress(p)));
    }

    [Fact]
    public void InternationalsSurviveSavingAndOldSavesLoad()
    {
        var s = TestData.NewSession(814);
        var w = s.World;
        AdvanceTo(s, w.InternationalWindows[0].End.AddDays(1));
        Assert.NotEmpty(w.InternationalFixtures);
        var path = Path.Combine(Path.GetTempPath(), $"fmh-intl-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(w, path);
            var loaded = SqliteStore.Load(path);
            Assert.Equal(w.NationalTeams.Select(t => (t.Code, t.Played, t.GoalsFor)), loaded.NationalTeams.Select(t => (t.Code, t.Played, t.GoalsFor)));
            Assert.Equal(w.InternationalFixtures.Select(f => (f.Id, f.Date, f.Result!.HomeGoals)), loaded.InternationalFixtures.Select(f => (f.Id, f.Date, f.Result!.HomeGoals)));
            Assert.Equal(w.InternationalWindows.Select(x => x.Start), loaded.InternationalWindows.Select(x => x.Start));
            Assert.Equal(w.Caps.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value.Caps, kv.Value.Nation)), loaded.Caps.Select(kv => (kv.Key, kv.Value.Caps, kv.Value.Nation)));

            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE national_teams; DROP TABLE international_fixtures; DROP TABLE international_windows; DROP TABLE international_caps; DELETE FROM meta WHERE key = 'schema.internationals';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Empty(old.InternationalWindows);
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.internationals"]);
            var session = new GameSession(old, TestData.Data, new TextMatchEngine());
            session.Advance(3);
            Assert.Empty(old.InternationalFixtures);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
