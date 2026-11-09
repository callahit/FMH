using Fmh.Core.Model;
using Fmh.Core.Modules;
using Fmh.Core.Simulation;

namespace Fmh.Tests;

/// <summary>AP 7: Schonfrist zum Saisonstart, Gehaltsvergleich, Wechselwunsch, Vertrauen, Hierarchie und Mannschaftsrat.</summary>
public class RelationsHierarchyTests
{
    private static RelationsModule Rel(Fmh.Core.Game.GameSession s) => s.Modules.OfType<RelationsModule>().Single();

    private static void Monday(Fmh.Core.Game.GameSession s)
    {
        while (s.World.Date.DayOfWeek != DayOfWeek.Monday) s.World.Date = s.World.Date.AddDays(1);
        Rel(s).OnDay(new DayContext(s.World.Date, Array.Empty<Fixture>()));
    }

    [Fact]
    public void NoPlayingTimeComplaintsBeforeTheGracePeriodAndWageComparedWithSameStatus()
    {
        var s = TestData.NewSession(201);
        var club = s.HumanClub;
        Assert.True(s.World.LeagueOf(club).CurrentMatchday < s.Data.Relations.GraceMatchdays);
        var star = s.World.SquadOf(club).First(p => p.Contract is not null);
        star.Contract!.Status = SquadStatus.Star;
        star.SeasonStats.Minutes = 0;
        s.RecalculateSatisfaction(star);
        Assert.DoesNotContain("sat.reason.playingTime.low", s.PlayerRelationsOf(star).ReasonKeys);

        // Ein Ergänzungsspieler mit kleinem Gehalt misst sich an anderen Ergänzungsspielern, nicht am Kaderschnitt
        var backups = s.World.SquadOf(club).Where(p => p.Contract is not null && !p.IsYouth).OrderBy(p => p.Contract!.WeeklyWage).Take(5).ToList();
        foreach (var other in s.World.SquadOf(club).Where(p => p.Contract?.Status == SquadStatus.Backup)) other.Contract!.Status = SquadStatus.Rotation;
        foreach (var b in backups) { b.Contract!.Status = SquadStatus.Backup; b.Contract.WeeklyWage = 1000; }
        s.RecalculateSatisfaction(backups[0]);
        Assert.DoesNotContain("sat.reason.wage.low", s.PlayerRelationsOf(backups[0]).ReasonKeys);
    }

    [Fact]
    public void TransferWishLapsesWhenThePlayerIsHappyAgain()
    {
        var s = TestData.NewSession(202);
        var p = s.World.SquadOf(s.HumanClub).First(x => !x.IsYouth);
        var rel = s.PlayerRelationsOf(p);
        rel.WantsTransfer = true;
        Monday(s);
        // Neu berechnet: zum Saisonstart zufrieden genug, der Wunsch erlischt
        Assert.True(rel.Satisfaction >= s.Data.Relations.WantsTransferResetAt, $"Zufriedenheit {rel.Satisfaction}");
        Assert.False(rel.WantsTransfer);
    }

    [Fact]
    public void BrokenPromisesCostTheWholeSquadTrust()
    {
        var s = TestData.NewSession(203);
        var club = s.HumanClub;
        var cr = s.ClubRelationsOf(club);
        Assert.Equal(s.Data.Relations.Trust.Start, cr.Trust);
        var players = s.World.SquadOf(club).Where(p => !p.IsYouth).Take(3).ToList();
        foreach (var p in players)
        {
            var result = s.TalkToPlayer(p, TalkTopic.Promise, PromiseKind.Contract, s.World.Date.AddDays(1));
            Assert.NotNull(result.Promise);
        }
        s.World.Date = s.World.Date.AddDays(3);
        Rel(s).OnDay(new DayContext(s.World.Date, Array.Empty<Fixture>()));
        Assert.All(s.World.Promises.Where(x => players.Any(p => p.Id == x.PlayerId)), x => Assert.Equal(PromiseOutcome.Broken, x.Outcome));
        Assert.Equal(s.Data.Relations.Trust.Start - 3 * s.Data.Relations.Trust.BrokenPromise, cr.Trust, 3);

        // Auch Unbeteiligte spüren das Misstrauen
        var bystander = s.World.SquadOf(club).First(p => !p.IsYouth && players.All(x => x.Id != p.Id));
        s.RecalculateSatisfaction(bystander);
        Assert.Contains("sat.reason.trust.low", s.PlayerRelationsOf(bystander).ReasonKeys);

        // Erholung Woche für Woche Richtung Startwert
        var before = cr.Trust;
        Monday(s);
        Assert.Equal(before + s.Data.Relations.Trust.WeeklyRecovery, cr.Trust, 3);
    }

    [Fact]
    public void HierarchyAndCouncil()
    {
        var s = TestData.NewSession(204);
        var club = s.HumanClub;
        Monday(s);
        var squad = s.World.SquadOf(club).Where(p => !p.IsYouth && !p.Retired).ToList();
        var cr = s.ClubRelationsOf(club);
        var tiers = squad.GroupBy(p => s.PlayerRelationsOf(p).Tier).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(cr.LeaderIds.Count, tiers.GetValueOrDefault(SquadTier.Leader));
        Assert.Equal((int)Math.Round(squad.Count * s.Data.Relations.Hierarchy.EstablishedShare), tiers.GetValueOrDefault(SquadTier.Established));
        Assert.True(tiers.GetValueOrDefault(SquadTier.Follower) > 0);
        Assert.Equal(Math.Max(s.Data.Relations.Hierarchy.CouncilSize, cr.LeaderIds.Count), cr.CouncilIds.Count);
        Assert.All(cr.LeaderIds, id => Assert.Contains(id, cr.CouncilIds));
        Assert.All(cr.CouncilIds.Except(cr.LeaderIds), id => Assert.Equal(SquadTier.Established, s.PlayerRelationsOf(s.World.Players[id]).Tier));

        // Ein unzufriedener Rat meldet sich beim Trainer und zieht die Mitläufer mit
        var follower = squad.First(p => s.PlayerRelationsOf(p).Tier == SquadTier.Follower);
        var followerBefore = s.PlayerRelationsOf(follower).Satisfaction;
        // Misstrauen und ein verfehltes Saisonziel drücken die Zufriedenheit aller, auch des Rats
        cr.Trust = 0;
        club.Board.Goal = SeasonGoal.Title;
        foreach (var id in cr.CouncilIds) s.World.Players[id].Hidden.Loyalty = 1;
        var inboxBefore = s.Inbox.Count();
        Monday(s);
        Assert.Contains(s.Inbox, m => m.SubjectRef?.Key == "council.concern.subject");
        Assert.True(s.Inbox.Count() > inboxBefore);
        Assert.True(s.PlayerRelationsOf(follower).Satisfaction < followerBefore);
    }
}
