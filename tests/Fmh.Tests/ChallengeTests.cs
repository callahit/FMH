using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 12a: Herausforderungen als Daten, Zielprüfung, Schnellsaison und Speicherung.</summary>
public sealed class ChallengeTests
{
    private static GameSession Challenge(string id, ulong seed)
    {
        var rule = Challenges.Find(TestData.Data, id)!;
        var s = TestData.NewSession(seed, Challenges.PickClub(TestData.Data, rule)!.Key);
        Assert.Null(s.StartChallenge(id));
        return s;
    }

    [Fact]
    public void EveryChallengePicksAClubFromTheData()
    {
        var data = TestData.Data;
        Assert.NotEmpty(data.Challenges.Challenges);
        foreach (var rule in data.Challenges.Challenges)
        {
            var club = Challenges.PickClub(data, rule);
            Assert.NotNull(club);
            Assert.True(data.Texts.Has("challenge." + rule.Id + ".name"), rule.Id);
            Assert.True(data.Texts.Has("challenge." + rule.Id + ".info"), rule.Id);
            Assert.True(data.Texts.Has("challenge.goal." + rule.Goal.Type), rule.Goal.Type);
        }
        var survival = Challenges.Find(data, "survival")!;
        var top = data.Clubs.Where(c => c.Division == "DE1" && c.ReserveOf is null).ToList();
        Assert.Equal(top.Min(c => c.Reputation), Challenges.PickClub(data, survival)!.Reputation);

        // Falscher Verein wird abgelehnt, die Startbedingungen gelten nur mit dem vorgegebenen.
        var other = TestData.NewSession(1201);
        Assert.Equal("challenge.error.club", other.StartChallenge("survival"));
        Assert.Null(other.Mode);
        var austerity = Challenges.Find(data, "austerity")!;
        var s = TestData.NewSession(1201, Challenges.PickClub(data, austerity)!.Key);
        var balance = s.HumanClub.Budget.Balance;
        Assert.Null(s.StartChallenge("austerity"));
        Assert.Equal((long)Math.Round(balance * austerity.BalanceFactor), s.HumanClub.Budget.Balance);
        Assert.Equal(0, s.HumanClub.Budget.TransferBudget);
        Assert.Equal((GameMode.Challenge, 1, ChallengeOutcome.Open), (s.Mode!.Mode, s.Mode.StartLevel, s.Mode.Outcome));
    }

    [Fact]
    public void SeasonGoalIsDecidedAtSeasonEndAndLeagueGoalAtTheNextSeasonStart()
    {
        // Saisonziel: Platz 9 oder besser, entschieden am letzten Spieltag
        var a = Challenge("austerity", 1202);
        TestData.PlayUntil(a, r => r.Reason == StopReason.SeasonEnd, 50_000);
        var row = a.World.ClubSeasons.Single(c => c.ClubId == a.HumanClub.Id && c.SeasonYear == a.World.SeasonYear);
        var expected = row.Position <= 9 ? ChallengeOutcome.Won : ChallengeOutcome.Lost;
        if (a.Mode!.Reason == "challenge.reason.sacked") expected = ChallengeOutcome.Lost;
        Assert.Equal(expected, a.Mode.Outcome);
        Assert.Equal(a.World.SeasonYear, a.Mode.DecidedSeason);
        Assert.Contains(a.World.Messages, m => m.SubjectRef?.Key is "challenge.won.subject" or "challenge.lost.subject");

        // Ligaziel: Klassenerhalt, entschieden erst nach Auf- und Abstieg
        var s = Challenge("survival", 1203);
        var start = s.World.SeasonYear;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 50_000);
        if (s.Mode!.Outcome == ChallengeOutcome.Open)
        {
            TestData.PlayUntil(s, _ => s.World.SeasonYear > start, 50_000);
            var stayedUp = Challenges.Level(s.World, s.Mode.ClubId) <= s.Mode.StartLevel;
            Assert.Equal(stayedUp ? ChallengeOutcome.Won : ChallengeOutcome.Lost, s.Mode.Outcome);
            Assert.Equal(start, s.Mode.DecidedSeason);
        }
        else Assert.Equal("challenge.reason.sacked", s.Mode.Reason);
        s.ContinueAsCareer();
        Assert.Null(s.Mode);
    }

    [Fact]
    public void QuickSeasonRunsWithoutStopsAndEndsAsCareer()
    {
        var s = TestData.NewSession(1204);
        s.StartQuickSeason();
        var start = s.World.SeasonYear;
        for (var i = 0; i < 5_000 && s.Mode is not null; i++)
        {
            var r = s.Advance();
            if (s.Mode is null) break;
            // Ohne Entlassung hält die Schnellsaison weder für eigene Spiele noch für Entscheidungen an.
            if (!s.AwaitingNewJob) Assert.NotEqual(StopReason.HumanMatch, r.Reason);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
        }
        Assert.Null(s.Mode);
        Assert.True(s.World.SeasonFinished);
        Assert.Equal(start, s.World.SeasonYear);
        Assert.Contains(s.World.Messages, m => m.SubjectRef?.Key == "quickSeason.done.subject");
        Assert.True(s.World.AllFixtures.Where(f => f.Involves(s.HumanClub.Id)).All(f => f.Result is not null || f.Date > s.World.Date));
    }

    [Fact]
    public void ModeSurvivesSavingAndOldSavesAreCareers()
    {
        var s = Challenge("title", 1205);
        var path = Path.Combine(Path.GetTempPath(), $"fmh-challenge-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            Assert.Equal((GameMode.Challenge, "title", s.HumanClub.Id, s.World.SeasonYear, ChallengeOutcome.Open),
                (loaded.Mode!.Mode, loaded.Mode.ChallengeId, loaded.Mode.ClubId, loaded.Mode.StartSeason, loaded.Mode.Outcome));
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.challenge"]);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE game_mode; DELETE FROM meta WHERE key = 'schema.challenge';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Null(old.Mode);
            new GameSession(old, TestData.Data, new TextMatchEngine()).Advance(2);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
