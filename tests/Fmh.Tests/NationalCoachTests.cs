using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 8c: Nationaltrainer – KI-Trainer, Angebot an den Manager, Vormerkliste, Abrechnung nach dem Turnier.</summary>
public sealed class NationalCoachTests
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

    /// <summary>Manager mit genug Ruf bekommt den freien Posten von Deutschland angeboten und nimmt an.</summary>
    private static (GameSession s, NationalTeam de) HumanCoach(ulong seed)
    {
        var s = TestData.NewSession(seed);
        AdvanceTo(s, s.World.Date.AddDays(1));
        var de = NationalSquads.TeamOf(s.World, "DE")!;
        s.HumanManager.Reputation = s.Data.Internationals.Coach.OfferReputation;
        de.CoachId = null;
        AdvanceTo(s, s.World.Date.AddDays(1));
        Assert.Same(de, s.PendingNationalOffer);
        Assert.Null(s.AcceptNationalJob(de));
        return (s, de);
    }

    [Fact]
    public void AiCoachesTakeOverAndStrongManagersGetOffers()
    {
        var s = TestData.NewSession(831);
        AdvanceTo(s, s.World.Date.AddDays(1));
        var w = s.World;
        var real = w.NationalTeams.Where(t => !t.Background).ToList();
        Assert.NotEmpty(real);
        // Zu wenig Ruf: kein Angebot, ein vereinsloser KI-Manager übernimmt
        Assert.Null(s.PendingNationalOffer);
        Assert.All(real, t =>
        {
            var coach = w.Managers.Single(m => m.Id == t.CoachId);
            Assert.False(coach.Human);
            Assert.Null(coach.ClubId);
            Assert.Contains(t.CoachTarget, new[] { "Q", "KO4", "KO2" });
            Assert.Equal(2028, t.CoachUntil);
        });
        Assert.Equal(real.Count, real.Select(t => t.CoachId).Distinct().Count());

        var (h, de) = HumanCoach(832);
        Assert.Same(de, h.HumanNation);
        Assert.Equal(h.HumanManager.Id, de.CoachId);
        Assert.NotNull(h.HumanManager.ClubId); // Verein bleibt
        Assert.Equal(h.Data.Internationals.SquadSize, de.PlannedSquad.Count);
        Assert.Contains(h.Inbox, m => m.Action == MessageAction.NationalJobOffer && m.Resolved);
        h.ResignNationalJob();
        Assert.Null(h.HumanNation);
        Assert.NotNull(de.CoachId);
    }

    [Fact]
    public void ShortlistAndFormationOfTheHumanCoachAreUsed()
    {
        var (s, de) = HumanCoach(833);
        var w = s.World;
        // Ein schwacher, aber spielberechtigter Spieler kommt nur über die Vormerkliste in den Kader
        var candidates = s.NationalCandidates(de);
        var weak = candidates[^1];
        s.SetNationalSquad(new[] { weak.Id }.Concat(candidates.Take(22).Select(p => p.Id)));
        s.SetNationalFormation("433");
        Assert.Equal("433", de.Formation);
        AdvanceTo(s, w.InternationalWindows[0].Start.AddDays(1));
        Assert.Contains(weak.Id, de.Squad);
        Assert.Equal(s.Data.Internationals.SquadSize, de.Squad.Count);
        Assert.True(w.Players[weak.Id].State.OnNationalDuty);
    }

    [Fact]
    public void TargetsDecideWhetherTheCoachStays()
    {
        var (s, de) = HumanCoach(834);
        var t = new InternationalTournament { Id = "EM-2028", Qualified = { "DE", "NRD" } };
        s.World.InternationalFixtures.Add(new InternationalFixture { TournamentId = t.Id, Stage = "KO4", HomeCode = "DE", AwayCode = "NRD" });
        de.CoachTarget = "Q";
        Assert.True(s.TargetReached(t, de));
        de.CoachTarget = "KO4";
        Assert.True(s.TargetReached(t, de));
        de.CoachTarget = "KO2";
        Assert.False(s.TargetReached(t, de));
        t.Qualified.Remove("DE");
        de.CoachTarget = "Q";
        Assert.False(s.TargetReached(t, de));
    }

    [Fact]
    public void CoachSurvivesSavingAndOldSavesGetCoaches()
    {
        var (s, de) = HumanCoach(835);
        var path = Path.Combine(Path.GetTempPath(), $"fmh-coach-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var lde = NationalSquads.TeamOf(loaded, "DE")!;
            Assert.Equal(de.CoachId, lde.CoachId);
            Assert.Equal(de.PlannedSquad, lde.PlannedSquad);
            Assert.Equal(de.CoachTarget, lde.CoachTarget);
            Assert.Equal("8", SqliteStore.ReadMetaTable(path)["schema.internationals"]);

            // Spielstand der Version 7: Team-Daten ohne Trainerfelder → Posten werden neu besetzt
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "UPDATE national_teams SET data = json_remove(data, '$.coachId', '$.coachTarget', '$.coachUntil', '$.plannedSquad');"
                                  + "UPDATE meta SET value = '7' WHERE key = 'schema.internationals';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.All(old.NationalTeams, t => Assert.Null(t.CoachId));
            var session = new GameSession(old, TestData.Data, new TextMatchEngine());
            AdvanceTo(session, old.Date.AddDays(1));
            // Mit genug Ruf geht der freie Posten zuerst als Angebot an den Manager, sonst an einen KI-Manager.
            Assert.All(old.NationalTeams.Where(t => !t.Background), t => Assert.True(t.CoachId is not null || t.OfferExpires is not null));
            Assert.NotNull(session.PendingNationalOffer);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
