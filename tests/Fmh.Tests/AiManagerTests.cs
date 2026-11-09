using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>KI-Manager: Erzeugung, Spielstand, Trainerkarussell, Wirkung auf die KI-Taktik.</summary>
public sealed class AiManagerTests
{
    private static List<Club> LeagueClubs(World w) =>
        w.Clubs.Values.Where(c => c.ReserveOf is null && w.FindLeague(c.LeagueId) is not null).ToList();

    [Fact]
    public void EveryLeagueClubHasAManagerAndAPoolExists()
    {
        var s = TestData.NewSession();
        var w = s.World;
        Assert.All(LeagueClubs(w), c => Assert.NotNull(s.ManagerOf(c)));
        Assert.Same(s.HumanManager, s.ManagerOf(s.HumanClub));
        var ai = w.Managers.Where(m => !m.Human).ToList();
        Assert.Equal(ai.Count, ai.Select(m => m.Id).Distinct().Count());
        Assert.True(s.FreeManagers.Count() >= ManagerGenerator.PoolTarget(w, s.Data.ManagerRules));
        Assert.All(ai.Where(m => m.ClubId is not null), m =>
        {
            Assert.NotNull(m.CurrentStation);
            Assert.InRange(m.Traits.Tactics, 1, 20);
            Assert.InRange(w.SeasonYear - m.Traits.BirthYear, 36, 65);
            Assert.Equal(w.Clubs[m.ClubId!.Value].CountryCode, m.Traits.Nationality);
        });
        Assert.True(ai.Select(m => m.Traits.Style).Distinct().Count() >= 4, "Alle Spielideen kommen vor");
        // Erzeugung mit eigenem Zufallsstrom: zwei Welten mit gleichem Seed sind gleich
        var again = TestData.NewSession().World;
        Assert.Equal(ai.Select(m => (m.Name, m.Reputation, m.Traits.Style)), again.Managers.Where(m => !m.Human).Select(m => (m.Name, m.Reputation, m.Traits.Style)));
    }

    [Fact]
    public void OldSavesWithoutAiManagersGetThemOnLoad()
    {
        var w = WorldGenerator.Create(TestData.Data, new NewGameOptions { Seed = 3, ClubKey = "nordheide", ManagerName = "Test" });
        foreach (var club in w.Clubs.Values.Where(c => c.ManagerId is { } id && !w.Managers.First(m => m.Id == id).Human)) club.ManagerId = null;
        w.Managers.RemoveAll(m => !m.Human);
        var s = new GameSession(w, TestData.Data, new TextMatchEngine());
        Assert.All(LeagueClubs(w), c => Assert.NotNull(s.ManagerOf(c)));
    }

