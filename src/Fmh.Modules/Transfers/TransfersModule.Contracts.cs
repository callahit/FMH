using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

/// <summary>Transfers: Deal-Id, mehrere Weiterverkaufsrechte und Verträge mit Historie.</summary>
public sealed partial class TransfersModule
{
    private void AddTransferRecord(int dealId, int playerId, int? from, int? to, long fee, bool loan) =>
        World.Transfers.Add(new TransferRecord
        {
            DealId = dealId, Date = World.Date, PlayerId = playerId, FromClubId = from, ToClubId = to, Fee = fee, Loan = loan, SeasonYear = World.SeasonYear,
        });

    /// <summary>Zahlt alle bestehenden Weiterverkaufsrechte. Der Käufer und der Verkäufer selbst sind ausgenommen.</summary>
    private void PaySellOns(Player p, Club seller, Club buyer, long fee)
    {
        foreach (var right in TransferTerms.ActiveSellOns(p.Transfer))
        {
            if (right.Percent <= 0 || right.ClubId == buyer.Id || right.ClubId == seller.Id) continue;
            if (!World.Clubs.TryGetValue(right.ClubId, out var beneficiary)) continue;
            var basis = TransferTerms.SellOnBase(right, fee);
            var share = basis * right.Percent / 100;
            if (share <= 0) continue;
            Finance.Book(seller, TransactionType.Other, -share, TR("tx.sellOnPaid", p.Name));
            Finance.Book(beneficiary, TransactionType.Other, share, TR("tx.sellOnReceived", p.Name));
        }
    }

    /// <summary>
    /// Behält mitgegebene Rechte und hängt das Recht des abgebenden Vereins an, soweit die Summe 50 % nicht übersteigt.
    /// Ein Recht desselben Vereins wird ersetzt. Der Vereinswechsel leert den Transferstatus, deshalb müssen die Rechte
    /// vorher kopiert werden.
    /// </summary>
    private void GrantSellOn(Player p, Club? seller, int percent, int dealId, IReadOnlyList<SellOnRight> carried, long fee = 0, SellOnBasis basis = SellOnBasis.Gross)
    {
        if ((seller is null || percent <= 0) && carried.Count == 0) return;
        var list = carried.Select(r => new SellOnRight
        {
            ClubId = r.ClubId, Percent = r.Percent, OriginDealId = r.OriginDealId, Basis = r.Basis, CostBasis = r.CostBasis,
        }).ToList();
        if (seller is not null && percent > 0)
        {
            var room = Math.Max(0, TransferTerms.MaxSellOnPercent - list.Where(r => r.ClubId != seller.Id).Sum(r => r.Percent));
            list.RemoveAll(r => r.ClubId == seller.Id);
            var granted = Math.Clamp(percent, 0, room);
            if (granted > 0)
                list.Add(new SellOnRight
                {
                    ClubId = seller.Id, Percent = granted, OriginDealId = dealId, Basis = basis,
                    CostBasis = basis == SellOnBasis.Profit ? fee : 0,
                });
        }
        else if (seller is not null)
            list.RemoveAll(r => r.ClubId == seller.Id);
        p.Transfer.SellOns = list.Count > 0 ? list : null;
        var last = list.LastOrDefault();
        p.Transfer.SellOnClubId = last?.ClubId;
        p.Transfer.SellOnPercent = last?.Percent ?? 0;
    }

    /// <summary>
    /// Setzt den Vertrag des Spielers und schreibt die Historie. <paramref name="previousClubId"/> ist der Verein des
    /// bisherigen Vertrags, wenn der Spieler schon zum neuen Verein umgezogen wurde.
    /// </summary>
    public void SetContract(Player p, Contract? next, int? previousClubId = null, int dealId = 0)
    {
        if (ReferenceEquals(p.Contract, next)) return;
        var previous = World.ContractHistory.LastOrDefault(c => c.PlayerId == p.Id && c.Current);
        if (previous is null && p.Contract is { } old)
        {
            previous = new ContractRecord
            {
                Id = World.NextContractId++, PlayerId = p.Id, ClubId = previousClubId ?? p.ClubId ?? 0,
                Start = old.Start, End = old.Until, WeeklyWage = old.WeeklyWage, Current = true, Terms = CloneContract(old),
            };
            World.ContractHistory.Add(previous);
        }
        int? newId = null;
        if (next is not null)
        {
            newId = World.NextContractId++;
            World.ContractHistory.Add(new ContractRecord
            {
                Id = newId.Value, PlayerId = p.Id, ClubId = p.ClubId ?? previousClubId ?? 0, DealId = dealId,
                Start = next.Start, End = next.Until, WeeklyWage = next.WeeklyWage, Current = true, Terms = CloneContract(next),
            });
        }
        if (previous is not null)
        {
            previous.Current = false;
            previous.SupersededBy = newId;
        }
        p.Contract = next;
        DeactivateContractClauses(p.Id);
        if (next is not null && newId is { } cid) SyncContractClauses(p, next, cid, dealId);
        if (next is not null) Relations.RecordNegotiatedStatus(p, next.Status);
    }

