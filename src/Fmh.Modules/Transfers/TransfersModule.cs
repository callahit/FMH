using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Transfermarkt, Verträge, Leihen und KI-Transfers (Kapitel 7).</summary>
public sealed partial class TransfersModule : GameModule, ITransfersService
{
    public override string Id => "transfers";
    public override IReadOnlyList<string> DependsOn => new[] { "matchday" };
    public override void OnDay(DayContext day) => MarketDaily();

    public long ValueOf(Player p) => MarketValue.Of(p, World, Data.PositionProfiles);

    public IEnumerable<Player> FreeAgents => World.Players.Values.Where(p => p.ClubId is null && !p.Retired && !p.IsYouth);

    /// <summary>Offene Verhandlungen des menschlichen Managers (eigene Anfragen und eingehende Angebote).</summary>
    public IReadOnlyList<Negotiation> OpenNegotiations => World.Negotiations
        .Where(n => Negotiations.IsOpenStage(n.Stage))
        .Where(n => n.BuyerId == HumanClub.Id || n.SellerId == HumanClub.Id)
        .ToList();

    public Negotiation? OpenNegotiationFor(Player p) =>
        OpenNegotiations.FirstOrDefault(n => n.PlayerId == p.Id && n.BuyerId == HumanClub.Id && !n.Incoming);

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
            if (type is DealType.Transfer or DealType.Loan && !Season.IsTransferWindowOpen()) return (null, "neg.error.window");
            if (type == DealType.FreeAgent && p.ClubId is not null) return (null, "neg.error.notFree");
            if (type == DealType.PreContract && !CanPreContract(p)) return (null, "neg.error.preContract");
            if (p.Loan is not null && type != DealType.PreContract) return (null, "neg.error.onLoan");
            if (Negotiations.IsLocked(World, p.Id, buyer.Id)) return (null, "neg.error.locked");
            if (p.PreContract is not null) return (null, "neg.error.preContractTaken");
            if (type != DealType.Loan && Negotiations.RefusalReason(p, buyer, World, Data) is { } refusal) return (null, refusal);
            if (type == DealType.Loan && p.ClubId is { } owner && LoanLimits.Check(World, Data, p, World.Clubs[owner], buyer, 0) is { } limit)
                return (null, limit);
        }

        // Transfer/Leihe starten mit Anfrage an den Verkäufer (E2-S2); Ausstiegsklausel und Vereinslose überspringen sie.
        var enquiry = type is DealType.Transfer or DealType.Loan
                      && p.ClubId is not null
                      && p.Contract?.ReleaseClause is null;
        var n = new Negotiation
        {
            Id = World.NextNegotiationId++,
            Type = type,
            PlayerId = p.Id,
            BuyerId = buyer.Id,
            SellerId = type is DealType.FreeAgent or DealType.Renewal ? null : p.ClubId,
            Started = World.Date,
            Stage = enquiry ? NegotiationStage.Enquiry
                : type is DealType.Transfer or DealType.Loan ? NegotiationStage.Talks : NegotiationStage.PlayerTalks,
            EnquiryDue = enquiry ? World.Date.AddDays(Rng.Next(1, 3)) : null,
            Contract = Negotiations.SuggestContract(p, buyer, World, Data),
        };
        if (type == DealType.Transfer && p.ClubId is { } sid)
            n.Offer.Fee = MarketValue.Round(Negotiations.AskingPrice(p, World.Clubs[sid], World, Data) * 0.85);
        if (type == DealType.Renewal && p.Contract is { } c)
        {
            n.Contract.Status = c.Status;
            n.Contract.WeeklyWage = Math.Max(c.WeeklyWage, n.Contract.WeeklyWage);
        }
        if (n.Stage == NegotiationStage.Talks)
            n.Response = TR("neg.enquiry.accepted");
        World.Negotiations.Add(n);
        if (enquiry)
            n.Response = TR("neg.enquiry.pending", TextArg.OfDate(n.EnquiryDue!.Value));
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
        if (!Negotiations.CanSubmitClub(n) || n.SellerId is not { } sellerId) return new(ResponseKind.Reject, "neg.error.stage");
        if ((n.Type is DealType.Transfer or DealType.Loan) && !Season.IsTransferWindowOpen(buyer))
            return new(ResponseKind.Reject, "neg.error.window");
        var seller = World.Clubs[sellerId];
        if (offer.SwapPlayerId is { } swap && World.Players[swap].ClubId != buyer.Id) return new(ResponseKind.Reject, "neg.error.swap");
        offer.InstallmentYears = Math.Clamp(offer.InstallmentYears, 0, 3);
        offer.InstallmentCount = Math.Clamp(offer.InstallmentCount, 0, TransferTerms.MaxInstallments);
        offer.DownPayment = Math.Max(0, offer.DownPayment);
        if (n.Type == DealType.Transfer)
        {
            TransferTerms.NormalizePaymentPlan(offer, offer.Fee);
            if (offer.DownPayment > buyer.Budget.TransferBudget) return new(ResponseKind.Reject, "neg.error.budget");
        }
        offer.SellOnPercent = Math.Clamp(offer.SellOnPercent, 0, 50);
        offer.LoanMonths = TransferTerms.ClampLoanMonths(offer.LoanMonths);
        offer.WageSharePercent = Math.Clamp(offer.WageSharePercent, 0, 100);
        offer.AppearanceBonus = Math.Max(0, offer.AppearanceBonus);
        offer.GoalBonus = Math.Max(0, offer.GoalBonus);
        offer.LoanFee = Math.Max(0, offer.LoanFee);
        offer.PurchaseConditionValue = Math.Clamp(offer.PurchaseConditionValue <= 0 ? 50 : offer.PurchaseConditionValue, 1, 100);
        if (n.Type == DealType.Loan && offer.LoanFee > buyer.Budget.TransferBudget) return new(ResponseKind.Reject, "neg.error.budget");
        if (n.Type == DealType.Loan && LoanLimits.Check(World, Data, p, seller, buyer, offer.LoanMonths) is { } limit) return new(ResponseKind.Reject, limit);

        n.ClubRounds++;
        n.Offer = offer.Clone();
        var resp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
        n.Response = TR(resp.TextKey, TextArg.Money(resp.Amount, Currency));
        switch (resp.Kind)
        {
            case ResponseKind.Accept:
                if (n.Type == DealType.Transfer && HoldForPreemption(n, p, offer))
                    return resp;
                MarkClubAgreed(n, p, buyer);
                break;
            case ResponseKind.Counter:
                n.CounterFee = resp.Amount;
                if (n.ClubRounds >= Negotiations.MaxRounds) FailSellerAbort(n);
                break;
            default:
                FailSellerAbort(n);
                break;
        }
        return resp;
    }

    public NegotiationResponse SubmitContract(Negotiation n, ContractOffer offer)
    {
        var p = World.Players[n.PlayerId];
        var buyer = World.Clubs[n.BuyerId];
        if (!Negotiations.CanSubmitPlayer(n)) return new(ResponseKind.Reject, "neg.error.stage");
        if ((n.Type is DealType.Transfer or DealType.Loan) && !Season.IsTransferWindowOpen(buyer))
            return new(ResponseKind.Reject, "neg.error.window");
        var currentWage = n.Type == DealType.Renewal ? p.Contract?.WeeklyWage ?? 0 : 0;
        if (Budgets.WageBill(buyer, World) - currentWage + offer.WeeklyWage > buyer.Budget.WageBudget)
            return new(ResponseKind.Reject, "neg.error.wageBudget");
        if (offer.SigningFee > Math.Max(0, buyer.Budget.Balance)) return new(ResponseKind.Reject, "neg.error.balance");

        offer.ExtensionOptionYears = Math.Clamp(offer.ExtensionOptionYears, 0, TransferTerms.MaxExtensionOptionYears);
        n.PlayerRounds++;
        n.Contract = offer.Clone();
        var resp = Negotiations.EvaluateContract(n, p, buyer, World, Data);
        n.Response = TR(resp.TextKey, TextArg.Join("", new object?[] { TextArg.Money(resp.Amount, Currency), TR("unit.perWeek") }));
        switch (resp.Kind)
        {
            case ResponseKind.Accept:
                n.PlayerAgreed = true;
                if (n.Type is DealType.FreeAgent or DealType.Renewal or DealType.PreContract)
                    Complete(n);
                else
                    TryFinishNegotiation(n);
                break;
            case ResponseKind.Counter:
                n.WageDemand = (int)resp.Amount;
                if (n.PlayerRounds >= Negotiations.MaxRounds) FailPlayerAbort(n);
                break;
            default:
                FailPlayerAbort(n);
                break;
        }
        return resp;
    }

    public void Withdraw(Negotiation n)
    {
        // Ausstieg des Käufers: keine 14-Tage-Sperre (E2-S1).
        if (Negotiations.IsOpenStage(n.Stage)) n.Stage = NegotiationStage.Withdrawn;
    }

    /// <summary>Verkäufer bricht ab → 14 Tage Sperre.</summary>
    private void FailSellerAbort(Negotiation n)
    {
        n.Stage = NegotiationStage.Failed;
        World.NegotiationLocks[Negotiations.LockKey(n.PlayerId, n.BuyerId)] = World.Date.AddDays(Negotiations.LockDays);
    }

    /// <summary>Spieler bricht ab → keine Sperre; der Käufer darf später erneut anfragen.</summary>
    private void FailPlayerAbort(Negotiation n) => n.Stage = NegotiationStage.Failed;

    private void Fail(Negotiation n) => FailSellerAbort(n);

    /// <summary>Antwort auf ein eingehendes Angebot eines KI-Vereins.</summary>
    public TextRef RespondIncoming(Negotiation n, ResponseKind answer, long counterFee = 0)
    {
        var p = World.Players[n.PlayerId];
        var buyer = World.Clubs[n.BuyerId];
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.IncomingOffer && m.ActionRefId == n.Id)) m.Resolved = true;
        if (n.Stage != NegotiationStage.Pending) return TR("neg.error.stage");

        if (answer == ResponseKind.Reject)
        {
            n.Stage = NegotiationStage.Failed;
            return TR("neg.incoming.rejected", buyer.Name);
        }
        if (answer == ResponseKind.Counter)
        {
            var limit = Math.Min(buyer.Budget.TransferBudget, (long)(ValueOf(p) * 1.5));
            if (counterFee > limit)
            {
                n.Stage = NegotiationStage.Failed;
                return TR("neg.incoming.counterRejected", buyer.Name);
            }
            n.Offer.Fee = counterFee;
        }
        // Verein ist sich einig: Spieler und Konkurrenz laufen über denselben Abschlussweg (E2).
        n.Contract = Negotiations.SuggestContract(p, buyer, World, Data);
        if (Negotiations.RefusalReason(p, buyer, World, Data) is { } refusal)
        {
            n.Stage = NegotiationStage.Failed;
            return TR("neg.incoming.playerRefused", p.Name, TR(refusal));
        }
        n.Incoming = false;
        n.Stage = NegotiationStage.Talks;
        n.PlayerAgreed = true;
        MarkClubAgreed(n, p, buyer);
        return n.Stage == NegotiationStage.Completed
            ? TR("neg.incoming.sold", p.Name, buyer.Name, TextArg.Money(n.Offer.Fee, Currency))
            : TR("neg.incoming.waiting", p.Name, buyer.Name);
    }

    /// <summary>Überlässt offene Pflicht-Entscheidungen dem Assistenten: Angebote werden abgelehnt.</summary>
    public void DelegateDecisions()
    {
        // Offene Sponsorenangebote: je Werbeplatz das beste annehmen.
        foreach (var best in HumanClub.SponsorOffers.GroupBy(o => (o.Slot, o.Index)).Select(g => g.MaxBy(o => o.PerSeason)!).ToList())
            Sponsors.AcceptSponsor(best);
        foreach (var m in PendingDecisions)
        {
            switch (m.Action)
            {
                case MessageAction.IncomingOffer when World.Negotiations.FirstOrDefault(n => n.Id == m.ActionRefId) is { } n:
                    RespondIncoming(n, ResponseKind.Reject);
                    break;
                case MessageAction.SeasonGoal:
                    Board.ChooseSeasonGoal(Board.ExpectedGoal(HumanClub));
                    break;
                case MessageAction.Event when Events.EventOf(m) is { } e:
                    Events.ResolveEvent(e, Data.Events.FirstOrDefault(d => d.Id == e.EventId)?.Options.LastOrDefault()?.Id ?? "");
                    break;
                case MessageAction.PressConference when World.PendingPress is { } conf:
                    Press.AnswerPress(conf.Questions.Select(_ => PressAnswer.Modest).ToList());
                    break;
                case MessageAction.JobOffer when HumanManager.Career.Offers.FirstOrDefault(o => o.ClubId == m.ActionRefId) is { } offer && Board.AwaitingNewJob:
                    Board.AcceptJob(offer);
                    break;
            }
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
        var contract = p.Contract is { } c ? new ContractOffer { Years = Math.Max(1, (c.Until.Year - World.Date.Year)), WeeklyWage = c.WeeklyWage, Status = c.Status, RelegationPercent = c.RelegationPercent, PromotionPercent = c.PromotionPercent } : Negotiations.SuggestContract(p, HumanClub, World, Data);
        p.Loan = null;
        _activeLoans = null;
        p.ClubId = parent.Id;
        HumanClub.PlayerIds.Remove(p.Id);
        parent.PlayerIds.Add(p.Id);
        ExecuteTransfer(p, HumanClub, parent, new ClubOffer { Fee = fee }, contract);
        return null;
    }

    /// <summary>Rückholung im Winterfenster: vertraglich vereinbart oder weil die Einsatzgarantie verfehlt wurde.</summary>
    public string? RecallLoan(Player p)
    {
        if (p.Loan is not { } loan || loan.ParentClubId != HumanClub.Id || !(loan.RecallInWinter || TransferTerms.GuaranteeBreached(loan)))
            return "neg.error.noRecall";
        if (!InWinterWindow(HumanClub)) return "neg.error.window";
        ReturnLoan(p);
        return null;
    }

    /// <summary>Rückkaufklausel ziehen (Must-now E1-S4), wenn das Wechselfenster offen ist.</summary>
    public string? ExerciseBuyBack(Player p)
    {
        if (ActiveBuyBack(p, HumanClub.Id) is not { } clause) return "neg.error.noBuyBack";
        if (!Season.IsTransferWindowOpen()) return "neg.error.window";
        if (p.ClubId is not { } ownerId || ownerId == HumanClub.Id) return "neg.error.own";
        var owner = World.Clubs[ownerId];
        if (!Season.IsTransferWindowOpen(owner)) return "neg.error.window";
        if (clause.Amount > HumanClub.Budget.TransferBudget) return "neg.error.budget";
        var contract = Negotiations.SuggestContract(p, HumanClub, World, Data);
        clause.Active = false;
        foreach (var c in World.Clauses.Where(c => c.Active && c.PlayerId == p.Id && c.Kind == ClauseKind.Preemption && c.BeneficiaryClubId == HumanClub.Id))
            c.Active = false;
        ExecuteTransfer(p, HumanClub, owner, new ClubOffer { Fee = clause.Amount }, contract);
        return null;
    }

    public bool CanExerciseBuyBack(Player p) =>
        ActiveBuyBack(p, HumanClub.Id) is not null && Season.IsTransferWindowOpen()
        && p.ClubId is { } oid && oid != HumanClub.Id;

    /// <summary>Vorkaufsrecht: Mitbieten oder verzichten (Must-now E1-S4).</summary>
    public string? ResolvePreemption(PreemptionOffer offer, bool match)
    {
        if (!World.PreemptionOffers.Contains(offer) || offer.BeneficiaryClubId != HumanClub.Id) return "neg.error.stage";
        if (match && offer.Fee > HumanClub.Budget.TransferBudget) return "neg.error.budget";
        World.PreemptionOffers.Remove(offer);
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.Preemption && m.ActionRefId == offer.NegotiationId))
            m.Resolved = true;
        var n = World.Negotiations.FirstOrDefault(x => x.Id == offer.NegotiationId);
        var p = World.Players[offer.PlayerId];
        if (match)
        {
            if (ActivePreemption(p) is { } clause) clause.Active = false;
            if (n is not null) n.Stage = NegotiationStage.Withdrawn;
            var seller = p.ClubId is { } sid ? World.Clubs[sid] : null;
            if (seller is null) return "neg.error.stage";
            ExecuteTransfer(p, HumanClub, seller, new ClubOffer { Fee = offer.Fee }, Negotiations.SuggestContract(p, HumanClub, World, Data));
            return null;
        }
        // Dritter darf fortsetzen.
        if (n is not null && n.Stage == NegotiationStage.Pending && !n.Incoming)
        {
            n.Stage = NegotiationStage.Talks;
            n.Contract = Negotiations.SuggestContract(p, World.Clubs[n.BuyerId], World, Data);
            MarkClubAgreed(n, p, World.Clubs[n.BuyerId]);
        }
        return null;
    }

}
