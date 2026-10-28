using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Transfers: Abwicklung von Transfers, Leihen und Vertragsenden.</summary>
public sealed partial class TransfersModule
{
    // ================================================================== Abwicklung

    private void Complete(Negotiation n)
    {
        var p = World.Players[n.PlayerId];
        var buyer = World.Clubs[n.BuyerId];
        n.Stage = NegotiationStage.Completed;
        switch (n.Type)
        {
            case DealType.Transfer:
                ExecuteTransfer(p, buyer, World.Clubs[n.SellerId!.Value], n.Offer, n.Contract);
                break;
            case DealType.Loan:
                ExecuteLoan(p, buyer, World.Clubs[n.SellerId!.Value], n.Offer);
                break;
            case DealType.FreeAgent:
                ExecuteTransfer(p, buyer, null, new ClubOffer(), n.Contract);
                break;
            case DealType.PreContract:
                p.PreContract = new PreContract { ClubId = buyer.Id, Contract = MakeContract(n.Contract, new DateOnly(World.SeasonYear + 1, 7, 1), buyer) };
                if (buyer.Id == HumanClub.Id || p.ClubId == HumanClub.Id)
                    Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.preContract.subject", p.Name),
                        TR("msg.preContract.body", p.Name, buyer.Name));
                break;
            case DealType.Renewal:
                var old = p.Contract;
                p.Contract = MakeContract(n.Contract, World.Date, buyer);
                if (old is not null) p.Contract.Start = old.Start;
                if (buyer.Id == HumanClub.Id)
                    Notify(MessageType.Contract, "msg.sender.assistant", TR("msg.renewed.subject", p.Name),
                        TR("msg.renewed.body", p.Name, TextArg.OfDate(p.Contract.Until)));
                break;
        }
    }

    public Contract MakeContract(ContractOffer o, DateOnly start, Club club)
    {
        // Verträge enden immer am 30. Juni.
        var endYear = start.Month >= 7 ? start.Year + o.Years : start.Year + o.Years - 1;
        var (rel, pro) = ContractClauses.Defaults(World, club);
        return new Contract
        {
            WeeklyWage = o.WeeklyWage,
            Start = start,
            Until = new DateOnly(endYear, 6, 30),
            SigningFee = o.SigningFee,
            AppearanceBonus = o.AppearanceBonus,
            GoalBonus = o.GoalBonus,
            ReleaseClause = o.ReleaseClause,
            YearlyRaisePercent = o.YearlyRaisePercent,
            RelegationPercent = o.RelegationPercent ?? rel,
            PromotionPercent = o.PromotionPercent ?? pro,
            ExtensionOptionYears = o.ExtensionOptionYears,
            Status = o.Status,
        };
    }

    private void ExecuteTransfer(Player p, Club buyer, Club? seller, ClubOffer offer, ContractOffer contract)
    {
        var fee = offer.Fee;
        if (seller is not null && fee > 0)
        {
            var upfront = offer.InstallmentYears > 0 ? fee / (offer.InstallmentYears + 1) : fee;
            Finance.Book(buyer, TransactionType.TransferIn, -upfront, TR("tx.transferIn", p.Name));
            Finance.Book(seller, TransactionType.TransferOut, upfront, TR("tx.transferOut", p.Name));
            for (var k = 1; k <= offer.InstallmentYears; k++)
                World.Installments.Add(new Installment { FromClubId = buyer.Id, ToClubId = seller.Id, Due = World.Date.AddDays(365 * k), Amount = (fee - upfront) / offer.InstallmentYears, TextRef = TR("tx.installment", p.Name) });
            buyer.Budget.TransferBudget -= fee;
            seller.Budget.TransferBudget += fee / 2;
            // Weiterverkaufsbeteiligung eines früheren Vereins.
            if (p.Transfer.SellOnClubId is { } soc && soc != buyer.Id && World.Clubs.TryGetValue(soc, out var sellOnClub) && p.Transfer.SellOnPercent > 0)
            {
                var share = fee * p.Transfer.SellOnPercent / 100;
                Finance.Book(seller, TransactionType.Other, -share, TR("tx.sellOnPaid", p.Name));
                Finance.Book(sellOnClub, TransactionType.Other, share, TR("tx.sellOnReceived", p.Name));
            }
            var agent = Negotiations.AgentFee(p, fee);
            Finance.Book(buyer, TransactionType.AgentFee, -agent, TR("tx.agentFee", p.Name));
        }
        if (contract.SigningFee > 0) Finance.Book(buyer, TransactionType.SigningFee, -contract.SigningFee, TR("tx.signingFee", p.Name));

        if (offer.SwapPlayerId is { } swapId && seller is not null && World.Players.TryGetValue(swapId, out var swap))
        {
            Season.RemoveFromClub(swap);
            swap.ClubId = seller.Id;
            seller.PlayerIds.Add(swap.Id);
            World.Transfers.Add(new TransferRecord { Date = World.Date, PlayerId = swap.Id, FromClubId = buyer.Id, ToClubId = seller.Id, SeasonYear = World.SeasonYear });
        }

        var from = p.ClubId;
        Season.RemoveFromClub(p);
        p.ClubId = buyer.Id;
        p.Loan = null;
        p.PreContract = null;
        buyer.PlayerIds.Add(p.Id);
        p.Contract = MakeContract(contract, World.Date, buyer);
        p.Transfer.SellOnClubId = offer.SellOnPercent > 0 ? seller?.Id : null;
        p.Transfer.SellOnPercent = offer.SellOnPercent;
        p.State.Morale = (Morale)Math.Min(6, (int)p.State.Morale + 1);
        World.Transfers.Add(new TransferRecord { Date = World.Date, PlayerId = p.Id, FromClubId = from, ToClubId = buyer.Id, Fee = fee, SeasonYear = World.SeasonYear });
        if (buyer.Id == HumanClub.Id) World.Knowledge[p.Id] = 100;
        if (buyer.Id == HumanClub.Id && fee >= 5_000_000) Board.Achieve("recordSigning");
        // Verkauf eines Publikumslieblings verärgert die Fans.
        if (seller is not null && seller.Fans.Favourites.Remove(p.Id))
        {
            seller.Fans.Mood = Math.Max(0, seller.Fans.Mood - 10);
            if (seller.Id == HumanClub.Id)
                Notify(MessageType.Fans, "msg.sender.fans", TR("msg.favouriteSold.subject", p.Name), TR("msg.favouriteSold.body", p.Name));
        }

        if (buyer.Id == HumanClub.Id || seller?.Id == HumanClub.Id)
        {
            var key = buyer.Id == HumanClub.Id ? "msg.signed" : "msg.sold";
            Notify(MessageType.Transfer, "msg.sender.assistant", TR(key + ".subject", p.Name),
                TR(key + ".body", p.Name, seller is null ? TR("transfer.freeAgent") : seller.Name, buyer.Name, TextArg.Money(fee, World.Country(buyer.CountryCode).Currency)));
        }
    }

    private void ExecuteLoan(Player p, Club buyer, Club parent, ClubOffer offer)
    {
        var until = World.Date.AddMonths(offer.LoanMonths);
        var seasonEnd = new DateOnly(World.SeasonYear + 1, 6, 30);
        if (until > seasonEnd) until = seasonEnd;
        parent.PlayerIds.Remove(p.Id);
        var slot = parent.Tactic.Lineup.IndexOf(p.Id);
        if (slot >= 0) parent.Tactic.Lineup[slot] = null;
        parent.Tactic.Bench.Remove(p.Id);
        buyer.PlayerIds.Add(p.Id);
        p.ClubId = buyer.Id;
        p.Loan = new Loan
        {
            ParentClubId = parent.Id, Until = until, WageSharePercent = offer.WageSharePercent, PurchaseFee = offer.PurchaseFee,
            PurchaseObligation = offer.PurchaseObligation, RecallInWinter = offer.RecallInWinter, PlayingTimeGuarantee = offer.PlayingTimeGuarantee,
        };
        World.Transfers.Add(new TransferRecord { Date = World.Date, PlayerId = p.Id, FromClubId = parent.Id, ToClubId = buyer.Id, Loan = true, SeasonYear = World.SeasonYear });
        if (buyer.Id == HumanClub.Id) World.Knowledge[p.Id] = 100;
        if (buyer.Id == HumanClub.Id || parent.Id == HumanClub.Id)
            Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.loan.subject", p.Name),
                TR("msg.loan.body", p.Name, parent.Name, buyer.Name, TextArg.OfDate(until)));
    }

    private void ReturnLoan(Player p)
    {
        if (p.Loan is not { } loan) return;
        var parent = World.Clubs[loan.ParentClubId];
        var current = p.ClubId is { } cid ? World.Clubs[cid] : null;
        if (current is not null)
        {
            current.PlayerIds.Remove(p.Id);
            var slot = current.Tactic.Lineup.IndexOf(p.Id);
            if (slot >= 0) current.Tactic.Lineup[slot] = null;
            current.Tactic.Bench.Remove(p.Id);
        }
        p.Loan = null;
        p.ClubId = parent.Id;
        parent.PlayerIds.Add(p.Id);
        if (parent.Id == HumanClub.Id || current?.Id == HumanClub.Id)
            Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.loanReturn.subject", p.Name), TR("msg.loanReturn.body", p.Name, parent.Name));
    }

    /// <summary>Zum Saisonwechsel: Verträge enden, Vorverträge greifen, Leihen laufen aus.</summary>
    public void ExpireContractsAndLoans()
    {
        var today = World.Date;
        foreach (var p in World.Players.Values.Where(p => p.Loan is not null && p.Loan.Until < today).ToList())
        {
            if (p.Loan!.PurchaseObligation && p.Loan.PurchaseFee is { } fee && p.ClubId is { } buyerId)
            {
                var buyer = World.Clubs[buyerId];
                var parent = World.Clubs[p.Loan.ParentClubId];
                p.Loan = null;
                buyer.PlayerIds.Remove(p.Id);
                parent.PlayerIds.Add(p.Id);
                p.ClubId = parent.Id;
                ExecuteTransfer(p, buyer, parent, new ClubOffer { Fee = fee }, new ContractOffer { Years = 3, WeeklyWage = p.Contract?.WeeklyWage ?? MarketValue.FairWage(p, World, Data.PositionProfiles) });
            }
            else ReturnLoan(p);
        }
        foreach (var p in World.Players.Values.Where(p => !p.Retired && p.Contract is { } c && c.Until < today).ToList())
        {
            if (p.Contract!.Youth && p.ClubId is not null)
            {
                p.Contract.Until = new DateOnly(World.SeasonYear + 1, 6, 30);
                continue;
            }
            var oldClub = p.ClubId is { } id ? World.Clubs[id] : null;
            if (p.PreContract is { } pre)
            {
                var target = World.Clubs[pre.ClubId];
                Season.RemoveFromClub(p);
                p.ClubId = target.Id;
                target.PlayerIds.Add(p.Id);
                p.Contract = pre.Contract;
                p.PreContract = null;
                World.Transfers.Add(new TransferRecord { Date = today, PlayerId = p.Id, FromClubId = oldClub?.Id, ToClubId = target.Id, SeasonYear = World.SeasonYear });
                if (target.Id == HumanClub.Id) World.Knowledge[p.Id] = 100;
                continue;
            }
            if (oldClub is not null && !IsHuman(oldClub) && ShouldAiRenew(oldClub, p))
            {
                p.Contract = MakeContract(new ContractOffer { Years = Rng.Next(1, 3), WeeklyWage = Negotiations.WageDemand(p, oldClub, p.Contract.Status, World, Data), Status = p.Contract.Status }, today, oldClub);
                continue;
            }
            Season.RemoveFromClub(p);
            p.Contract = null;
            if (oldClub?.Id == HumanClub.Id)
                Notify(MessageType.Contract, "msg.sender.assistant", TR("msg.contractEnded.subject", p.Name), TR("msg.contractEnded.body", p.Name));
        }
    }

    /// <summary>Vereinslose ohne Perspektive beenden ihre Laufbahn (auch freigegebene Jugendspieler unterhalb des Amateurniveaus).</summary>
    public void RetireFreeAgents()
    {
        var floor = PlayerGenerator.ClubLevel(1) - 2;
        foreach (var p in FreeAgents.ToList())
        {
            var age = p.Age(World.Date);
            var chance = age >= 33 ? 1.0 : age >= 30 ? 0.75 : Rating(p) < floor ? 0.9 : 0.6;
            if (Rng.Chance(chance)) p.Retired = true;
        }
    }
}
