using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 9: Testspiele – Vereinbarung, keine Wettbewerbsstatistik, Fitness und Vertrautheit, Trainingslager.</summary>
public sealed class FriendlyTests
{
    private static void AdvanceTo(GameSession s, DateOnly date)
    {
        for (var i = 0; i < 2_000 && s.World.Date < date; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
    }

    /// <summary>Vergleichbarer Gegner aus der eigenen Liga.</summary>
    private static Club Opponent(GameSession s) => s.World.LeagueOf(s.HumanClub).ClubIds.Select(id => s.World.Clubs[id])
        .Where(c => c.Id != s.HumanClub.Id).OrderBy(c => Math.Abs(c.Reputation - s.HumanClub.Reputation)).First();

    [Fact]
    public void ArrangingFollowsTheRules()
    {
        var s = TestData.NewSession(901);
        var w = s.World;
        var rules = s.Data.Friendlies;
        var opp = Opponent(s);
        var date = s.FriendlyDates(opp).First();
        Assert.Equal("friendly.error.share", s.ArrangeFriendly(opp, date, true, 33));
        Assert.Equal("friendly.error.date", s.ArrangeFriendly(opp, w.Date, true, 100));
        Assert.Equal("friendly.error.window", s.ArrangeFriendly(opp, w.InternationalWindows[0].Start.AddDays(1), true, 100));
        var league = w.AllFixtures.Where(f => f.Involves(s.HumanClub.Id) && f.Date > w.Date).Min(f => f.Date);
        Assert.Equal("friendly.error.busy", s.ArrangeFriendly(opp, league.AddDays(-1), true, 100));
        Assert.DoesNotContain(s.FriendlyDates(opp), d => Math.Abs(d.DayNumber - league.DayNumber) <= rules.MinRestDays);

        // Ein deutlich bekannterer Verein kommt nicht, sagt aber zu einem Auswärtsspiel bis zum doppelten Abstand zu
        var big = s.FriendlyCandidates().First(c => c.Reputation - s.HumanClub.Reputation is var gap && gap > rules.AcceptReputationGap && gap <= 2 * rules.AcceptReputationGap);
        var bigDate = s.FriendlyDates(big).First();
        Assert.Equal("friendly.error.declined", s.ArrangeFriendly(big, bigDate, true, 100));
        Assert.Null(s.ArrangeFriendly(big, bigDate, false, 50));
        Assert.Null(s.ArrangeFriendly(opp, s.FriendlyDates(opp).First(), true, 70));
        Assert.Equal(2, s.OwnFriendlies.Count());
        Assert.All(s.OwnFriendlies, f => Assert.Equal(CompetitionKind.Friendly, f.Fixture.Kind));
        Assert.Equal(s.OwnFriendlies.Count(), s.OwnFriendlies.Select(f => f.Fixture.Id).Distinct().Count());
        s.CancelFriendly(s.OwnFriendlies.First());
        Assert.Single(s.OwnFriendlies);
    }

    [Fact]
    public void FriendliesDoNotCountButImproveFitnessAndFamiliarity()
    {
        var s = TestData.NewSession(902);
        var w = s.World;
        var club = s.HumanClub;
        var opp = Opponent(s);
        var date = s.FriendlyDates(opp).First();
        Assert.Null(s.ArrangeFriendly(opp, date, true, 50));
        AdvanceTo(s, date.AddDays(-1));
        var squad = w.SquadOf(club).ToDictionary(p => p.Id, p => (p.SeasonStats.Appearances, p.State.RecentGrades.Count, p.State.YellowCardsAccumulated, p.Training.Physical));
        var familiarity = s.Familiarity(club, club.Tactic.FormationId);
        var table = LeagueTable.Compute(w.LeagueOf(club)).Select(r => (r.ClubId, r.Played)).ToList();
        var tickets = w.Transactions.Count(t => t.Type == TransactionType.Tickets);
        var oppTickets = opp.Finance.Season.GetValueOrDefault(TransactionType.Tickets);
        AdvanceTo(s, date.AddDays(1));

        var f = s.OwnFriendlies.Single().Fixture;
        Assert.NotNull(f.Result);
        Assert.True(f.Result!.Attendance > 0);
        // Keine Wettbewerbsstatistik: Einsätze, Noten, Gelbkartenkonto und Tabelle unverändert
        Assert.All(w.SquadOf(club).Where(p => squad.ContainsKey(p.Id)), p =>
        {
            Assert.Equal(squad[p.Id].Appearances, p.SeasonStats.Appearances);
            Assert.Equal(squad[p.Id].Count, p.State.RecentGrades.Count);
            Assert.Equal(squad[p.Id].YellowCardsAccumulated, p.State.YellowCardsAccumulated);
        });
        Assert.Equal(table, LeagueTable.Compute(w.LeagueOf(club)).Select(r => (r.ClubId, r.Played)).ToList());
        // Fitness (Physis-Training) und Vertrautheit steigen
        Assert.Contains(w.SquadOf(club), p => squad.TryGetValue(p.Id, out var b) && p.Training.Physical > b.Physical + 0.3);
        Assert.True(s.Familiarity(club, club.Tactic.FormationId) > familiarity);
        // Zuschauereinnahmen werden geteilt (Hälfte für den Gast; Einzelbuchungen gibt es nur beim eigenen Verein)
        var own = w.Transactions.Where(t => t.Type == TransactionType.Tickets).Skip(tickets).Single(t => t.TextRef?.Key == "tx.friendly");
        Assert.True(own.Amount > 0);
        Assert.True(Math.Abs(opp.Finance.Season.GetValueOrDefault(TransactionType.Tickets) - oppTickets - own.Amount) <= 1);
    }

    [Fact]
    public void CampFriendlyIsNeutralWithoutGateAndStronger()
    {
        var s = TestData.NewSession(903);
        var w = s.World;
        var club = s.HumanClub;
        var opp = Opponent(s);
        var start = s.FriendlyDates(opp).First(d => d > w.Date.AddDays(2));
        Assert.Null(s.BookCamp(CampGoal.Fitness, start.AddDays(-1), 5, "Alpen"));
        Assert.Null(s.ArrangeFriendly(opp, start.AddDays(1), true, 100));
        var friendly = s.OwnFriendlies.Single();
        Assert.True(friendly.Camp);
        AdvanceTo(s, start.AddDays(2));
        Assert.NotNull(friendly.Fixture.Result);
        Assert.Equal(0, friendly.Fixture.Result!.Attendance);
        Assert.DoesNotContain(w.Transactions, t => t.TextRef?.Key == "tx.friendly");
    }

    [Fact]
    public void LaterCompetitiveFixtureCancelsTheFriendly()
    {
        var s = TestData.NewSession(905);
        var w = s.World;
        var opp = Opponent(s);
        var date = s.FriendlyDates(opp).First(d => d > w.Date.AddDays(3));
        Assert.Null(s.ArrangeFriendly(opp, date, true, 100));
        // Pflichtspiel des Gegners wird nachträglich auf denselben Tag gelegt (wie bei einer Pokalauslosung)
        var cupLike = w.AllFixtures.First(f => f.Involves(opp.Id) && f.Date > date);
        cupLike.Date = date;
        AdvanceTo(s, w.Date.AddDays(1));
        Assert.Empty(s.OwnFriendlies);
        Assert.Contains(s.Inbox, m => m.SubjectRef?.Key == "friendly.cancelled.subject");
    }

    [Fact]
    public void FriendliesSurviveSavingAndOldSavesLoad()
    {
        var s = TestData.NewSession(904);
        var opp = Opponent(s);
        Assert.Null(s.ArrangeFriendly(opp, s.FriendlyDates(opp).First(), false, 70));
        var path = Path.Combine(Path.GetTempPath(), $"fmh-friendly-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var a = s.World.Friendlies.Single();
            var b = loaded.Friendlies.Single();
            Assert.Equal((a.Fixture.Id, a.Fixture.Date, a.Fixture.HomeId, a.HomeSharePercent, a.Fixture.Kind), (b.Fixture.Id, b.Fixture.Date, b.Fixture.HomeId, b.HomeSharePercent, b.Fixture.Kind));
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.friendlies"]);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE friendlies; DELETE FROM meta WHERE key = 'schema.friendlies';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Empty(old.Friendlies);
            new GameSession(old, TestData.Data, new TextMatchEngine()).Advance(2);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
