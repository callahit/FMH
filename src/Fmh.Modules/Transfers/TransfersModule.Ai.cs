using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Transfers: Transfermarkt der KI-Vereine.</summary>
public sealed partial class TransfersModule
{
    // ================================================================== KI

    private void MarketDaily()
    {
        foreach (var inst in World.Installments.Where(i => i.Due == World.Date).ToList())
        {
            Finance.Book(World.Clubs[inst.FromClubId], TransactionType.Installment, -inst.Amount, inst.TextRef ?? TextRef.Literal(inst.Text));
            Finance.Book(World.Clubs[inst.ToClubId], TransactionType.Installment, inst.Amount, inst.TextRef ?? TextRef.Literal(inst.Text));
            World.Installments.Remove(inst);
        }
        foreach (var lockKey in World.NegotiationLocks.Where(kv => kv.Value <= World.Date).Select(kv => kv.Key).ToList())
            World.NegotiationLocks.Remove(lockKey);

        var monday = World.Date.DayOfWeek == DayOfWeek.Monday;
        if (!monday) return;

        var aiClubs = World.Clubs.Values.Where(c => !IsHuman(c)).ToList();
        Rng.Shuffle(aiClubs);
        var window = Season.IsTransferWindowOpen();
        foreach (var club in aiClubs)
        {
            ManageAiSquadSize(club);
            // Dünne Kader: zuerst Vereinslose, sonst Talente aus der eigenen Jugend oder dem Umfeld.
            if (club.PlayerIds.Count < 22 && !SignBestFreeAgent(club, Season.NeededPosition(club)) && club.PlayerIds.Count < 21) Season.AddEmergencyPlayer(club);
            if (window && Rng.Chance(0.3)) TryAiSigning(club);
        }
        if (window && Rng.Chance(0.3)) MakeIncomingOffer();
        if (window) foreach (var club in aiClubs.Where(_ => Rng.Chance(0.15))) TryAiLoanOut(club);
        if (World.Date.Month is >= 2 and <= 5 && World.Date.Day <= 7) AiPreContracts(aiClubs);
    }

    private int TransfersThisWindow(Club club)
    {
        var from = World.Date.AddDays(-62);
        return World.Transfers.Count(t => t.ToClubId == club.Id && t.Date >= from && !t.Loan);
    }

    public double Rating(Player p) => Ratings.PositionRating(p, p.MainPosition, Data.PositionProfiles);

