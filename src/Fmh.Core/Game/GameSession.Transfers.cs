using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Transfermarkt, Verträge, Leihen und KI-Transfers (Kapitel 7).</summary>
public sealed partial class GameSession
{
    public long ValueOf(Player p) => MarketValue.Of(p, World.Date, Data.PositionProfiles);

    public IEnumerable<Player> FreeAgents => World.Players.Values.Where(p => p.ClubId is null && !p.Retired && !p.IsYouth);

    /// <summary>Offene Verhandlungen des menschlichen Managers (eigene Anfragen und eingehende Angebote).</summary>
    public IReadOnlyList<Negotiation> OpenNegotiations => World.Negotiations
        .Where(n => n.Stage is NegotiationStage.ClubTalks or NegotiationStage.PlayerTalks or NegotiationStage.Pending)
        .Where(n => n.BuyerId == HumanClub.Id || n.SellerId == HumanClub.Id)
        .ToList();

    public Negotiation? OpenNegotiationFor(Player p) => OpenNegotiations.FirstOrDefault(n => n.PlayerId == p.Id && !n.Incoming);

    private string Currency => World.Country(HumanClub.CountryCode).Currency;

    // ================================================================== Menschliche Aktionen

    /// <summary>Beginnt eine Verhandlung. Gibt einen Fehlertext-Schlüssel zurück, wenn sie nicht möglich ist.</summary>
    public (Negotiation? negotiation, string? errorKey) StartNegotiation(Player p, DealType type)
    {
        var buyer = HumanClub;
        if (OpenNegotiationFor(p) is { } open) return (open, null);
        if (p.Retired) return (null, "neg.error.retired");
        if (type == DealType.Renewal)
        {
            if (p.ClubId != buyer.Id || p.Loan is not null) return (null, "neg.error.notOwn");
        }
        else
        {
            if (p.ClubId == buyer.Id) return (null, "neg.error.own");
            if (p.IsYouth) return (null, "neg.error.youth");
            if (type is DealType.Transfer or DealType.Loan && !IsTransferWindowOpen()) return (null, "neg.error.window");
            if (type == DealType.FreeAgent && p.ClubId is not null) return (null, "neg.error.notFree");
            if (type == DealType.PreContract && !CanPreContract(p)) return (null, "neg.error.preContract");
            if (p.Loan is not null && type != DealType.PreContract) return (null, "neg.error.onLoan");
            if (Negotiations.IsLocked(World, p.Id, buyer.Id)) return (null, "neg.error.locked");
            if (p.PreContract is not null) return (null, "neg.error.preContractTaken");
            if (type != DealType.Loan && Negotiations.RefusalReason(p, buyer, World, Data) is { } refusal) return (null, refusal);
        }

        var n = new Negotiation
        {
            Id = World.NextNegotiationId++,
            Type = type,
            PlayerId = p.Id,
            BuyerId = buyer.Id,
            SellerId = type is DealType.FreeAgent or DealType.Renewal ? null : p.ClubId,
            Started = World.Date,
            Stage = type is DealType.Transfer or DealType.Loan ? NegotiationStage.ClubTalks : NegotiationStage.PlayerTalks,
            Contract = Negotiations.SuggestContract(p, buyer, World, Data),
        };
        if (type == DealType.Transfer && p.ClubId is { } sid)
            n.Offer.Fee = MarketValue.Round(Negotiations.AskingPrice(p, World.Clubs[sid], World, Data) * 0.85);
        if (type == DealType.Renewal && p.Contract is { } c)
        {
            n.Contract.Status = c.Status;
            n.Contract.WeeklyWage = Math.Max(c.WeeklyWage, n.Contract.WeeklyWage);
        }
        World.Negotiations.Add(n);
        return (n, null);
    }

    public bool CanPreContract(Player p)
    {
        if (p.ClubId is null || p.Contract is not { } c) return false;
        var country = World.Country(World.Clubs[p.ClubId.Value].CountryCode);
        var from = ScheduleGenerator.ParseDate(country.TransferWindows.PreContractFrom, World.SeasonYear, country.Season.StartMonth);
        return World.Date >= from && c.Until <= new DateOnly(World.SeasonYear + 1, 6, 30);
    }