    private void DeactivateContractClauses(int playerId)
    {
        foreach (var c in World.Clauses.Where(c => c.PlayerId == playerId && c.Kind is ClauseKind.Release or ClauseKind.RelegationWage
                     or ClauseKind.PromotionWage or ClauseKind.YearlyRaise or ClauseKind.ExtensionOption))
            c.Active = false;
    }

    /// <summary>Schreibt Vertrags-Klauseln als Datensätze (Must-now E1-S3).</summary>
    private void SyncContractClauses(Player p, Contract next, int contractId, int dealId)
    {
        void Add(ClauseKind kind, long amount, int percent)
        {
            World.Clauses.Add(new Clause
            {
                Id = World.NextClauseId++, PlayerId = p.Id, ContractId = contractId, DealId = dealId > 0 ? dealId : null,
                Kind = kind, Amount = amount, Percent = percent, Until = next.Until, Active = true,
            });
        }
        if (next.ReleaseClause is { } release) Add(ClauseKind.Release, release, 0);
        if (next.RelegationPercent is { } rel) Add(ClauseKind.RelegationWage, 0, rel);
        if (next.PromotionPercent is { } pro) Add(ClauseKind.PromotionWage, 0, pro);
        if (next.YearlyRaisePercent > 0) Add(ClauseKind.YearlyRaise, 0, next.YearlyRaisePercent);
        if (next.ExtensionOptionYears > 0) Add(ClauseKind.ExtensionOption, next.ExtensionOptionYears, 0);
    }

    private static Contract CloneContract(Contract c) => new()
    {
        WeeklyWage = c.WeeklyWage, Until = c.Until, Start = c.Start, SigningFee = c.SigningFee,
        AppearanceBonus = c.AppearanceBonus, GoalBonus = c.GoalBonus, ReleaseClause = c.ReleaseClause,
        YearlyRaisePercent = c.YearlyRaisePercent, RelegationClause = c.RelegationClause,
        RelegationPercent = c.RelegationPercent, PromotionPercent = c.PromotionPercent,
        ExtensionOptionYears = c.ExtensionOptionYears, Status = c.Status, Youth = c.Youth,
    };

    /// <summary>Rückkauf und Vorkauf zugunsten des Verkäufers (Must-now E1-S4).</summary>
    private void GrantDealClauses(Player p, Club? seller, ClubOffer offer, int dealId)
    {
        if (seller is null) return;
        if (offer.BuyBackFee is { } fee and > 0)
        {
            var years = Math.Clamp(offer.BuyBackYears <= 0 ? 2 : offer.BuyBackYears, 1, 3);
            World.Clauses.Add(new Clause
            {
                Id = World.NextClauseId++, PlayerId = p.Id, DealId = dealId, BeneficiaryClubId = seller.Id,
                Kind = ClauseKind.BuyBack, Amount = fee, Until = World.Date.AddYears(years), Active = true,
            });
        }
        if (offer.PreemptionRight)
        {
            World.Clauses.Add(new Clause
            {
                Id = World.NextClauseId++, PlayerId = p.Id, DealId = dealId, BeneficiaryClubId = seller.Id,
                Kind = ClauseKind.Preemption, Until = null, Active = true,
            });
        }
    }

    /// <summary>Aktives Rückkaufrecht des angegebenen Vereins.</summary>
    public Clause? ActiveBuyBack(Player p, int clubId) =>
        World.Clauses.FirstOrDefault(c => c.Active && c.Kind == ClauseKind.BuyBack && c.PlayerId == p.Id
            && c.BeneficiaryClubId == clubId && (c.Until is null || c.Until >= World.Date));

    /// <summary>Aktives Vorkaufsrecht eines früheren Vereins.</summary>
    public Clause? ActivePreemption(Player p) =>
        World.Clauses.FirstOrDefault(c => c.Active && c.Kind == ClauseKind.Preemption && c.PlayerId == p.Id
            && (c.Until is null || c.Until >= World.Date));
}
