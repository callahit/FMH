using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>Zustandsfehler aus dem Code-Review: Leihe nach Karriereende, Tausch mit Leihspieler, Kaderstatus-Bonus.</summary>
public sealed class StateBugTests
{
    /// <summary>Ein Leihspieler im Ruhestand (auch aus älteren Spielständen) kehrt nicht zum Stammverein zurück.</summary>
    [Fact]
    public void RetiredLoaneeDoesNotReturnToTheParentClub()
    {
        var s = TestData.NewSession(31);
        var w = s.World;
        var parent = w.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.LeagueId == s.HumanClub.LeagueId);
        var borrower = w.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.Id != parent.Id && c.LeagueId == s.HumanClub.LeagueId);
        var p = w.SquadOf(parent).First(x => x.Loan is null && !x.IsYouth);
        parent.PlayerIds.Remove(p.Id);
        // Karriereende während einer Leihe, die erst nach der kommenden Saison enden würde.
        p.Loan = new Loan { ParentClubId = parent.Id, Start = w.Date, Until = new DateOnly(w.SeasonYear + 1, 6, 30) };
        p.ClubId = null;
        p.Retired = true;

        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);

        Assert.Null(p.Loan);
        Assert.Null(p.ClubId);
        Assert.DoesNotContain(p.Id, parent.PlayerIds);
        Assert.DoesNotContain(p.Id, borrower.PlayerIds);
    }

    /// <summary>Ein Leihspieler (gehört einem anderen Verein) oder der umworbene Spieler selbst taugt nicht als Tausch.</summary>
    [Fact]
    public void SwapRejectsLoaneesAndTheTargetItself()
    {
        var s = TestData.NewSession(32);
        var w = s.World;
        var seller = w.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.LeagueId == s.HumanClub.LeagueId);
        var target = w.SquadOf(seller).OrderByDescending(x => x.Age(w.Date)).First(x => x.Loan is null && !x.IsYouth);
        var other = w.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.Id != seller.Id);
        var loanee = w.SquadOf(other).First(x => x.Loan is null && !x.IsYouth);
        other.PlayerIds.Remove(loanee.Id);
        s.HumanClub.PlayerIds.Add(loanee.Id);
        loanee.ClubId = s.HumanClub.Id;
        loanee.Loan = new Loan { ParentClubId = other.Id, Start = w.Date, Until = new DateOnly(w.SeasonYear + 1, 6, 30) };
        var own = w.SquadOf(s.HumanClub).First(x => x.Loan is null && !x.IsYouth);

        var n = TestData.StartTalks(s, target, DealType.Transfer);
        Assert.Equal("neg.error.swap", s.SubmitClubOffer(n, new ClubOffer { Fee = 1, SwapPlayerId = loanee.Id }).TextKey);
        Assert.Equal("neg.error.swap", s.SubmitClubOffer(n, new ClubOffer { Fee = 1, SwapPlayerId = target.Id }).TextKey);
        Assert.NotEqual("neg.error.swap", s.SubmitClubOffer(n, new ClubOffer { Fee = 1, SwapPlayerId = own.Id }).TextKey);
    }

    /// <summary>Der Bonus gilt, wenn der angebotene Status mindestens dem erwarteten entspricht (vorher umgekehrt).</summary>
    [Fact]
    public void AttractionRewardsAStatusAtLeastAsGoodAsExpected()
    {
        var s = TestData.NewSession(33);
        var w = s.World;
        var p = w.Players.Values.First(x => x.ClubId is { } c && c != s.HumanClub.Id && !x.IsYouth);
        var expected = Negotiations.ExpectedStatus(p, s.HumanClub, w, s.Data);
        var n = new Negotiation { BuyerId = s.HumanClub.Id, PlayerId = p.Id, Contract = new ContractOffer { WeeklyWage = 0 } };
        double At(SquadStatus status)
        {
            n.Contract.Status = status;
            return Negotiations.PlayerAttraction(n, p, w, s.Data);
        }
        var roleOnly = new Dictionary<SquadStatus, double>
            { [SquadStatus.Star] = 1.25, [SquadStatus.Regular] = 1.1, [SquadStatus.Rotation] = 1.0, [SquadStatus.Backup] = 0.85, [SquadStatus.Prospect] = 0.7 };
        foreach (var status in Enum.GetValues<SquadStatus>())
        {
            var bonus = At(status) - (s.HumanClub.Reputation * 10 + roleOnly[status] * 20);
            Assert.Equal(status <= expected ? 5 : 0, bonus, 6);
        }
    }
}
