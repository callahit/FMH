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
        .Where(n => n.Stage is NegotiationStage.ClubTalks or NegotiationStage.PlayerTalks or NegotiationStage.Pending)
        .Where(n => n.BuyerId == HumanClub.Id || n.SellerId == HumanClub.Id)
        .ToList();

    public Negotiation? OpenNegotiationFor(Player p) => OpenNegotiations.FirstOrDefault(n => n.PlayerId == p.Id && !n.Incoming);

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
        n.Response = TR(resp.TextKey, TextArg.Money(resp.Amount, Currency));
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
        n.Response = TR(resp.TextKey, TextArg.Join("", new object?[] { TextArg.Money(resp.Amount, Currency), TR("unit.perWeek") }));
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
        // Verein ist sich einig: der Spieler verhandelt selbst mit dem Käufer.
        n.Contract = Negotiations.SuggestContract(p, buyer, World, Data);
        if (Negotiations.RefusalReason(p, buyer, World, Data) is { } refusal)
        {
            n.Stage = NegotiationStage.Failed;
            return TR("neg.incoming.playerRefused", p.Name, TR(refusal));
        }
        n.Stage = NegotiationStage.PlayerTalks;
        Complete(n);
        return TR("neg.incoming.sold", p.Name, buyer.Name, TextArg.Money(n.Offer.Fee, Currency));
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
}