    /// <summary>KI kauft gezielt für die Position mit dem größten Bedarf.</summary>
    private void TryAiSigning(Club club)
    {
        var summer = World.Date.Month is 7 or 8;
        if (TransfersThisWindow(club) >= (summer ? 3 : 1)) return;
        var need = Season.NeededPosition(club);
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
            var n = new Negotiation { PlayerId = p.Id, BuyerId = club.Id, SellerId = sellerId, Type = DealType.Transfer, Offer = new ClubOffer { Fee = MarketValue.Round(ask * Rng.Range(0.85, 1.0)) } };
            var resp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
            if (resp.Kind == ResponseKind.Counter && resp.Amount <= club.Budget.TransferBudget) n.Offer.Fee = resp.Amount;
            else if (resp.Kind != ResponseKind.Accept) continue;
            // Der Verkäufer füllt die Lücke später selbst wieder auf.
            ExecuteTransfer(p, club, seller, n.Offer, contract);
            club.Budget.WageBudget = Math.Max(club.Budget.WageBudget, Budgets.WageBill(club, World));
            return;
        }
    }

    /// <summary>KI verleiht junge Ergänzungsspieler an schwächere Vereine, die auf der Position Bedarf haben.</summary>
    private void TryAiLoanOut(Club club)
    {
        if (club.PlayerIds.Count <= 22) return;
        var ranked = World.SquadOf(club).OrderByDescending(Rating).ToList();
        var candidate = ranked.Skip(16).FirstOrDefault(p => p.Age(World.Date) <= 21 && p.Loan is null && p.PreContract is null
                                                           && p.Hidden.PotentialAbility - p.Hidden.CurrentAbility >= 15);
        if (candidate is null) return;
        var target = World.Clubs.Values
            .Where(c => c.Id != club.Id && !IsHuman(c) && c.Reputation < club.Reputation && c.PlayerIds.Count < 26)
            .Where(c => Season.NeededPosition(c) == candidate.MainPosition)
            .Where(c => Rating(candidate) >= PlayerGenerator.ClubLevel(c.Reputation) - 1)
            .OrderByDescending(c => c.Reputation)
            .FirstOrDefault();
        if (target is null) return;
        ExecuteLoan(candidate, target, club, new ClubOffer { LoanMonths = 12, WageSharePercent = Rng.Pick(new[] { 50, 75, 100 }), RecallInWinter = Rng.Chance(0.3) });
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
            Season.RemoveFromClub(worst);
            worst.Contract = null;
        }
    }

    private bool ShouldAiRenew(Club club, Player p)
    {
        var rank = World.SquadOf(club).OrderByDescending(Rating).ToList().IndexOf(p);
        return rank is >= 0 and < 18 && p.Age(World.Date) <= 33;
    }

    /// <summary>KI-Vereine sichern sich ablösefreie Spieler mit auslaufendem Vertrag per Vorvertrag.</summary>
    private void AiPreContracts(List<Club> aiClubs)
    {
        foreach (var club in aiClubs.Where(_ => Rng.Chance(0.1)))
        {
            var need = Season.NeededPosition(club);
            var level = PlayerGenerator.ClubLevel(club.Reputation);
            var target = World.Players.Values
                .Where(p => p.ClubId is { } c && c != club.Id && !p.IsYouth && p.PreContract is null && p.Loan is null && CanPreContract(p))
                .Where(p => p.ClubId != HumanClub.Id || Rng.Chance(0.5))
                .Where(p => p.MainPosition == need && Rating(p) >= level - 0.5 && Rating(p) <= level + 2.5)
                .Where(p => !ShouldAiRenew(World.Clubs[p.ClubId!.Value], p) || p.ClubId == HumanClub.Id)
                .FirstOrDefault(p => Negotiations.RefusalReason(p, club, World, Data) is null);
            if (target is null) continue;
            var contract = Negotiations.SuggestContract(target, club, World, Data);
            target.PreContract = new PreContract { ClubId = club.Id, Contract = MakeContract(contract, new DateOnly(World.SeasonYear + 1, 7, 1), club) };
            if (target.ClubId == HumanClub.Id)
                Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.preContractLost.subject", target.Name),
                    TR("msg.preContractLost.body", target.Name, club.Name));
        }
    }

    /// <summary>Ein KI-Verein bietet für einen Spieler des menschlichen Vereins (Pflicht-Entscheidung im Posteingang).</summary>
    private void MakeIncomingOffer()
    {
        if (Board.AwaitingNewJob) return; // ohne Verein entscheidet der Interimstrainer
        var human = HumanClub;
        if (World.Date.Month == 7 && World.Date.Day < 15) return; // Neue Manager bekommen zwei Wochen Ruhe.
        if (OpenNegotiations.Any(n => n.Incoming)) return;
        var squad = World.SquadOf(human).Where(p => p.Loan is null && p.PreContract is null).ToList();
        if (squad.Count <= 18) return;
        var listed = squad.Where(p => p.Transfer.Listed).ToList();
        var target = listed.Count > 0 && Rng.Chance(0.7) ? Rng.Pick(listed) : Rng.Pick(squad.OrderByDescending(Rating).Take(14).ToList());
        var buyers = World.Clubs.Values.Where(c => !IsHuman(c) && c.Reputation >= human.Reputation - 4 && World.FindLeague(c.LeagueId) is not null)
            .Where(c => Season.NeededPosition(c) == target.MainPosition || Rng.Chance(0.15))
            .Where(c => c.Budget.TransferBudget >= ValueOf(target) * 0.8)
            .Where(c => Rating(target) >= PlayerGenerator.ClubLevel(c.Reputation) - 1)
            .ToList();
        if (buyers.Count == 0) return;
        var buyer = Rng.Pick(buyers);
        var fee = target.Transfer.Listed && target.Transfer.AskingPrice is { } ask
            ? MarketValue.Round(ask * Rng.Range(0.8, 1.02))
            : MarketValue.Round(ValueOf(target) * Rng.Range(0.9, 1.4));
        fee = Math.Min(fee, buyer.Budget.TransferBudget);
        var n = new Negotiation
        {
            Id = World.NextNegotiationId++, Type = DealType.Transfer, Stage = NegotiationStage.Pending, Incoming = true,
            PlayerId = target.Id, BuyerId = buyer.Id, SellerId = human.Id, Started = World.Date,
            Offer = new ClubOffer { Fee = fee },
        };
        World.Negotiations.Add(n);
        Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.incoming.subject", buyer.Name, target.Name),
            TR("msg.incoming.body", buyer.Name, target.Name, TextArg.Money(fee, Currency), TextArg.Money(ValueOf(target), Currency)),
            MessageAction.IncomingOffer, n.Id, required: true);
    }
}
