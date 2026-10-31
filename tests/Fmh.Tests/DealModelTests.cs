using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;

namespace Fmh.Tests;

/// <summary>
/// Deal- und Vertragsmodell (Konzeptanalyse, P1, erster Teil): Leihgebühr, mehrere Weiterverkaufsrechte,
/// Deal-Id an Rate und Bedingung, Vertrag mit Id und Historie.
/// </summary>
public class DealModelTests
{
    private static Player BuyTarget(Fmh.Core.Game.GameSession s) =>
        s.World.Players.Values
            .Where(p => p.ClubId is { } c && c != s.HumanClub.Id && s.World.FindLeague(s.World.Clubs[c].LeagueId) is not null)
            .Where(p => !p.IsYouth && p.Loan is null && p.MainPosition != Position.GK && Negotiations.RefusalReason(p, s.HumanClub, s.World, s.Data) is null)
            .OrderBy(p => s.ValueOf(p))
            .First(p => s.ValueOf(p) > 100_000);

    [Fact]
    public void LoanFeeIsBookedAndLowersTheWageShare()
    {
        var s = TestData.NewSession(31);
        var p = BuyTarget(s);
        var parent = s.World.Clubs[p.ClubId!.Value];
        p.Transfer.LoanListed = true;
        p.BirthDate = s.World.Date.AddYears(-25);
        var value = s.ValueOf(p);
        var fee = (long)Math.Ceiling(value * 0.2);
        Assert.True(TransferTerms.WageShareFromLoanFee(fee, value) >= 20);
        var (n, error) = s.StartNegotiation(p, DealType.Loan);
        Assert.Null(error);
        s.HumanClub.Budget.TransferBudget = Math.Max(s.HumanClub.Budget.TransferBudget, fee);
        var low = new ClubOffer { LoanMonths = 12, WageSharePercent = 30 };
        Assert.Equal(ResponseKind.Counter, s.SubmitClubOffer(n!, low).Kind);
        n!.Stage = NegotiationStage.ClubTalks;
        n.ClubRounds = 1;
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n, new ClubOffer { LoanMonths = 12, WageSharePercent = 30, LoanFee = fee }).Kind);
        Assert.Equal(fee, p.Loan!.Fee);
        long Booked(int club, string key) => s.World.Transactions.Where(t => t.ClubId == club && t.TextRef?.Key == key).Sum(t => t.Amount);
        Assert.Equal(-fee, Booked(s.HumanClub.Id, "tx.loanFeePaid"));
        // Buchungen von KI-Vereinen stehen nur in der Saisonübersicht, nicht in der Einzelhistorie.
        Assert.Equal(fee, parent.Finance.Season.GetValueOrDefault(TransactionType.LoanFee));
        Assert.Equal(TransactionType.LoanFee, s.World.Transactions.First(t => t.TextRef?.Key == "tx.loanFeePaid").Type);

        n = new Negotiation { PlayerId = p.Id, BuyerId = s.HumanClub.Id, SellerId = parent.Id, Type = DealType.Loan, Stage = NegotiationStage.ClubTalks };
        s.World.Negotiations.Add(n);
        s.HumanClub.Budget.TransferBudget = 0;
        Assert.Equal(ResponseKind.Reject, s.SubmitClubOffer(n, new ClubOffer { LoanMonths = 12, WageSharePercent = 100, LoanFee = 1 }).Kind);
    }

    [Fact]
    public void SeveralSellOnsArePaidAndKeptAndTheSumIsCapped()
    {
        var s = TestData.NewSession(32);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        var earlier = s.World.Clubs.Values.First(c => c.Id != seller.Id && c.Id != s.HumanClub.Id);
        var earlierBefore = earlier.Budget.Balance;
        var sellerBefore = seller.Budget.Balance;
        p.Transfer.SellOnClubId = earlier.Id;
        p.Transfer.SellOnPercent = 40;
        var ask = Negotiations.AskingPrice(p, seller, s.World, s.Data);
        var fee = ask * 2;
        s.HumanClub.Budget.TransferBudget = Math.Max(s.HumanClub.Budget.TransferBudget, fee);
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer { Fee = fee, SellOnPercent = 30, InstallmentYears = 1 }).Kind);
        Assert.Equal(ResponseKind.Accept, s.SubmitContract(n!, n!.Contract).Kind);

        var deal = s.World.Transfers.Single(t => t.PlayerId == p.Id && t.ToClubId == s.HumanClub.Id);
        Assert.True(deal.DealId > 0);
        var rate = Assert.Single(s.World.Installments, i => i.DealId == deal.DealId);
        Assert.Equal((seller.Id, s.HumanClub.Id), (rate.ToClubId, rate.FromClubId));
        var rights = p.Transfer.SellOns ?? throw new InvalidOperationException("Rechte fehlen");
        Assert.Equal(40, rights.Single(r => r.ClubId == earlier.Id).Percent);
        Assert.Equal(10, rights.Single(r => r.ClubId == seller.Id).Percent);
        Assert.Equal(TransferTerms.MaxSellOnPercent, rights.Sum(r => r.Percent));
        Assert.Equal(deal.DealId, rights.Single(r => r.ClubId == seller.Id).OriginDealId);
        var upfront = fee / 2;
        Assert.Equal(earlierBefore + fee * 40 / 100, earlier.Budget.Balance);
        Assert.Equal(sellerBefore + upfront - fee * 40 / 100, seller.Budget.Balance);

        var history = s.World.ContractHistory.Where(c => c.PlayerId == p.Id).OrderBy(c => c.Id).ToList();
        Assert.Equal(2, history.Count);
        Assert.False(history[0].Current);
        Assert.Equal(history[1].Id, history[0].SupersededBy);
        Assert.True(history[1].Current);
        Assert.Equal((s.HumanClub.Id, deal.DealId, p.Contract!.WeeklyWage), (history[1].ClubId, history[1].DealId, history[1].WeeklyWage));

        // Zweiter Wechsel: beide Rechte werden erneut bezahlt und bleiben bestehen.
        var buyer = s.World.Clubs.Values.Where(c => c.Id != s.HumanClub.Id && c.Id != earlier.Id && c.Budget.TransferBudget >= 500_000
                                                    && Negotiations.RefusalReason(p, c, s.World, s.Data) is null)
            .OrderByDescending(c => c.Budget.TransferBudget).First();
        var incoming = new Negotiation
        {
            Id = s.World.NextNegotiationId++, Type = DealType.Transfer, Stage = NegotiationStage.Pending, Incoming = true,
            PlayerId = p.Id, BuyerId = buyer.Id, SellerId = s.HumanClub.Id, Started = s.World.Date,
            Offer = new ClubOffer { Fee = 400_000, SellOnPercent = 0 },
            Contract = Negotiations.SuggestContract(p, buyer, s.World, s.Data),
        };
        incoming.Contract.WeeklyWage = Math.Max(incoming.Contract.WeeklyWage, p.Contract.WeeklyWage);
        s.World.Negotiations.Add(incoming);
        s.RespondIncoming(incoming, ResponseKind.Accept);
        Assert.Equal(buyer.Id, p.ClubId);
        Assert.Equal(2, p.Transfer.SellOns?.Count);
        Assert.Equal(earlierBefore + fee * 40 / 100 + 400_000 * 40 / 100, earlier.Budget.Balance);
        Assert.Equal(sellerBefore + upfront - fee * 40 / 100 + 400_000 * 10 / 100, seller.Budget.Balance);
    }

    [Fact]
    public void ContractHistoryAndDealIdsSurviveSaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-deals-" + Guid.NewGuid());
        try
        {
            var s = TestData.NewSession(33);
            var p = s.World.SquadOf(s.HumanClub).First(x => x.Contract is not null);
            s.World.NextDealId = 9;
            s.World.NextContractId = 4;
            s.World.ContractHistory.Add(new ContractRecord
            {
                Id = 3, PlayerId = p.Id, ClubId = s.HumanClub.Id, DealId = 8, Start = s.World.Date, End = p.Contract!.Until,
                WeeklyWage = p.Contract.WeeklyWage, Current = false, SupersededBy = 4,
            });
            s.World.Transfers.Add(new TransferRecord { DealId = 8, Date = s.World.Date, PlayerId = p.Id, ToClubId = s.HumanClub.Id, Fee = 10, SeasonYear = s.World.SeasonYear });
            s.World.Installments.Add(new Installment { DealId = 8, FromClubId = s.HumanClub.Id, ToClubId = 1, Due = s.World.Date, Amount = 5 });
            var path = Path.Combine(dir, "deals.fmh");
            SqliteStore.Save(s.World, path);
            var w = SqliteStore.Load(path);
            Assert.Equal(9, w.NextDealId);
            // Save legt für alle laufenden Verträge Historieeinträge an und erhöht next_contract_id entsprechend.
            Assert.True(w.NextContractId > 4);
            var c = Assert.Single(w.ContractHistory, x => x.Id == 3);
            Assert.Equal((3, p.Id, 8, 4), (c.Id, c.PlayerId, c.DealId, c.SupersededBy));
            Assert.Equal(8, Assert.Single(w.Transfers).DealId);
            Assert.Equal(8, Assert.Single(w.Installments).DealId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