    public NegotiationResponse SubmitClubOffer(Negotiation n, ClubOffer offer)
    {
        var p = World.Players[n.PlayerId];
        var buyer = World.Clubs[n.BuyerId];
        if (n.Stage != NegotiationStage.ClubTalks || n.SellerId is not { } sellerId) return new(ResponseKind.Reject, "neg.error.stage");
        var seller = World.Clubs[sellerId];
        if (n.Type == DealType.Transfer && offer.Fee > buyer.Budget.TransferBudget) return new(ResponseKind.Reject, "neg.error.budget");
        if (offer.SwapPlayerId is { } swap && World.Players[swap].ClubId != buyer.Id) return new(ResponseKind.Reject, "neg.error.swap");
        offer.InstallmentYears = Math.Clamp(offer.InstallmentYears, 0, 3);
        offer.SellOnPercent = Math.Clamp(offer.SellOnPercent, 0, 50);
        offer.LoanMonths = Math.Clamp(offer.LoanMonths, 6, 12);
        offer.WageSharePercent = Math.Clamp(offer.WageSharePercent, 0, 100);

        n.ClubRounds++;
        n.Offer = offer.Clone();
        var resp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
        n.LastResponse = T.Format(resp.TextKey, Fmt.Money(resp.Amount, Currency));
        switch (resp.Kind)
        {
            case ResponseKind.Accept:
                n.Stage = NegotiationStage.PlayerTalks;
                if (n.Type == DealType.Loan)
                {
                    Complete(n);
                    return resp;
                }
                n.Contract = Negotiations.SuggestContract(p, buyer, World, Data);
                break;
            case ResponseKind.Counter:
                n.CounterFee = resp.Amount;
                if (n.ClubRounds >= Negotiations.MaxRounds) Fail(n);
                break;
            default:
                Fail(n);
                break;
        }
        return resp;
    }

    public NegotiationResponse SubmitContract(Negotiation n, ContractOffer offer)
    {
        var p = World.Players[n.PlayerId];
        var buyer = World.Clubs[n.BuyerId];
        if (n.Stage != NegotiationStage.PlayerTalks) return new(ResponseKind.Reject, "neg.error.stage");
        var currentWage = n.Type == DealType.Renewal ? p.Contract?.WeeklyWage ?? 0 : 0;
        if (Budgets.WageBill(buyer, World) - currentWage + offer.WeeklyWage > buyer.Budget.WageBudget)
            return new(ResponseKind.Reject, "neg.error.wageBudget");
        if (offer.SigningFee > Math.Max(0, buyer.Budget.Balance)) return new(ResponseKind.Reject, "neg.error.balance");

        n.PlayerRounds++;
        n.Contract = offer.Clone();
        var resp = Negotiations.EvaluateContract(n, p, buyer, World, Data);
        n.LastResponse = T.Format(resp.TextKey, Fmt.Money(resp.Amount, Currency) + T["unit.perWeek"]);
        switch (resp.Kind)
        {
            case ResponseKind.Accept:
                Complete(n);
                break;
            case ResponseKind.Counter:
                n.WageDemand = (int)resp.Amount;
                if (n.PlayerRounds >= Negotiations.MaxRounds) Fail(n);
                break;
            default:
                Fail(n);
                break;
        }
        return resp;
    }

    public void Withdraw(Negotiation n)
    {
        if (n.Stage is NegotiationStage.ClubTalks or NegotiationStage.PlayerTalks) n.Stage = NegotiationStage.Withdrawn;
    }

    private void Fail(Negotiation n)
    {
        n.Stage = NegotiationStage.Failed;
        World.NegotiationLocks[Negotiations.LockKey(n.PlayerId, n.BuyerId)] = World.Date.AddDays(Negotiations.LockDays);
    }

