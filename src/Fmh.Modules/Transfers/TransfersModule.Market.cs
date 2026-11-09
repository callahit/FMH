using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Transfers: Anfragen, Bieterkonkurrenz, Deadline Day (E2) und Vorkauf.</summary>
public sealed partial class TransfersModule
{
    private const int CompetitionDays = 3;

    /// <summary>Hält einen akzeptierten Vereinsdeal an, wenn ein Vorkaufsrecht greift.</summary>
    private bool HoldForPreemption(Negotiation n, Player p, ClubOffer offer)
    {
        if (ActivePreemption(p) is not { } clause) return false;
        if (clause.BeneficiaryClubId == n.BuyerId) return false;
        if (World.PreemptionOffers.Any(o => o.PlayerId == p.Id && o.BeneficiaryClubId == clause.BeneficiaryClubId)) return true;

        var pending = new PreemptionOffer
        {
            ClauseId = clause.Id, NegotiationId = n.Id, PlayerId = p.Id,
            BeneficiaryClubId = clause.BeneficiaryClubId, Fee = offer.Fee,
            Expires = World.Date.AddDays(3),
        };

        if (clause.BeneficiaryClubId == HumanClub.Id)
        {
            World.PreemptionOffers.Add(pending);
            n.Stage = NegotiationStage.Pending;
            Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.preemption.subject", p.Name),
                TR("msg.preemption.body", p.Name, TextArg.Money(offer.Fee, Currency)),
                MessageAction.Preemption, n.Id, required: true);
            return true;
        }

