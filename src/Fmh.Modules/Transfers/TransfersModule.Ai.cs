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

        _activeLoans = null;
        var monday = World.Date.DayOfWeek == DayOfWeek.Monday;
        if (!monday) return;

        var aiClubs = World.Clubs.Values.Where(c => !IsHuman(c)).ToList();
        Rng.Shuffle(aiClubs);
        _loanPool = null;
        _needs.Clear();
        foreach (var club in aiClubs)
        {
            ManageAiSquadSize(club);
            // Dünne Kader: zuerst Vereinslose (im Gehaltsbudget, außer der Spieltagskader ist nicht mehr voll), sonst Talente
            // aus der eigenen Jugend oder dem Umfeld.
            // Unter dem Mindestkader sofort bis zum Mindestkader, darüber eine Verpflichtung je Woche bis zur üblichen Größe.
            var division = Finance.DivisionFinance(club);
            for (var guard = 0; guard <= division.MinSquad && club.PlayerIds.Count < Math.Max(division.MinSquad, division.SquadSize - 3); guard++)
            {
                var below = club.PlayerIds.Count < division.MinSquad;
                var critical = club.PlayerIds.Count < 11 + World.Country(club.CountryCode).Squad.BenchSize;
                if (!SignBestFreeAgent(club, Season.NeededPosition(club), withinBudget: !critical) && below) Season.AddEmergencyPlayer(club);
                if (!below) break;
            }
            // Das Transferfenster des eigenen Verbands entscheidet (die Fenster der Länder unterscheiden sich).
            if (!Season.IsTransferWindowOpen(club)) continue;
            var profile = Data.TransferRules.Profile(ClubStrategy.Of(World, Data, club));
            if (Rng.Chance(profile.SigningChance)) TryAiSigning(club, profile);
            if (Rng.Chance(Math.Min(1, profile.BorrowChance * ClubStrategy.Tendency(World, club).LoanAffinity))) TryAiBorrow(club);
            if (profile.LendBeyondRank > 0 && Rng.Chance(0.25)) TryAiLoanOut(club, profile);
            // Neuzugänge im Fenster nachmelden, solange Plätze frei sind.
            Registration.Fill(World, club, Data, keep: true);
        }
        if (Season.IsTransferWindowOpen() && Rng.Chance(0.3)) MakeIncomingOffer();
        if (World.Date.Month is >= 2 and <= 5 && World.Date.Day <= 7) AiPreContracts(aiClubs);
    }

    private int TransfersThisWindow(Club club)
    {
        var from = World.Date.AddDays(-62);
        return World.Transfers.Count(t => t.ToClubId == club.Id && t.Date >= from && !t.Loan);
    }

    public double Rating(Player p) => Ratings.PositionRating(p, p.MainPosition, Data.PositionProfiles);

    /// <summary>
    /// KI kauft gezielt für die Position mit dem größten Bedarf, und zwar nur Spieler, deren Marktwert ins Transferbudget passt.
    /// Ausbildungsvereine bevorzugen junge Spieler mit Potenzial.
    /// </summary>
    private void TryAiSigning(Club club, StrategyProfile profile)
    {
        var summer = World.Date.Month is >= 5 and <= 10;
        if (TransfersThisWindow(club) >= (summer ? profile.MaxSummerSignings : profile.MaxWinterSignings)) return;
        var need = NeedOf(club);
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        var current = World.SquadOf(club).Where(p => p.MainPosition == need).Select(Rating).DefaultIfEmpty(0).Max();
        var minRating = Math.Max(current + 0.3, level - 1.0);
        var youthAge = profile.YouthFocusAge;
        double Score(Player p, double r) =>
            youthAge > 0 && p.Age(World.Date) <= youthAge ? r + (p.Hidden.PotentialAbility - p.Hidden.CurrentAbility) / 20.0 : r;
        var budget = club.Budget.TransferBudget;
        var candidates = World.Players.Values
            .Where(p => !p.Retired && !p.IsYouth && p.ClubId != club.Id && p.ClubId != HumanClub.Id && p.Loan is null && p.PreContract is null)
            .Where(p => p.MainPosition == need && p.Age(World.Date) <= 31)
            .Select(p => (p, r: Rating(p)))
            .Where(x => x.r >= minRating && x.r <= level + 3)
            .Where(x => x.p.ClubId is null || World.Clubs[x.p.ClubId.Value].Reputation <= club.Reputation + 3)
            // Grobe Vorauswahl nach Marktwert, damit nicht nur unbezahlbare Spieler in die engere Wahl kommen.
            .Where(x => x.p.ClubId is null || ValueOf(x.p) <= budget)
            .OrderByDescending(x => Score(x.p, x.r))
            .Take(8)
            .ToList();
        // Bei fast vollem Kader gibt der Verein für den Neuen einen Spieler ab: den schwächsten der Position oder, wenn teurer,
        // einen Überzähligen außerhalb der besten 18. Sein Gehalt steht dann für den Neuen zur Verfügung.
        var squad = World.SquadOf(club).Where(p => p.Loan is null && !p.Transfer.Listed && p.Contract is not null).OrderByDescending(Rating).ToList();
        var replaced = club.PlayerIds.Count >= Finance.DivisionFinance(club).SquadSize - 1
            ? new[] { squad.Where(p => p.MainPosition == need).LastOrDefault(), squad.Skip(18).MaxBy(p => p.Contract!.WeeklyWage) }
                .Where(p => p is not null).MaxBy(p => p!.Contract!.WeeklyWage)
            : null;
        var freed = replaced?.Contract?.WeeklyWage ?? 0;
        // Das Gehaltsbudget bleibt, wie es ist: Ein Kauf muss hineinpassen, sonst schaukeln sich die Gehälter über die Saison hoch.
        void Done()
        {
            if (replaced is not null) SetTransferListed(replaced, true, MarketValue.Round(ValueOf(replaced) * 0.8));
        }
        var bill = WageBillOf(club);
        foreach (var (p, _) in candidates)
        {
            if (Negotiations.IsLocked(World, p.Id, club.Id) || Negotiations.RefusalReason(p, club, World, Data) is not null) continue;
            var contract = Negotiations.SuggestContract(p, club, World, Data);
            if (bill - freed + contract.WeeklyWage > club.Budget.WageBudget * 1.05) continue;
            if (p.ClubId is not { } sellerId)
            {
                ExecuteTransfer(p, club, null, new ClubOffer(), contract);
                Done();
                return;
            }
            var seller = World.Clubs[sellerId];
            var ask = Negotiations.AskingPrice(p, seller, World, Data);
            if (ask > budget) continue;
            var n = new Negotiation { PlayerId = p.Id, BuyerId = club.Id, SellerId = sellerId, Type = DealType.Transfer, Offer = new ClubOffer { Fee = MarketValue.Round(ask * Rng.Range(0.85, 1.0)) } };
            var resp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
            if (resp.Kind == ResponseKind.Counter && resp.Amount <= budget) n.Offer.Fee = resp.Amount;
            else if (resp.Kind != ResponseKind.Accept) continue;
            // Der Verkäufer füllt die Lücke später selbst wieder auf.
            ExecuteTransfer(p, club, seller, n.Offer, contract);
            Done();
            return;
        }
    }

    public bool SignBestFreeAgent(Club club, Position pos, bool withinBudget = false)
    {
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        var candidates = FreeAgents
            .Where(p => p.MainPosition == pos || p.SuitabilityFor(pos) >= Suitability.Good)
            .Select(p => (p, r: Ratings.EffectiveRating(p, pos, Data.PositionProfiles)))
            .Where(x => x.r <= level + 2 && Negotiations.RefusalReason(x.p, club, World, Data) is null)
            .OrderByDescending(x => x.r)
            .Select(x => x.p);
        var bill = WageBillOf(club);
        // Mit Budgetgrenze: der beste Spieler, dessen Gehalt noch passt (höchstens acht Verhandlungen).
        foreach (var p in candidates.Take(withinBudget ? 8 : 1))
        {
            var contract = Negotiations.SuggestContract(p, club, World, Data);
            contract.Years = Math.Min(contract.Years, p.Age(World.Date) >= 30 ? 1 : 2);
            if (withinBudget && bill + contract.WeeklyWage > club.Budget.WageBudget * 1.05) continue;
            ExecuteTransfer(p, club, null, new ClubOffer(), contract);
            club.Budget.WageBudget = Math.Max(club.Budget.WageBudget, WageBillOf(club));
            return true;
        }
        return false;
    }

    /// <summary>KI hält den Kader in Grenzen: Überzählige auf die Transferliste, notfalls Vertrag auflösen.</summary>
    private void ManageAiSquadSize(Club club)
    {
        var max = World.Country(club.CountryCode).Squad.MaxSquad;
        var ranked = World.SquadOf(club).Where(p => p.Loan is null).OrderBy(Rating).ToList();
        foreach (var p in ranked.Take(Math.Max(0, club.PlayerIds.Count - Finance.DivisionFinance(club).SquadSize - 1)))
            if (!p.Transfer.Listed) SetTransferListed(p, true, MarketValue.Round(ValueOf(p) * 0.8));
        // Gehälter über dem Budget: den bestbezahlten Spieler außerhalb der ersten Elf abgeben (Review F-01).
        if (WageBillOf(club) > club.Budget.WageBudget * 1.1)
        {
            var costly = ranked.Take(Math.Max(0, ranked.Count - 11)).Where(p => !p.Transfer.Listed && p.Contract is not null)
                .OrderByDescending(p => p.Contract!.WeeklyWage).FirstOrDefault();
            if (costly is not null) SetTransferListed(costly, true, MarketValue.Round(ValueOf(costly) * 0.8));
        }
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
        // Die besten (Kadergröße − 7) Spieler bekommen einen neuen Vertrag.
        return rank >= 0 && rank < Finance.DivisionFinance(club).SquadSize - 7 && p.Age(World.Date) <= 33;
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
