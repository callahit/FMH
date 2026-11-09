using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>Should·Next E2-S5: Sportdirektor mit Verhandlungsmandat.</summary>
public class SportsDirectorE2Tests
{
    private static StaffMember EnsureDirector(GameSession s, int ability = 18)
    {
        var existing = s.StaffOf(s.HumanClub).FirstOrDefault(x => x.Role == StaffRole.SportsDirector);
        if (existing is not null)
        {
            existing.Ability = ability;
            return existing;
        }
        var hire = new StaffMember
        {
            Id = s.World.NextStaffId++, Name = "Test Direktion", Role = StaffRole.SportsDirector,
            Ability = ability, Motivation = 12, WeeklyWage = 5_000, ClubId = s.HumanClub.Id,
            ContractUntil = new DateOnly(s.World.SeasonYear + 2, 6, 30),
        };
        s.World.Staff.Add(hire);
        return hire;
    }

    /// <summary>Spielt bis einschließlich eines Montags mit offenem Fenster weiter (mindestens ein Tag).</summary>
    private static void AdvanceToMondayWindow(GameSession s)
    {
        var start = s.World.Date;
        for (var i = 0; i < 120; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
            if (s.World.Date > start && s.IsTransferWindowOpen() && s.World.Date.DayOfWeek == DayOfWeek.Monday)
                return;
        }
        Assert.Fail("Kein Montag mit offenem Transferfenster in 120 Tagen.");
    }

    [Fact]
    public void MandateRequiresSportsDirectorAndBudget()
    {
        var s = TestData.NewSession(21);
        foreach (var d in s.StaffOf(s.HumanClub).Where(x => x.Role == StaffRole.SportsDirector).ToList())
            s.FireStaff(d);
        Assert.Equal("staff.error.noDirector", s.SetDirectorMandate(true, 1_000_000, 10_000));
        EnsureDirector(s);
        Assert.Equal("staff.error.directorBudget", s.SetDirectorMandate(true, 0, 10_000));
        Assert.Null(s.SetDirectorMandate(true, 2_000_000, 15_000));
        Assert.True(s.HumanManager.Profile.DelegateTransfers);
        Assert.Contains(s.World.Messages, m => m.SubjectRef?.Key == "msg.director.mandate.subject"
                                               || m.Subject.Contains("Mandat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirectorSignsShortlistTargetWithinBudget()
    {
        var s = TestData.NewSession(22);
        EnsureDirector(s, ability: 20);
        // Ablösefreier Wechsel: Sportdirektor verpflichtet ohne Verkäufer-KI.
        var target = s.FreeAgents.Where(p => !p.IsYouth && !p.Retired)
            .OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, s.Data.PositionProfiles))
            .First();
        s.HumanClub.Shortlist.Clear();
        s.HumanClub.Shortlist.Add(target.Id);
        s.HumanClub.Budget.WageBudget = Math.Max(s.HumanClub.Budget.WageBudget, 500_000);
        var wage = Negotiations.SuggestContract(target, s.HumanClub, s.World, s.Data).WeeklyWage;
        Assert.Null(s.SetDirectorMandate(true, 5_000_000, Math.Max(wage * 2, 20_000)));

        AdvanceToMondayWindow(s);
        for (var guard = 0; guard < 4 && target.ClubId != s.HumanClub.Id; guard++)
            AdvanceToMondayWindow(s);

        Assert.Equal(s.HumanClub.Id, target.ClubId);
        Assert.Contains(s.World.Messages, m => m.BodyRef?.Key is "msg.director.signed.body" or "msg.director.signedFree.body"
                                               || m.SubjectRef?.Key == "msg.director.signed.subject");
    }

    [Fact]
    public void SportsDirectorIsHireableFromStaffMarket()
    {
        var s = TestData.NewSession(23);
        if (!s.StaffMarket.Any(x => x.Role == StaffRole.SportsDirector))
        {
            s.World.Staff.Add(new StaffMember
            {
                Id = s.World.NextStaffId++, Name = "Markt Direktion", Role = StaffRole.SportsDirector,
                Ability = 14, Motivation = 12, WeeklyWage = GameSession.StaffWage(14),
                ContractUntil = new DateOnly(s.World.SeasonYear + 2, 6, 30),
            });
        }
        var candidate = s.StaffMarket.First(x => x.Role == StaffRole.SportsDirector);
        Assert.Null(s.HireStaff(candidate));
        Assert.Equal(s.HumanClub.Id, candidate.ClubId);
        Assert.Null(s.SetDirectorMandate(true, 1_000_000, 10_000));
    }
}
