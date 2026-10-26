using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

public class SessionTests
{
    [Fact]
    public void AdvanceStopsOnFirstOwnMatchday()
    {
        var s = TestData.NewSession();
        // Erster Pflichttermin ist die 1. Pokalrunde.
        var first = s.AllFixtures.Where(f => f.Involves(s.HumanClub.Id)).Min(f => f.Date);
        var r = s.Advance();
        // Vor dem ersten Spiel kommen Saisonziel, Sponsorenangebote und Pressetermine.
        while (r.Reason is StopReason.Decision or StopReason.Messages) { s.DelegateDecisions(); r = s.Advance(); }
        Assert.Equal(StopReason.HumanMatch, r.Reason);
        Assert.Equal(first, s.World.Date);
        Assert.NotNull(r.Fixture);
        Assert.Same(r.Fixture, s.PendingHumanFixture);
        // Ohne gespieltes Spiel bleibt der Kalender stehen.
        Assert.Equal(StopReason.HumanMatch, s.Advance().Reason);
        Assert.Equal(first, s.World.Date);
    }

    [Fact]
    public void FullSeasonCompletesWithPromotionAndRelegation()
    {
        var s = TestData.NewSession(11);
        var l1 = s.World.Leagues.First(l => l.Level == 1);
        var l2 = s.World.Leagues.First(l => l.Level == 2);

        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd);
        Assert.True(s.World.Leagues.All(l => l.IsComplete));
        Assert.All(s.World.Leagues, l => Assert.Equal(l.ClubIds.Count * (l.ClubIds.Count - 1), l.Fixtures.Count));
        Assert.Equal(2, s.World.History.Count(h => s.World.FindLeague(h.LeagueId) is not null));
        Assert.Contains(s.World.Cups, c => c.Kind == CompetitionKind.Cup && c.IsFinished);
        var playoff = Assert.Single(s.World.Cups, c => c.Kind == CompetitionKind.Playoff);
        Assert.True(playoff.IsFinished);
        Assert.Contains(s.Inbox, m => m.Type == MessageType.Season);

