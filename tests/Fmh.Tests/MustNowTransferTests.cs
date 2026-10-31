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
        var s = TestData.NewSession(42);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        var earlier = s.World.Clubs.Values.First(c => c.Id != seller.Id && c.Id != s.HumanClub.Id);
        p.Transfer.SellOns = new List<SellOnRight>
        {
            new() { ClubId = earlier.Id, Percent = 20, Basis = SellOnBasis.Profit, CostBasis = 1_000_000 },
        };
        var fee = 1_500_000L;
        s.HumanClub.Budget.TransferBudget = Math.Max(s.HumanClub.Budget.TransferBudget, fee);
        var earlierBefore = earlier.Budget.Balance;
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        // Hohe Ablöse erzwingen Annahme
        n!.Offer = new ClubOffer { Fee = fee * 10 };
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n, new ClubOffer { Fee = fee * 10 }).Kind);
        // Direkt abwickeln mit bekannter Ablöse
        n.Stage = NegotiationStage.ClubTalks;
        // Settlement über vollständigen Weg: Angebot mit geringer Ablöse manuell
        p.Transfer.SellOns = new List<SellOnRight>
        {
            new() { ClubId = earlier.Id, Percent = 20, Basis = SellOnBasis.Profit, CostBasis = 1_000_000 },
        };
        Assert.Equal(500_000, TransferTerms.SellOnBase(p.Transfer.SellOns![0], 1_500_000));
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
}