    /// <summary>Antwort auf ein eingehendes Angebot eines KI-Vereins.</summary>
    public string RespondIncoming(Negotiation n, ResponseKind answer, long counterFee = 0)
    {
        var p = World.Players[n.PlayerId];
        var buyer = World.Clubs[n.BuyerId];
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.IncomingOffer && m.ActionRefId == n.Id)) m.Resolved = true;
        if (n.Stage != NegotiationStage.Pending) return T["neg.error.stage"];

        if (answer == ResponseKind.Reject)
        {
            n.Stage = NegotiationStage.Failed;
            return T.Format("neg.incoming.rejected", buyer.Name);
        }
        if (answer == ResponseKind.Counter)
        {
            var limit = Math.Min(buyer.Budget.TransferBudget, (long)(ValueOf(p) * 1.5));
            if (counterFee > limit)
            {
                n.Stage = NegotiationStage.Failed;
                return T.Format("neg.incoming.counterRejected", buyer.Name);
            }
            n.Offer.Fee = counterFee;
        }
        // Verein ist sich einig: der Spieler verhandelt selbst mit dem Käufer.
        n.Contract = Negotiations.SuggestContract(p, buyer, World, Data);
        if (Negotiations.RefusalReason(p, buyer, World, Data) is { } refusal)
        {
            n.Stage = NegotiationStage.Failed;
            return T.Format("neg.incoming.playerRefused", p.Name, T[refusal]);
        }
        n.Stage = NegotiationStage.PlayerTalks;
        Complete(n);
        return T.Format("neg.incoming.sold", p.Name, buyer.Name, Fmt.Money(n.Offer.Fee, Currency));
    }

    /// <summary>Überlässt offene Pflicht-Entscheidungen dem Assistenten: Angebote werden abgelehnt.</summary>
    public void DelegateDecisions()
    {
        foreach (var m in PendingDecisions)
        {
            if (m.Action == MessageAction.IncomingOffer && World.Negotiations.FirstOrDefault(n => n.Id == m.ActionRefId) is { } n)
                RespondIncoming(n, ResponseKind.Reject);
            m.Resolved = true;
        }
    }

    public void SetTransferListed(Player p, bool listed, long? askingPrice = null)
    {
        p.Transfer.Listed = listed;
        p.Transfer.AskingPrice = listed ? askingPrice ?? MarketValue.Round(ValueOf(p) * 1.05) : null;
    }

    public void SetLoanListed(Player p, bool listed) => p.Transfer.LoanListed = listed;

    public void ToggleShortlist(Player p)
    {
        var list = HumanClub.Shortlist;
        if (!list.Remove(p.Id)) list.Add(p.Id);
    }

    /// <summary>Kaufoption eines Leihspielers ziehen.</summary>
    public string? ExercisePurchaseOption(Player p)
    {
        if (p.Loan is not { PurchaseFee: { } fee } loan || p.ClubId != HumanClub.Id) return "neg.error.noOption";
        if (fee > HumanClub.Budget.TransferBudget) return "neg.error.budget";
        var parent = World.Clubs[loan.ParentClubId];
        var contract = p.Contract is { } c ? new ContractOffer { Years = Math.Max(1, (c.Until.Year - World.Date.Year)), WeeklyWage = c.WeeklyWage, Status = c.Status } : Negotiations.SuggestContract(p, HumanClub, World, Data);
        p.Loan = null;
        p.ClubId = parent.Id;
        HumanClub.PlayerIds.Remove(p.Id);
        parent.PlayerIds.Add(p.Id);
        ExecuteTransfer(p, HumanClub, parent, new ClubOffer { Fee = fee }, contract);
        return null;
    }

    /// <summary>Verliehenen Spieler in der Winterpause zurückholen (Rückholklausel).</summary>
    public string? RecallLoan(Player p)
    {
        if (p.Loan is not { RecallInWinter: true } loan || loan.ParentClubId != HumanClub.Id) return "neg.error.noRecall";
        var country = World.Country(HumanClub.CountryCode);
        var tw = country.TransferWindows;
        var winterStart = ScheduleGenerator.ParseDate(tw.WinterStart, World.SeasonYear, country.Season.StartMonth);
        var winterEnd = ScheduleGenerator.ParseDate(tw.WinterEnd, World.SeasonYear, country.Season.StartMonth);
        if (World.Date < winterStart || World.Date > winterEnd) return "neg.error.window";
        ReturnLoan(p);
        return null;
    }

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
                p.PreContract = new PreContract { ClubId = buyer.Id, Contract = MakeContract(n.Contract, new DateOnly(World.SeasonYear + 1, 7, 1)) };
                if (buyer.Id == HumanClub.Id || p.ClubId == HumanClub.Id)
                    Notify(MessageType.Transfer, "msg.sender.assistant", T.Format("msg.preContract.subject", p.Name),
                        T.Format("msg.preContract.body", p.Name, buyer.Name));
                break;
            case DealType.Renewal:
                var old = p.Contract;
                p.Contract = MakeContract(n.Contract, World.Date);
                if (old is not null) p.Contract.Start = old.Start;
                if (buyer.Id == HumanClub.Id)
                    Notify(MessageType.Contract, "msg.sender.assistant", T.Format("msg.renewed.subject", p.Name),
                        T.Format("msg.renewed.body", p.Name, Fmt.Date(p.Contract.Until)));
                break;
        }
    }

    private Contract MakeContract(ContractOffer o, DateOnly start)
    {
        // Verträge enden immer am 30. Juni.
        var endYear = start.Month >= 7 ? start.Year + o.Years : start.Year + o.Years - 1;
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
            RelegationClause = o.RelegationClause,
            ExtensionOptionYears = o.ExtensionOptionYears,
            Status = o.Status,
        };
    }

    private void Book(Club club, TransactionType type, long amount, string text)
    {
        club.Budget.Balance += amount;
        World.Transactions.Add(new Transaction { Date = World.Date, ClubId = club.Id, Type = type, Amount = amount, Text = text });
    }

    private void ExecuteTransfer(Player p, Club buyer, Club? seller, ClubOffer offer, ContractOffer contract)
    {
        var fee = offer.Fee;
        if (seller is not null && fee > 0)
        {
            var upfront = offer.InstallmentYears > 0 ? fee / (offer.InstallmentYears + 1) : fee;
            Book(buyer, TransactionType.TransferIn, -upfront, T.Format("tx.transferIn", p.Name));
            Book(seller, TransactionType.TransferOut, upfront, T.Format("tx.transferOut", p.Name));
            for (var k = 1; k <= offer.InstallmentYears; k++)
                World.Installments.Add(new Installment { FromClubId = buyer.Id, ToClubId = seller.Id, Due = World.Date.AddDays(365 * k), Amount = (fee - upfront) / offer.InstallmentYears, Text = T.Format("tx.installment", p.Name) });
            buyer.Budget.TransferBudget -= fee;
            seller.Budget.TransferBudget += fee / 2;
            // Weiterverkaufsbeteiligung eines früheren Vereins.
            if (p.Transfer.SellOnClubId is { } soc && soc != buyer.Id && World.Clubs.TryGetValue(soc, out var sellOnClub) && p.Transfer.SellOnPercent > 0)
            {
                var share = fee * p.Transfer.SellOnPercent / 100;
                Book(seller, TransactionType.Other, -share, T.Format("tx.sellOnPaid", p.Name));
                Book(sellOnClub, TransactionType.Other, share, T.Format("tx.sellOnReceived", p.Name));
            }
            var agent = Negotiations.AgentFee(p, fee);
            Book(buyer, TransactionType.AgentFee, -agent, T.Format("tx.agentFee", p.Name));
        }
        if (contract.SigningFee > 0) Book(buyer, TransactionType.SigningFee, -contract.SigningFee, T.Format("tx.signingFee", p.Name));

        if (offer.SwapPlayerId is { } swapId && seller is not null && World.Players.TryGetValue(swapId, out var swap))
        {
            RemoveFromClub(swap);
            swap.ClubId = seller.Id;
            seller.PlayerIds.Add(swap.Id);
            World.Transfers.Add(new TransferRecord { Date = World.Date, PlayerId = swap.Id, FromClubId = buyer.Id, ToClubId = seller.Id, SeasonYear = World.SeasonYear });
        }

        var from = p.ClubId;
        RemoveFromClub(p);
        p.ClubId = buyer.Id;
        p.Loan = null;
        p.PreContract = null;
        buyer.PlayerIds.Add(p.Id);
        p.Contract = MakeContract(contract, World.Date);
        p.Transfer.SellOnClubId = offer.SellOnPercent > 0 ? seller?.Id : null;
        p.Transfer.SellOnPercent = offer.SellOnPercent;
        p.State.Morale = (Morale)Math.Min(6, (int)p.State.Morale + 1);
        World.Transfers.Add(new TransferRecord { Date = World.Date, PlayerId = p.Id, FromClubId = from, ToClubId = buyer.Id, Fee = fee, SeasonYear = World.SeasonYear });
        if (buyer.Id == HumanClub.Id) World.Knowledge[p.Id] = 100;

        if (buyer.Id == HumanClub.Id || seller?.Id == HumanClub.Id)
        {
            var key = buyer.Id == HumanClub.Id ? "msg.signed" : "msg.sold";
            Notify(MessageType.Transfer, "msg.sender.assistant", T.Format(key + ".subject", p.Name),
                T.Format(key + ".body", p.Name, seller?.Name ?? T["transfer.freeAgent"], buyer.Name, Fmt.Money(fee, World.Country(buyer.CountryCode).Currency)));
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
            Notify(MessageType.Transfer, "msg.sender.assistant", T.Format("msg.loan.subject", p.Name),
                T.Format("msg.loan.body", p.Name, parent.Name, buyer.Name, Fmt.Date(until)));
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
            Notify(MessageType.Transfer, "msg.sender.assistant", T.Format("msg.loanReturn.subject", p.Name), T.Format("msg.loanReturn.body", p.Name, parent.Name));
    }

    /// <summary>Zum Saisonwechsel: Verträge enden, Vorverträge greifen, Leihen laufen aus.</summary>
    private void ExpireContractsAndLoans()
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
                ExecuteTransfer(p, buyer, parent, new ClubOffer { Fee = fee }, new ContractOffer { Years = 3, WeeklyWage = p.Contract?.WeeklyWage ?? MarketValue.FairWage(p, Data.PositionProfiles) });
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
                RemoveFromClub(p);
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
                p.Contract = MakeContract(new ContractOffer { Years = _rng.Next(1, 3), WeeklyWage = Negotiations.WageDemand(p, oldClub, p.Contract.Status, World, Data), Status = p.Contract.Status }, today);
                continue;
            }
            RemoveFromClub(p);
            p.Contract = null;
            if (oldClub?.Id == HumanClub.Id)
                Notify(MessageType.Contract, "msg.sender.assistant", T.Format("msg.contractEnded.subject", p.Name), T.Format("msg.contractEnded.body", p.Name));
        }
    }

    private void RetireFreeAgents()
    {
        foreach (var p in FreeAgents.Where(p => p.Age(World.Date) >= 34 || (p.Age(World.Date) >= 31 && _rng.Chance(0.35))).ToList())
            p.Retired = true;
    }

    // ================================================================== KI

    private void MarketDaily()
    {
        foreach (var inst in World.Installments.Where(i => i.Due == World.Date).ToList())
        {
            Book(World.Clubs[inst.FromClubId], TransactionType.Installment, -inst.Amount, inst.Text);
            Book(World.Clubs[inst.ToClubId], TransactionType.Installment, inst.Amount, inst.Text);
            World.Installments.Remove(inst);
        }
        foreach (var lockKey in World.NegotiationLocks.Where(kv => kv.Value <= World.Date).Select(kv => kv.Key).ToList())
            World.NegotiationLocks.Remove(lockKey);

        var monday = World.Date.DayOfWeek == DayOfWeek.Monday;
        if (!monday) return;

        var aiClubs = World.Clubs.Values.Where(c => !IsHuman(c)).ToList();
        _rng.Shuffle(aiClubs);
        var window = IsTransferWindowOpen();
        foreach (var club in aiClubs)
        {
            ManageAiSquadSize(club);
            if (club.PlayerIds.Count < 22) SignBestFreeAgent(club, NeededPosition(club));
            if (window && _rng.Chance(0.3)) TryAiSigning(club);
        }
        if (window && _rng.Chance(0.3)) MakeIncomingOffer();
        if (World.Date.Month is >= 2 and <= 5 && World.Date.Day <= 7) AiPreContracts(aiClubs);
    }

    private int TransfersThisWindow(Club club)
    {
        var from = World.Date.AddDays(-62);
        return World.Transfers.Count(t => t.ToClubId == club.Id && t.Date >= from && !t.Loan);
    }

    private double Rating(Player p) => Ratings.PositionRating(p, p.MainPosition, Data.PositionProfiles);

    /// <summary>KI kauft gezielt für die Position mit dem größten Bedarf.</summary>
    private void TryAiSigning(Club club)
    {
        var summer = World.Date.Month is 7 or 8;
        if (TransfersThisWindow(club) >= (summer ? 3 : 1)) return;
        var need = NeededPosition(club);
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        var current = World.SquadOf(club).Where(p => p.MainPosition == need).Select(Rating).DefaultIfEmpty(0).Max();
        var minRating = Math.Max(current + 0.3, level - 1.0);
        var candidates = World.Players.Values
            .Where(p => !p.Retired && !p.IsYouth && p.ClubId != club.Id && p.ClubId != HumanClub.Id && p.Loan is null && p.PreContract is null)
            .Where(p => p.MainPosition == need && p.Age(World.Date) <= 31)
            .Select(p => (p, r: Rating(p)))
            .Where(x => x.r >= minRating && x.r <= level + 3)
            .Where(x => x.p.ClubId is null || World.Clubs[x.p.ClubId.Value].Reputation <= club.Reputation + 3)
            .OrderByDescending(x => x.r)
            .Take(6)
            .ToList();
        foreach (var (p, _) in candidates)
        {
            if (Negotiations.IsLocked(World, p.Id, club.Id) || Negotiations.RefusalReason(p, club, World, Data) is not null) continue;
            var contract = Negotiations.SuggestContract(p, club, World, Data);
            if (Budgets.WageBill(club, World) + contract.WeeklyWage > club.Budget.WageBudget * 1.05) continue;
            if (p.ClubId is not { } sellerId)
            {
                ExecuteTransfer(p, club, null, new ClubOffer(), contract);
                club.Budget.WageBudget = Math.Max(club.Budget.WageBudget, Budgets.WageBill(club, World));
                return;
            }
            var seller = World.Clubs[sellerId];
            var ask = Negotiations.AskingPrice(p, seller, World, Data);
            if (ask > club.Budget.TransferBudget) continue;
            var n = new Negotiation { PlayerId = p.Id, BuyerId = club.Id, SellerId = sellerId, Type = DealType.Transfer, Offer = new ClubOffer { Fee = MarketValue.Round(ask * _rng.Range(0.85, 1.0)) } };
            var resp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
            if (resp.Kind == ResponseKind.Counter && resp.Amount <= club.Budget.TransferBudget) n.Offer.Fee = resp.Amount;
            else if (resp.Kind != ResponseKind.Accept) continue;
            // Der Verkäufer füllt die Lücke später selbst wieder auf.
            ExecuteTransfer(p, club, seller, n.Offer, contract);
            club.Budget.WageBudget = Math.Max(club.Budget.WageBudget, Budgets.WageBill(club, World));
            return;
        }
    }

    public bool SignBestFreeAgent(Club club, Position pos)
    {
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        var best = FreeAgents
            .Where(p => p.MainPosition == pos || p.SuitabilityFor(pos) >= Suitability.Good)
            .Select(p => (p, r: Ratings.EffectiveRating(p, pos, Data.PositionProfiles)))
            .Where(x => x.r <= level + 2 && Negotiations.RefusalReason(x.p, club, World, Data) is null)
            .OrderByDescending(x => x.r)
            .Select(x => x.p)
            .FirstOrDefault();
        if (best is null) return false;
        var contract = Negotiations.SuggestContract(best, club, World, Data);
        contract.Years = Math.Min(contract.Years, best.Age(World.Date) >= 30 ? 1 : 2);
        ExecuteTransfer(best, club, null, new ClubOffer(), contract);
        club.Budget.WageBudget = Math.Max(club.Budget.WageBudget, Budgets.WageBill(club, World));
        return true;
    }

    /// <summary>KI hält den Kader in Grenzen: Überzählige auf die Transferliste, notfalls Vertrag auflösen.</summary>
    private void ManageAiSquadSize(Club club)
    {
        var max = World.Country(club.CountryCode).Squad.MaxSquad;
        var ranked = World.SquadOf(club).Where(p => p.Loan is null).OrderBy(Rating).ToList();
        foreach (var p in ranked.Take(Math.Max(0, club.PlayerIds.Count - 26)))
            if (!p.Transfer.Listed) SetTransferListed(p, true, MarketValue.Round(ValueOf(p) * 0.8));
        while (club.PlayerIds.Count > max && ranked.Count > 0)
        {
            var worst = ranked[0];
            ranked.RemoveAt(0);
            RemoveFromClub(worst);
            worst.Contract = null;
        }
    }

    private bool ShouldAiRenew(Club club, Player p)
    {
        var rank = World.SquadOf(club).OrderByDescending(Rating).ToList().IndexOf(p);
        return rank is >= 0 and < 18 && p.Age(World.Date) <= 31;
    }

    /// <summary>KI-Vereine sichern sich ablösefreie Spieler mit auslaufendem Vertrag per Vorvertrag.</summary>
    private void AiPreContracts(List<Club> aiClubs)
    {
        foreach (var club in aiClubs.Where(_ => _rng.Chance(0.1)))
        {
            var need = NeededPosition(club);
            var level = PlayerGenerator.ClubLevel(club.Reputation);
            var target = World.Players.Values
                .Where(p => p.ClubId is { } c && c != club.Id && !p.IsYouth && p.PreContract is null && p.Loan is null && CanPreContract(p))
                .Where(p => p.ClubId != HumanClub.Id || _rng.Chance(0.5))
                .Where(p => p.MainPosition == need && Rating(p) >= level - 0.5 && Rating(p) <= level + 2.5)
                .Where(p => !ShouldAiRenew(World.Clubs[p.ClubId!.Value], p) || p.ClubId == HumanClub.Id)
                .FirstOrDefault(p => Negotiations.RefusalReason(p, club, World, Data) is null);
            if (target is null) continue;
            var contract = Negotiations.SuggestContract(target, club, World, Data);
            target.PreContract = new PreContract { ClubId = club.Id, Contract = MakeContract(contract, new DateOnly(World.SeasonYear + 1, 7, 1)) };
            if (target.ClubId == HumanClub.Id)
                Notify(MessageType.Transfer, "msg.sender.assistant", T.Format("msg.preContractLost.subject", target.Name),
                    T.Format("msg.preContractLost.body", target.Name, club.Name));
        }
    }

    /// <summary>Ein KI-Verein bietet für einen Spieler des menschlichen Vereins (Pflicht-Entscheidung im Posteingang).</summary>
    private void MakeIncomingOffer()
    {
        var human = HumanClub;
        if (World.Date.Month == 7 && World.Date.Day < 15) return; // Neue Manager bekommen zwei Wochen Ruhe.
        if (OpenNegotiations.Any(n => n.Incoming)) return;
        var squad = World.SquadOf(human).Where(p => p.Loan is null && p.PreContract is null).ToList();
        if (squad.Count <= 18) return;
        var listed = squad.Where(p => p.Transfer.Listed).ToList();
        var target = listed.Count > 0 && _rng.Chance(0.7) ? _rng.Pick(listed) : _rng.Pick(squad.OrderByDescending(Rating).Take(14).ToList());
        var buyers = World.Clubs.Values.Where(c => !IsHuman(c) && c.Reputation >= human.Reputation - 4 && World.FindLeague(c.LeagueId) is not null)
            .Where(c => NeededPosition(c) == target.MainPosition || _rng.Chance(0.15))
            .Where(c => c.Budget.TransferBudget >= ValueOf(target) * 0.8)
            .Where(c => Rating(target) >= PlayerGenerator.ClubLevel(c.Reputation) - 1)
            .ToList();
        if (buyers.Count == 0) return;
        var buyer = _rng.Pick(buyers);
        var fee = target.Transfer.Listed && target.Transfer.AskingPrice is { } ask
            ? MarketValue.Round(ask * _rng.Range(0.8, 1.02))
            : MarketValue.Round(ValueOf(target) * _rng.Range(0.9, 1.4));
        fee = Math.Min(fee, buyer.Budget.TransferBudget);
        var n = new Negotiation
        {
            Id = World.NextNegotiationId++, Type = DealType.Transfer, Stage = NegotiationStage.Pending, Incoming = true,
            PlayerId = target.Id, BuyerId = buyer.Id, SellerId = human.Id, Started = World.Date,
            Offer = new ClubOffer { Fee = fee },
        };
        World.Negotiations.Add(n);
        Notify(MessageType.Transfer, "msg.sender.assistant", T.Format("msg.incoming.subject", buyer.Name, target.Name),
            T.Format("msg.incoming.body", buyer.Name, target.Name, Fmt.Money(fee, Currency), Fmt.Money(ValueOf(target), Currency)),
            MessageAction.IncomingOffer, n.Id, required: true);
    }
}