        // Zwei direkte Auf- und Absteiger, dazu der Verlierer bzw. Sieger der Relegation (16. gegen 3.).
        var relegated = LeagueTable.Compute(l1).TakeLast(2).Select(r => r.ClubId).ToList();
        var promoted = LeagueTable.Compute(l2).Take(2).Select(r => r.ClubId).ToList();
        var upper16 = LeagueTable.Compute(l1).First(r => r.Position == 16).ClubId;
        var lower3 = LeagueTable.Compute(l2).First(r => r.Position == 3).ClubId;
        Assert.Contains(upper16, playoff.ParticipantIds);
        Assert.Contains(lower3, playoff.ParticipantIds);
        if (playoff.WinnerId == lower3) { promoted.Add(lower3); relegated.Add(upper16); }
        var year = s.World.SeasonYear;

        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason || s.World.SeasonYear > year);
        Assert.Equal(year + 1, s.World.SeasonYear);
        Assert.All(relegated, id => Assert.Contains(id, l2.ClubIds));
        Assert.All(promoted, id => Assert.Contains(id, l1.ClubIds));
        Assert.All(s.World.Leagues, l => Assert.Equal(18, l.ClubIds.Count));
        Assert.All(s.World.Leagues, l => Assert.All(l.Fixtures, f => Assert.Null(f.Result)));
        Assert.All(s.World.Clubs.Values, c => Assert.Empty(LineupSelector.Validate(c, s.World, TestData.Data)));
        // Der menschliche Verein verlängert in diesem Test nie Verträge; das Sicherheitsnetz hält ihn bei 16.
        Assert.All(s.World.Clubs.Values, c => Assert.InRange(c.PlayerIds.Count, c == s.HumanClub ? 16 : 20, s.World.Country(c.CountryCode).Squad.MaxSquad));
        Assert.Contains(s.World.Cups, c => c.Kind == CompetitionKind.Cup && c.SeasonYear == year + 1 && c.Rounds[0].Drawn);
    }

    [Fact]
    public void MatchResultsUpdatePlayersAndDiscipline()
    {
        var s = TestData.NewSession(3);
        TestData.PlayUntil(s, _ => s.World.LeagueOf(s.HumanClub).CurrentMatchday >= 12);
        var players = s.World.Players.Values.Where(p => p.ClubId is not null).ToList();
        Assert.Contains(players, p => p.SeasonStats.Goals > 0);
        Assert.Contains(players, p => p.SeasonStats.YellowCards > 0);
        Assert.Contains(players, p => p.State.RecentGrades.Count > 0);
        Assert.Contains(players, p => p.State.Injury is not null || p.History.Count > 0 || s.Inbox.Any(m => m.Type == MessageType.Medical));
        var totalGoals = s.AllFixtures.Where(f => f.Result is not null).Sum(f => f.Result!.HomeGoals + f.Result!.AwayGoals);
        // Nach einem Vereinswechsel liegt die Statistik des alten Vereins in der Historie derselben Saison.
        var season = s.World.SeasonYear;
        Assert.Equal(totalGoals, s.World.Players.Values.Sum(p =>
            (p.SeasonStats.SeasonYear == season ? p.SeasonStats.Goals : 0) + p.History.Where(h => h.SeasonYear == season).Sum(h => h.Goals)));
    }

    [Fact]
    public void SaveLoadRoundTripContinuesIdentically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-test-" + Guid.NewGuid());
        try
        {
            var a = TestData.NewSession(99);
            TestData.PlayUntil(a, _ => a.World.LeagueOf(a.HumanClub).CurrentMatchday >= 5);
            a.SyncRng();
            var path = Path.Combine(dir, "test" + SaveGame.Extension);
            SaveGame.Save(a.World, path);

            var b = new GameSession(SaveGame.Load(path), TestData.Data, new TextMatchEngine());
            Assert.Equal(a.World.Date, b.World.Date);
            Assert.Equal(a.World.Players.Count, b.World.Players.Count);
            Assert.Equal(a.HumanClub.Tactic.Lineup, b.HumanClub.Tactic.Lineup);
            var pa = a.World.Players.Values.First();
            var pb = b.World.Players[pa.Id];
            foreach (var attr in Fmh.Core.Model.AttrInfo.All) Assert.Equal(pa.Attributes[attr], pb.Attributes[attr]);

            TestData.PlayUntil(a, _ => a.World.LeagueOf(a.HumanClub).CurrentMatchday >= 9);
            TestData.PlayUntil(b, _ => b.World.LeagueOf(b.HumanClub).CurrentMatchday >= 9);
            var ra = a.AllFixtures.Where(f => f.Result is not null).Select(f => $"{f.Id}:{f.Result!.HomeGoals}-{f.Result.AwayGoals}");
            var rb = b.AllFixtures.Where(f => f.Result is not null).Select(f => $"{f.Id}:{f.Result!.HomeGoals}-{f.Result.AwayGoals}");
            Assert.Equal(ra, rb);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AutosaveRotatesThroughSlots()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-test-" + Guid.NewGuid());
        try
        {
            var s = TestData.NewSession();
            var paths = Enumerable.Range(0, 4).Select(_ => SaveGame.Autosave(s.World, dir)).ToList();
            Assert.Equal(3, paths.Distinct().Count());
            Assert.Equal(paths[0], paths[3]);
            Assert.Equal(3, SaveGame.List(dir).Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SaveIsAQueryableSqliteDatabase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-test-" + Guid.NewGuid());
        try
        {
            var s = TestData.NewSession();
            var path = Path.Combine(dir, "db" + SaveGame.Extension);
            SaveGame.Save(s.World, path);
            using var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM players; ";
            Assert.Equal((long)s.World.Players.Count, (long)cmd.ExecuteScalar()!);
            cmd.CommandText = "SELECT MAX(a_Finishing) FROM players WHERE main_position = 'ST'";
            Assert.InRange((long)cmd.ExecuteScalar()!, 10, 20);
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'club_name'";
            Assert.Equal(s.HumanClub.Name, (string)cmd.ExecuteScalar()!);
            con.Close();

            var info = Assert.Single(SaveGame.List(dir));
            Assert.Equal(s.HumanClub.Name, info.ClubName);
            Assert.Equal(s.World.Date, info.GameDate);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NewerSaveVersionIsRejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-test-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(dir, "x" + SaveGame.Extension);
            SaveGame.Save(TestData.NewSession().World, path);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "UPDATE meta SET value = '999' WHERE key = 'schema_version'";
                cmd.ExecuteNonQuery();
            }
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(path));
            File.WriteAllText(path, "kein Spielstand");
            Assert.ThrowsAny<Exception>(() => SaveGame.Load(path));
            Assert.False(Assert.Single(SaveGame.List(dir)).Valid);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
