using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>Must-now E1-S2/S5/S6/S7: freie Raten, Gewinn-Weiterverkauf, Leihe mit Bedingung, Ratenvorschau.</summary>
public class MustNowTransferTests
{
    private static Player BuyTarget(Fmh.Core.Game.GameSession s) =>
        s.World.Players.Values
            .Where(p => p.ClubId is { } c && c != s.HumanClub.Id && s.World.FindLeague(s.World.Clubs[c].LeagueId) is not null)
            .Where(p => !p.IsYouth && p.Loan is null && p.MainPosition != Position.GK && Negotiations.RefusalReason(p, s.HumanClub, s.World, s.Data) is null)
            .OrderBy(p => s.ValueOf(p))
            .First(p => s.ValueOf(p) > 100_000);

    [Fact]
    public void FreePaymentPlanSplitsDownPaymentAndUpToEightRates()
    {
        var s = TestData.NewSession(41);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        var fee = Negotiations.AskingPrice(p, seller, s.World, s.Data) * 2;
        var down = fee / 5;
        s.HumanClub.Budget.TransferBudget = down; // nur Anzahlung muss reichen
        var beforeTb = s.HumanClub.Budget.TransferBudget;
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer
        {
            Fee = fee, DownPayment = down, InstallmentCount = 8, SellOnPercent = 0,
        }).Kind);
        Assert.Equal(ResponseKind.Accept, s.SubmitContract(n!, n!.Contract).Kind);
        Assert.Equal(8, s.World.Installments.Count(i => i.FromClubId == s.HumanClub.Id));
        Assert.Equal(beforeTb - down, s.HumanClub.Budget.TransferBudget);
        Assert.True(s.World.Installments.Max(i => i.Due) <= s.World.Date.AddMonths(TransferTerms.MaxInstallmentMonths));
        var sellerValueImmediate = Negotiations.EffectiveValue(new ClubOffer { Fee = fee }, p, s.World, s.Data);
        var sellerValueRates = Negotiations.EffectiveValue(new ClubOffer { Fee = fee, DownPayment = down, InstallmentCount = 8 }, p, s.World, s.Data);
        Assert.True(sellerValueRates < sellerValueImmediate);
    }

    [Fact]
    public void SellOnProfitUsesFeeMinusCostBasis()
    {
        var profit = new SellOnRight { ClubId = 1, Percent = 20, Basis = SellOnBasis.Profit, CostBasis = 1_000_000 };
        Assert.Equal(500_000, TransferTerms.SellOnBase(profit, 1_500_000));
        Assert.Equal(0, TransferTerms.SellOnBase(profit, 800_000));
        Assert.Equal(1_500_000, TransferTerms.SellOnBase(new SellOnRight { Percent = 20, Basis = SellOnBasis.Gross }, 1_500_000));
    }

    [Fact]
    public void LoanAllowsTwentyFourMonthsAndConditionalBuy()
    {
        var s = TestData.NewSession(43);
        var p = BuyTarget(s);
        p.Transfer.LoanListed = true;
        p.BirthDate = s.World.Date.AddYears(-25);
        var (n, error) = s.StartNegotiation(p, DealType.Loan);
        Assert.Null(error);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer
        {
            LoanMonths = 24, WageSharePercent = 100, PurchaseFee = s.ValueOf(p),
            PurchaseObligation = true, PurchaseCondition = LoanBuyCondition.AppearancePercent, PurchaseConditionValue = 50,
        }).Kind);
        Assert.NotNull(p.Loan);
        Assert.True(p.Loan!.Until >= s.World.Date.AddMonths(18));
        Assert.Equal(LoanBuyCondition.AppearancePercent, p.Loan.PurchaseCondition);
        p.Loan.ClubMatches = 10;
        p.Loan.Appearances = 6;
        Assert.True(TransferTerms.PurchaseObligationTriggered(p.Loan, s.World, s.World.Clubs[p.ClubId!.Value]));
        p.Loan.Appearances = 2;
        Assert.False(TransferTerms.PurchaseObligationTriggered(p.Loan, s.World, s.World.Clubs[p.ClubId!.Value]));
    }

    [Fact]
    public void FinanceForecastCoversFourSeasons()
    {
        var s = TestData.NewSession(44);
        var lines = TransferForecast.ForClub(s.World, s.HumanClub);
        Assert.Equal(4, lines.Count);
        Assert.Equal(s.World.SeasonYear, lines[0].SeasonYear);
        Assert.True(lines.Sum(l => l.Wages) > 0);
    }

    [Fact]
    public void TransferWritesContractClausesAndSellerBuyBack()
    {
        var s = TestData.NewSession(45);
        var p = BuyTarget(s);
        var sellerId = p.ClubId!.Value;
        var fee = Negotiations.AskingPrice(p, s.World.Clubs[sellerId], s.World, s.Data) * 2;
        s.HumanClub.Budget.TransferBudget = Math.Max(s.HumanClub.Budget.TransferBudget, fee);
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer
        {
            Fee = fee, BuyBackFee = fee / 2, BuyBackYears = 2, PreemptionRight = true,
        }).Kind);
        n!.Contract.ReleaseClause = fee * 3;
        n.Contract.YearlyRaisePercent = 5;
        n.Contract.ExtensionOptionYears = 1;
        Assert.Equal(ResponseKind.Accept, s.SubmitContract(n, n.Contract).Kind);
        Assert.Contains(s.World.Clauses, c => c.Active && c.Kind == ClauseKind.BuyBack && c.PlayerId == p.Id && c.BeneficiaryClubId == sellerId);
        Assert.Contains(s.World.Clauses, c => c.Active && c.Kind == ClauseKind.Preemption && c.PlayerId == p.Id && c.BeneficiaryClubId == sellerId);
        Assert.Contains(s.World.Clauses, c => c.Active && c.Kind == ClauseKind.Release && c.PlayerId == p.Id);
        Assert.Contains(s.World.Clauses, c => c.Active && c.Kind == ClauseKind.YearlyRaise && c.Percent == 5);
        Assert.Contains(s.World.Clauses, c => c.Active && c.Kind == ClauseKind.ExtensionOption && c.Amount == 1);
    }

    [Fact]
    public void HumanCanExerciseBuyBackAndMatchPreemption()
    {
        var s = TestData.NewSession(46);
        var p = BuyTarget(s);
        var fee = 1_000_000L;
        s.World.Clauses.Add(new Clause
        {
            Id = s.World.NextClauseId++, PlayerId = p.Id, BeneficiaryClubId = s.HumanClub.Id,
            Kind = ClauseKind.BuyBack, Amount = fee, Until = s.World.Date.AddYears(2), Active = true,
        });
        s.HumanClub.Budget.TransferBudget = Math.Max(s.HumanClub.Budget.TransferBudget, fee);
        Assert.True(s.CanExerciseBuyBack(p));
        Assert.Null(s.ExerciseBuyBack(p));
        Assert.Equal(s.HumanClub.Id, p.ClubId);

        var other = BuyTarget(s);
        var clause = new Clause
        {
            Id = s.World.NextClauseId++, PlayerId = other.Id, BeneficiaryClubId = s.HumanClub.Id,
            Kind = ClauseKind.Preemption, Active = true,
        };
        s.World.Clauses.Add(clause);
        var third = s.World.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.Id != other.ClubId && !c.ReserveOf.HasValue);
        var deal = new Negotiation
        {
            Id = s.World.NextNegotiationId++, PlayerId = other.Id, BuyerId = third.Id, SellerId = other.ClubId,
            Type = DealType.Transfer, Stage = NegotiationStage.Pending, Started = s.World.Date,
            Offer = new ClubOffer { Fee = fee },
        };
        s.World.Negotiations.Add(deal);
        var pending = new PreemptionOffer
        {
            ClauseId = clause.Id, NegotiationId = deal.Id, PlayerId = other.Id,
            BeneficiaryClubId = s.HumanClub.Id, Fee = fee, Expires = s.World.Date.AddDays(3),
        };
        s.World.PreemptionOffers.Add(pending);
        s.HumanClub.Budget.TransferBudget = Math.Max(s.HumanClub.Budget.TransferBudget, fee);
        Assert.Null(s.ResolvePreemption(pending, match: true));
        Assert.Equal(s.HumanClub.Id, other.ClubId);
        Assert.Empty(s.World.PreemptionOffers);
    }
}