        var beneficiary = World.Clubs[clause.BeneficiaryClubId];
        if (AiWantsPreemption(beneficiary, p, offer.Fee))
        {
            clause.Active = false;
            n.Stage = NegotiationStage.Withdrawn;
            ExecuteTransfer(p, beneficiary, World.Clubs[n.SellerId ?? p.ClubId!.Value], new ClubOffer { Fee = offer.Fee },
                Negotiations.SuggestContract(p, beneficiary, World, Data));
            return true;
        }
        return false;
    }

    private bool AiWantsPreemption(Club club, Player p, long fee) =>
        !IsHuman(club) && fee <= club.Budget.TransferBudget
        && p.Age(World.Date) <= 24 && p.Hidden.PotentialAbility - p.Hidden.CurrentAbility >= 12
        && Rating(p) >= PlayerGenerator.ClubLevel(club.Reputation) - 1;

    /// <summary>Tägliche Marktpflege vor der KI: Anfragen, Bieterwahl, Deadline Day.</summary>
    private void MarketLifecycle()
    {
        ProcessEnquiries();
        ResolveSellerChoices();
        ResolvePlayerChoices();
        DeadlineDayTick();
    }

    private void MarkClubAgreed(Negotiation n, Player p, Club buyer)
    {
        n.ClubAgreed = true;
        n.ClubAgreedOn = World.Date;
        n.SellerChosen = false;
        if (n.Stage is NegotiationStage.ClubTalks or NegotiationStage.Talks)
            n.Stage = NegotiationStage.Talks;
        if (n.Contract.WeeklyWage <= 0)
            n.Contract = Negotiations.SuggestContract(p, buyer, World, Data);
        // Neue Zusage öffnet die Bieterwahl erneut.
        foreach (var other in Negotiations.ActiveBids(World, p.Id).Where(b => b.Id != n.Id))
            other.SellerChosen = false;
        NotifyCompetitionIfNeeded(p);
        ResolveSellerChoices(playerId: n.PlayerId);
        TryFinishNegotiation(n);
    }

    /// <summary>Schließt ab, sobald Verein (und bei Transfers der Spieler) zugestimmt haben und keine Konkurrenz mehr offen ist.</summary>
    private void TryFinishNegotiation(Negotiation n)
    {
        if (n.Stage is NegotiationStage.Completed or NegotiationStage.Failed or NegotiationStage.Withdrawn) return;
        if (!n.ClubAgreed) return;
        if (n.Type == DealType.Transfer && !n.PlayerAgreed) return;
        if (n.Type == DealType.Transfer && !n.SellerChosen && HasOpenCompetition(n.PlayerId, n))
            return;
        if ((n.Type is DealType.Transfer or DealType.Loan) && !Season.IsTransferWindowOpen(World.Clubs[n.BuyerId]))
        {
            n.Response = TR("neg.error.window");
            n.Stage = NegotiationStage.Failed;
            return;
        }
        if (n.Type == DealType.Loan)
        {
            Complete(n);
            return;
        }
        if (n.Type == DealType.Transfer)
        {
            if (!n.SellerChosen) n.SellerChosen = true;
            Complete(n);
        }
    }

    private bool HasOpenCompetition(int playerId, Negotiation self)
    {
        var bids = Negotiations.ActiveBids(World, playerId).Where(b => b.Id != self.Id).ToList();
        if (bids.Count == 0) return false;
        // Andere Zusagen oder noch laufende Bieter im 3-Tage-Fenster.
        var agreedOn = self.ClubAgreedOn ?? self.Started;
        return bids.Any(b => b.ClubAgreed || World.Date < agreedOn.AddDays(CompetitionDays));
    }

    private void NotifyCompetitionIfNeeded(Player p)
    {
        var agreed = Negotiations.ActiveBids(World, p.Id).Where(b => b.ClubAgreed).ToList();
        if (agreed.Count < 2) return;
        if (agreed.Any(b => b.BuyerId == HumanClub.Id || b.SellerId == HumanClub.Id))
            Notify(MessageType.Transfer, "msg.sender.assistant",
                TR("msg.competition.subject", p.Name),
                TR("msg.competition.body", p.Name, agreed.Count));
    }

    private void ProcessEnquiries()
    {
        foreach (var n in World.Negotiations.Where(n => n.Stage == NegotiationStage.Enquiry && n.EnquiryDue is { } due && due <= World.Date).ToList())
        {
            var p = World.Players[n.PlayerId];
            if (n.SellerId is not { } sellerId || !World.Clubs.TryGetValue(sellerId, out var seller))
            {
                FailSellerAbort(n);
                continue;
            }
            // Menschlicher Verkäufer: Anfrage wird zur Pflicht-Entscheidung (eingehend).
            if (IsHuman(seller))
            {
                n.Stage = NegotiationStage.Pending;
                n.Incoming = true;
                n.Offer.Fee = Math.Max(n.Offer.Fee, MarketValue.Round(Negotiations.AskingPrice(p, seller, World, Data) * 0.9));
                Notify(MessageType.Transfer, "msg.sender.assistant",
                    TR("msg.enquiry.subject", World.Clubs[n.BuyerId].Name, p.Name),
                    TR("msg.enquiry.body", World.Clubs[n.BuyerId].Name, p.Name, TextArg.Money(n.Offer.Fee, Currency)),
                    MessageAction.IncomingOffer, n.Id, required: true);
                continue;
            }
            if (AiAcceptsEnquiry(p, seller))
            {
                n.Stage = NegotiationStage.Talks;
                n.Response = TR("neg.enquiry.accepted");
                n.Contract = Negotiations.SuggestContract(p, World.Clubs[n.BuyerId], World, Data);
                if (n.BuyerId == HumanClub.Id)
                    Notify(MessageType.Transfer, "msg.sender.assistant",
                        TR("msg.enquiryOk.subject", p.Name), TR("msg.enquiryOk.body", seller.Name, p.Name));
            }
            else
            {
                n.Response = TR("neg.enquiry.rejected");
                FailSellerAbort(n);
                if (n.BuyerId == HumanClub.Id)
                    Notify(MessageType.Transfer, "msg.sender.assistant",
                        TR("msg.enquiryNo.subject", p.Name), TR("msg.enquiryNo.body", seller.Name, p.Name));
            }
        }
    }

    /// <summary>KI-Verkäufer: gelistet oder unbedeutend → oft ja; Stammspieler → selten.</summary>
    private bool AiAcceptsEnquiry(Player p, Club seller)
    {
        if (p.Transfer.Listed || p.Transfer.LoanListed) return true;
        var squad = World.SquadOf(seller).OrderByDescending(x => Ratings.Strength(x, Data.PositionProfiles)).ToList();
        var rank = squad.IndexOf(p);
        if (rank is >= 0 and < 4 && seller.Tactic.Lineup.Contains(p.Id)) return Rng.Chance(0.15);
        if (rank is >= 0 and < 11) return Rng.Chance(0.45);
        return Rng.Chance(0.85);
    }

    private void ResolveSellerChoices(int? playerId = null)
    {
        var groups = World.Negotiations
            .Where(n => n.ClubAgreed && n.Type == DealType.Transfer && Negotiations.IsOpenStage(n.Stage))
            .Where(n => playerId is null || n.PlayerId == playerId)
            .GroupBy(n => n.PlayerId);
        foreach (var g in groups)
        {
            var agreed = g.ToList();
            if (agreed.Count == 0) continue;
            var p = World.Players[g.Key];
            var othersOpen = Negotiations.ActiveBids(World, g.Key).Any(b => !b.ClubAgreed);
            var firstAgree = agreed.Min(a => a.ClubAgreedOn ?? a.Started);
            // Ein Bieter ohne Konkurrenz: sofort. Sonst bis zu 3 Tage auf weitere Zusagen warten (E2-S1).
            if (agreed.Count == 1 && !othersOpen)
            {
                agreed[0].SellerChosen = true;
                TryFinishNegotiation(agreed[0]);
                continue;
            }
            if (World.Date < firstAgree.AddDays(CompetitionDays)) continue;
            if (agreed.Count == 1)
            {
                agreed[0].SellerChosen = true;
                TryFinishNegotiation(agreed[0]);
                continue;
            }

            var winner = Negotiations.PickSellerFavourite(agreed, p, World, Data);
            winner.SellerChosen = true;
            foreach (var loser in agreed.Where(a => a.Id != winner.Id))
            {
                loser.Response = TR("neg.competition.lost", World.Clubs[winner.BuyerId].Name);
                FailSellerAbort(loser);
            }
            // Laufende Bieter ohne Zusage verlieren ebenfalls.
            foreach (var open in Negotiations.ActiveBids(World, g.Key).Where(b => !b.ClubAgreed).ToList())
            {
                open.Response = TR("neg.competition.lost", World.Clubs[winner.BuyerId].Name);
                FailSellerAbort(open);
            }
            if (winner.BuyerId == HumanClub.Id || winner.SellerId == HumanClub.Id)
                Notify(MessageType.Transfer, "msg.sender.assistant",
                    TR("msg.competitionWon.subject", p.Name),
                    TR("msg.competitionWon.body", p.Name, World.Clubs[winner.BuyerId].Name,
                        TextArg.Money(winner.Offer.Fee, Currency)));
            TryFinishNegotiation(winner);
        }
    }

    /// <summary>Mehrere Vereine mit Spielerzusage: der Spieler wählt nach Ruf/Rolle/Gehalt.</summary>
    private void ResolvePlayerChoices()
    {
        var groups = World.Negotiations
            .Where(n => n.Type == DealType.Transfer && n.ClubAgreed && n.PlayerAgreed && n.SellerChosen
                        && Negotiations.IsOpenStage(n.Stage))
            .GroupBy(n => n.PlayerId)
            .Where(g => g.Count() > 1);
        foreach (var g in groups)
        {
            var p = World.Players[g.Key];
            var ready = g.ToList();
            var pick = Negotiations.PickPlayerFavourite(ready, p, World, Data);
            foreach (var loser in ready.Where(r => r.Id != pick.Id))
            {
                loser.Response = TR("neg.competition.playerChose", World.Clubs[pick.BuyerId].Name);
                FailPlayerAbort(loser);
            }
            TryFinishNegotiation(pick);
        }
    }

    private void DeadlineDayTick()
    {
        var country = World.Country(HumanClub.CountryCode);
        var tw = country.TransferWindows;
        var start = country.Season.StartMonth;
        var summerEnd = ScheduleGenerator.ParseDate(tw.SummerEnd, World.SeasonYear, start);
        var winterEnd = ScheduleGenerator.ParseDate(tw.WinterEnd, World.SeasonYear, start);
        if (World.Date != summerEnd && World.Date != winterEnd) return;

        var today = World.Transfers.Where(t => t.Date == World.Date && !t.Loan).Take(12).ToList();
        var lines = today.Select(t =>
        {
            var name = World.Players.TryGetValue(t.PlayerId, out var pl) ? pl.Name : "?";
            var to = t.ToClubId is { } tid && World.Clubs.TryGetValue(tid, out var c) ? c.Name : "?";
            return $"{name} → {to}";
        }).ToList();
        Notify(MessageType.Transfer, "msg.sender.press",
            TR("msg.deadline.subject"),
            lines.Count == 0
                ? TR("msg.deadline.empty")
                : TR("msg.deadline.body", TextArg.Join(" · ", lines.Cast<object?>().ToList())));

        // KI: unter Mindestkader dringend verpflichten.
        foreach (var club in World.Clubs.Values.Where(c => !IsHuman(c) && WindowOpen(c)))
        {
            var min = Finance.DivisionFinance(club).MinSquad;
            for (var guard = 0; guard < min && club.PlayerIds.Count < min; guard++)
            {
                if (!SignBestFreeAgent(club, Season.NeededPosition(club), withinBudget: false))
                    Season.AddEmergencyPlayer(club);
            }
        }
    }
}
