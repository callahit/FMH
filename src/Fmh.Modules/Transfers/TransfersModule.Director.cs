using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Sportdirektor: Transferverhandlungen mit Budgetrahmen (E2-S5).</summary>
public sealed partial class TransfersModule
{
    /// <summary>Setzt oder hebt das Mandat für den Sportdirektor. Fehlertext-Schlüssel oder null.</summary>
    public string? SetDirectorMandate(bool active, long maxFee, int maxWeeklyWage)
    {
        var profile = HumanManager.Profile;
        if (active)
        {
            if (Staff.StaffAbility(HumanClub, StaffRole.SportsDirector) <= 0) return "staff.error.noDirector";
            if (maxFee <= 0 || maxWeeklyWage <= 0) return "staff.error.directorBudget";
        }
        profile.DelegateTransfers = active;
        profile.DirectorMaxFee = Math.Max(0, maxFee);
        profile.DirectorMaxWeeklyWage = Math.Max(0, maxWeeklyWage);
        if (active)
        {
            var director = World.Staff.First(s => s.ClubId == HumanClub.Id && s.Role == StaffRole.SportsDirector);
            Notify(MessageType.Transfer, "msg.sender.sportsDirector",
                TR("msg.director.mandate.subject"),
                TR("msg.director.mandate.body", director.Name, TextArg.Money(maxFee, Currency), TextArg.Money(maxWeeklyWage, Currency)));
        }
        return null;
    }

    /// <summary>Montag im offenen Fenster: Sportdirektor versucht einen Neuzugang im Mandat.</summary>
    private void TryDirectorSigning()
    {
        var profile = HumanManager.Profile;
        if (!profile.DelegateTransfers) return;
        if (!WindowOpen(HumanClub)) return;
        var director = World.Staff.Where(s => s.ClubId == HumanClub.Id && s.Role == StaffRole.SportsDirector)
            .OrderByDescending(s => s.Ability).FirstOrDefault();
        if (director is null)
        {
            profile.DelegateTransfers = false;
            Notify(MessageType.Transfer, "msg.sender.assistant",
                TR("msg.director.missing.subject"), TR("msg.director.missing.body"));
            return;
        }
        var maxFee = profile.DirectorMaxFee;
        var maxWage = profile.DirectorMaxWeeklyWage;
        if (maxFee <= 0 || maxWage <= 0) return;

        // Beobachtungsliste zuerst, sonst Positionsbedarf wie die KI.
        var targets = HumanClub.Shortlist
            .Select(id => World.Players.GetValueOrDefault(id))
            .Where(p => p is not null && !p.Retired && p.ClubId != HumanClub.Id && p.Loan is null && !p.IsYouth)
            .Cast<Player>()
            .ToList();
        if (targets.Count == 0)
        {
            var need = Season.NeededPosition(HumanClub);
            targets = MarketOf(need)
                .Where(x => x.p.ClubId != HumanClub.Id && x.p.Loan is null && !x.p.Retired && !x.p.IsYouth)
                .Where(x => x.p.ClubId is null || ValueOf(x.p) <= maxFee)
                .OrderByDescending(x => x.r)
                .Take(6)
                .Select(x => x.p)
                .ToList();
        }

        foreach (var p in targets)
        {
            if (Negotiations.IsLocked(World, p.Id, HumanClub.Id)) continue;
            if (Negotiations.RefusalReason(p, HumanClub, World, Data) is not null) continue;
            if (OpenNegotiationFor(p) is not null) continue;

            var contract = Negotiations.SuggestContract(p, HumanClub, World, Data);
            // Verhandlungsgeschick senkt die Gehaltsforderung (Ability 1–20 → bis −20 %).
            contract.WeeklyWage = Math.Max(1, (int)(contract.WeeklyWage * (1.0 - director.Ability * 0.01)));
            if (contract.WeeklyWage > maxWage) continue;
            if (WageBillOf(HumanClub) + contract.WeeklyWage > HumanClub.Budget.WageBudget * 1.05) continue;

            if (p.ClubId is not { } sellerId)
            {
                ExecuteTransfer(p, HumanClub, null, new ClubOffer(), contract);
                ReportDirector(director, p, completed: true, fee: 0);
                return;
            }

            var seller = World.Clubs[sellerId];
            if (!WindowOpen(seller)) continue;
            var ask = Negotiations.AskingPrice(p, seller, World, Data);
            // Ability 1–20 → Angebot 80–99 % der Forderung, gekappt am Mandat.
            var fee = MarketValue.Round(Math.Min(maxFee, ask * (0.79 + director.Ability * 0.01)));
            if (fee <= 0 || fee > HumanClub.Budget.TransferBudget) continue;

            var n = new Negotiation
            {
                Id = World.NextNegotiationId++, PlayerId = p.Id, BuyerId = HumanClub.Id, SellerId = sellerId,
                Type = DealType.Transfer, Offer = new ClubOffer { Fee = fee }, Stage = NegotiationStage.Talks,
                Started = World.Date, Contract = contract,
            };
            var clubResp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
            if (clubResp.Kind == ResponseKind.Counter && clubResp.Amount <= maxFee
                && clubResp.Amount <= HumanClub.Budget.TransferBudget)
            {
                n.Offer.Fee = clubResp.Amount;
                clubResp = Negotiations.EvaluateClubOffer(n, p, seller, World, Data);
            }
            if (clubResp.Kind != ResponseKind.Accept) continue;

            var playerResp = Negotiations.EvaluateContract(n, p, HumanClub, World, Data);
            if (playerResp.Kind != ResponseKind.Accept) continue;

            World.Negotiations.Add(n);
            if (HoldForPreemption(n, p, n.Offer))
            {
                ReportDirector(director, p, completed: false, fee: n.Offer.Fee, noteKey: "msg.director.preemption");
                return;
            }
            n.PlayerAgreed = true;
            MarkClubAgreed(n, p, HumanClub);
            ReportDirector(director, p, completed: n.Stage == NegotiationStage.Completed, fee: n.Offer.Fee);
            return;
        }

        Notify(MessageType.Transfer, "msg.sender.sportsDirector",
            TR("msg.director.none.subject", director.Name),
            TR("msg.director.none.body", director.Name, TextArg.Money(maxFee, Currency)));
    }

    private void ReportDirector(StaffMember director, Player p, bool completed, long fee, string? noteKey = null)
    {
        if (completed)
        {
            Notify(MessageType.Transfer, "msg.sender.sportsDirector",
                TR("msg.director.signed.subject", p.Name),
                fee > 0
                    ? TR("msg.director.signed.body", director.Name, p.Name, TextArg.Money(fee, Currency))
                    : TR("msg.director.signedFree.body", director.Name, p.Name));
        }
        else
        {
            Notify(MessageType.Transfer, "msg.sender.sportsDirector",
                TR("msg.director.pending.subject", p.Name),
                TR(noteKey ?? "msg.director.pending.body", director.Name, p.Name));
        }
    }
}