    [Fact]
    public void TraitsSurviveSavingAndVersionSevenMigrates()
    {
        var s = TestData.NewSession();
        var path = Path.Combine(Path.GetTempPath(), $"fmh-mgr-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var before = s.World.Managers.First(m => !m.Human && m.ClubId is not null);
            var after = loaded.Managers.Single(m => m.Id == before.Id);
            Assert.Equal((before.Traits.Style, before.Traits.Tactics, before.Traits.BirthYear, before.Traits.Nationality),
                (after.Traits.Style, after.Traits.Tactics, after.Traits.BirthYear, after.Traits.Nationality));
            Assert.Equal(before.CurrentStation!.From, after.CurrentStation!.From);

            // Version 7 ohne Spalte traits: lädt mit Standardwerten und wird auf 8 angehoben
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE managers DROP COLUMN traits; UPDATE meta SET value = '7' WHERE key = 'schema.world';";
                cmd.ExecuteNonQuery();
            }
            var migrated = SqliteStore.Load(path);
            Assert.All(migrated.Managers, m => Assert.Equal(ManagerStyle.Balanced, m.Traits.Style));
            Assert.Equal("8", SqliteStore.ReadMetaTable(path)["schema.world"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ManagerStyleShiftsMentalityAndInstructions()
    {
        var s = TestData.NewSession();
        var w = s.World;
        var league = w.League(s.HumanClub.LeagueId);
        // Schwächster KI-Verein gegen den stärksten: klarer Außenseiter
        var ranked = league.ClubIds.Where(id => id != s.HumanClub.Id).Select(id => w.Clubs[id])
            .OrderBy(c => AiTactics.TeamStrength(c, w, s.Data)).ToList();
        var club = ranked[0];
        var opponent = ranked[^1];
        var manager = s.ManagerOf(club)!;
        Mentality With(ManagerStyle style)
        {
            manager.Traits.Style = style;
            AiTactics.Prepare(club, opponent, true, w, s.Data, s.BenchSize(club));
            return club.Tactic.Mentality;
        }
        var attacking = With(ManagerStyle.Attacking);
        var defensive = With(ManagerStyle.Defensive);
        // Haltung ist auf −2…2 begrenzt: offensiver Stil liegt darüber, außer eine Grenze ist erreicht
        Assert.True(attacking > defensive || attacking == Mentality.Attacking || defensive == Mentality.Defensive, $"{attacking} / {defensive}");
        With(ManagerStyle.Counter);
        Assert.True(club.Tactic.Instructions.Counter);
        With(ManagerStyle.Pressing);
        Assert.NotEqual(PressingHeight.Low, club.Tactic.Instructions.Pressing);
    }

    /// <summary>Eine Saison: Entlassungen kommen vor, keine Stelle bleibt frei, Bilanzen und Wechsel sind schlüssig.</summary>
    [Fact]
    public void CoachCarouselRunsThroughASeason()
    {
        var s = TestData.NewSession(seed: 11);
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 20_000);
        var w = s.World;
        Assert.All(LeagueClubs(w), c => Assert.NotNull(s.ManagerOf(c)));
        var changes = s.CoachChanges(w.SeasonYear);
        var sacked = changes.Where(c => c.Exit == "sacked").ToList();
        Assert.NotEmpty(sacked);
        Assert.InRange(sacked.Count, 1, LeagueClubs(w).Count / 2);
        Assert.All(sacked, c =>
        {
            Assert.True(c.Previous.Traits.Sackings >= 1);
            Assert.NotNull(c.Successor);
            Assert.NotEqual(c.Previous.Id, c.Successor!.Id);
        });
        // Bilanz: höchstens so viele Spiele wie der Verein bestritten hat
        foreach (var m in w.Managers.Where(m => !m.Human && m.CurrentStation is not null))
        {
            var st = m.CurrentStation!;
            Assert.Equal(st.Matches, st.Wins + st.Draws + st.Losses);
            var played = w.AllFixtures.Count(f => f.Result is not null && f.Involves(m.ClubId!.Value) && f.Date >= st.From);
            Assert.True(st.Matches <= played, $"{m.Name}: {st.Matches} > {played}");
        }
        // Meldung für Wechsel in der eigenen Liga
        if (sacked.Any(c => c.Club.LeagueId == s.HumanClub.LeagueId))
            Assert.Contains(s.Inbox, msg => msg.SubjectRef?.Key == "msg.managerSacked.subject");
    }

    /// <summary>Nach dem Rücktritt übernimmt ein Interimstrainer; beim Jobwechsel wird der Trainer des neuen Vereins frei.</summary>
    [Fact]
    public void InterimAfterResignationAndJobChangeFreesTheOtherManager()
    {
        var s = TestData.NewSession(seed: 5);
        var w = s.World;
        var old = s.HumanClub;
        s.Resign();
        s.FillManagerVacancies();
        var interim = s.ManagerOf(old);
        Assert.NotNull(interim);
        Assert.False(interim!.Human);

        var offer = s.HumanManager.Career.Offers.First();
        var target = w.Clubs[offer.ClubId];
        var displaced = s.ManagerOf(target)!;
        Assert.False(displaced.Human);
        Assert.Null(s.AcceptJob(offer));
        Assert.Same(interim, s.ManagerOf(old));
        Assert.Same(s.HumanManager, s.ManagerOf(target));
        Assert.Null(displaced.ClubId);
        Assert.Equal("moved", displaced.Career.Stations[^1].Exit);
    }

    /// <summary>Abweichende Grenze für Punktabzüge (Serie A) nur mit Begründung in den Ligazahlen.</summary>
    [Fact]
    public void DeductionLimitNeedsJustification()
    {
        var data = TestData.LoadAll();
        var reference = data.Countries.First(c => c.Code == "ITA").Finance.Divisions["ITA1"].Reference!;
        Assert.Equal(1.5, reference.MaxDeductionsPerSeason);
        Assert.DoesNotContain(DataValidator.Validate(data), e => e.Contains("'ITA1'"));
        reference.ToleranceNote = "";
        Assert.Contains(DataValidator.Validate(data), e => e.Contains("Ligazahlen 'ITA1': abweichende Toleranzen ohne Begründung"));
    }
}
