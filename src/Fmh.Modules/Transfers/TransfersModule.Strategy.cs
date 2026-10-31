using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

/// <summary>
/// Transfers: Leihmarkt der KI-Vereine. Stärkere Vereine geben Spieler außerhalb ihrer Stammformation ab, schwächere Vereine
/// leihen sie aus (CIES: in fast zwei Dritteln der Fälle ist der ausleihende Verein sportlich schwächer). Die Leihgrenzen
/// gelten für beide Seiten (<see cref="LoanLimits"/>).
/// </summary>
public sealed partial class TransfersModule
{
    /// <summary>Spieler, die ihre Vereine diese Woche abgeben würden (je Montag neu ermittelt).</summary>
    private List<(Player p, double rating, Club parent)>? _loanPool;
    /// <summary>Derzeit verliehene Spieler (für die Leihgrenzen; je Montag neu ermittelt, neue Leihen kommen hinzu).</summary>
    private List<Player>? _activeLoans;

    /// <summary>Spieler bis 31 je Hauptposition mit ihrer Stärke (je Montag einmal ermittelt; Verein und Status prüft der Aufrufer).</summary>
    private Dictionary<Position, List<(Player p, double r)>>? _market;

    private List<(Player p, double r)> MarketOf(Position pos)
    {
        _market ??= World.Players.Values.Where(p => !p.Retired && !p.IsYouth && p.Age(World.Date) <= 31)
            .GroupBy(p => p.MainPosition).ToDictionary(g => g.Key, g => g.Select(p => (p, Rating(p))).ToList());
        return _market.TryGetValue(pos, out var list) ? list : new();
    }

    /// <summary>Position mit dem größten Bedarf je Verein (je Montag zwischengespeichert, bei Kaderwechseln verworfen).</summary>
    private readonly Dictionary<int, Position> _needs = new();

    private Position NeedOf(Club club)
    {
        if (!_needs.TryGetValue(club.Id, out var need)) _needs[club.Id] = need = Season.NeededPosition(club);
        return need;
    }

    private List<Player> ActiveLoans() => _activeLoans ??= World.Players.Values.Where(p => p.Loan is not null).ToList();

    private long WageBillOf(Club club) => Budgets.WageBill(club, World, ActiveLoans());

    private string? CheckLoan(Player p, Club parent, Club borrower) => LoanLimits.Check(World, Data, p, parent, borrower, 12, ActiveLoans());

    /// <summary>Spieler außerhalb der besten 14 ihres Kaders (oder zur Leihe bzw. zum Verkauf freigegeben) bei KI-Vereinen der Profiligen.</summary>
    private List<(Player p, double rating, Club parent)> LoanPool()
    {
        if (_loanPool is not null) return _loanPool;
        var pool = new List<(Player, double, Club)>();
        var seasonEnd = new DateOnly(World.SeasonYear + 1, 6, 30);
        foreach (var club in World.Clubs.Values.Where(c => !IsHuman(c) && World.FindLeague(c.LeagueId) is { } l && l.Level <= Data.TransferRules.ProfessionalLevels))
        {
            var ranked = World.SquadOf(club).Where(p => p.Loan is null).Select(p => (p, r: Rating(p))).OrderByDescending(x => x.r).ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                var (p, r) = ranked[i];
                // Auf der Transferliste stehende Spieler gibt der Verein auch leihweise ab (spart Gehalt).
                if (i < 14 && !p.Transfer.LoanListed && !p.Transfer.Listed) continue;
                if (p.IsYouth || p.PreContract is not null || p.Age(World.Date) < 17 || p.Contract is not { } c || c.Until < seasonEnd) continue;
                pool.Add((p, r, club));
            }
        }
        return _loanPool = pool;
    }

    /// <summary>KI leiht einen Spieler eines stärkeren Vereins für die Position mit dem größten Bedarf aus.</summary>
    private void TryAiBorrow(Club club)
    {
        var need = NeedOf(club);
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        var tendency = ClubStrategy.Tendency(World, club);
        var candidates = LoanPool()
            .Where(x => x.p.MainPosition == need && x.parent.Reputation > club.Reputation && x.p.ClubId == x.parent.Id)
            .Where(x => x.rating >= level - 1.0 && x.rating <= level + 1.5 && x.p.Reputation < club.Reputation + 6)
            // Im eigenen Land ist der Weg kürzer: gleich starke Kandidaten aus dem Ausland kommen erst danach.
            .OrderByDescending(x => x.rating - (x.parent.CountryCode == club.CountryCode ? 0 : 0.5))
            .Take(5)
            .ToList();
        foreach (var (p, _, parent) in candidates)
        {
            // Beide Verbände müssen ihr Wechselfenster offen haben (Leihe ist ein Wechsel für beide Seiten).
            if (!WindowOpen(club) || !WindowOpen(parent)) continue;
            if (club.PlayerIds.Count >= World.Country(club.CountryCode).Squad.MaxSquad) return;
            if (CheckLoan(p, parent, club) is not null) continue;
            // Der ausleihende Verein zahlt so viel vom Gehalt, wie sein Budget hergibt und in seiner Spielklasse üblich ist
            // (höchstens das Doppelte des Durchschnittsgehalts); den Rest trägt der abgebende Verein, der den Spieler sonst ohne
            // Einsatz bezahlen müsste. Junge Spieler bis 23 verleiht er zur Ausbildung notfalls auch ganz auf eigene Kosten.
            var room = club.Budget.WageBudget * 1.05 - WageBillOf(club);
            var target = Finance.DivisionFinance(club).TargetAnnualWage / 52.0;
            var cap = target > 0 ? Math.Min(room, target * 2) : room;
            var wage = Math.Max(1, p.Contract?.WeeklyWage ?? 0);
            var shares = p.Age(World.Date) <= 23 ? new[] { 100, 75, 50, 25, 0 } : new[] { 100, 75, 50, 25 };
            var share = shares.Select(s => (int?)s).FirstOrDefault(s => wage * s!.Value / 100.0 <= cap);
            if (share is not { } fits) continue;
            var offer = new ClubOffer { LoanMonths = 12, WageSharePercent = fits, RecallInWinter = Rng.Chance(0.2) };
            var value = ValueOf(p);
            // Leihe mit Kaufpflicht (in Italien häufig), wenn der Marktwert ins Budget passt.
            if (value <= Math.Min(club.Budget.TransferBudget, Finance.Equity(club)) && Rng.Chance(tendency.PurchaseObligationShare))
            {
                offer.PurchaseFee = value;
                offer.PurchaseObligation = true;
            }
            ExecuteLoan(p, club, parent, offer);
            return;
        }
    }

    /// <summary>
    /// Spitzen- und Ausbildungsvereine verleihen junge Spieler außerhalb ihrer besten <see cref="StrategyProfile.LendBeyondRank"/>
    /// an schwächere Profivereine mit Bedarf auf der Position, bevorzugt im eigenen Land.
    /// </summary>
    private void TryAiLoanOut(Club club, StrategyProfile profile)
    {
        if (club.PlayerIds.Count <= Finance.DivisionFinance(club).MinSquad) return;
        var ranked = World.SquadOf(club).Where(p => p.Loan is null).OrderByDescending(Rating).ToList();
        var candidate = ranked.Skip(profile.LendBeyondRank).FirstOrDefault(p => p.Age(World.Date) <= 23 && p.PreContract is null && !p.IsYouth
                                                                             && p.Hidden.PotentialAbility - p.Hidden.CurrentAbility >= 10);
        if (candidate is null) return;
        var rating = Rating(candidate);
        var target = World.Clubs.Values
            .Where(c => c.Id != club.Id && !IsHuman(c) && c.Reputation < club.Reputation && c.PlayerIds.Count < Finance.DivisionFinance(c).SquadSize + 2)
            .Where(c => WindowOpen(c) && c.PlayerIds.Count < World.Country(c.CountryCode).Squad.MaxSquad)
            .Where(c => World.FindLeague(c.LeagueId) is { } l && l.Level <= Data.TransferRules.ProfessionalLevels)
            .Where(c => rating >= PlayerGenerator.ClubLevel(c.Reputation) - 1)
            .OrderByDescending(c => c.CountryCode == club.CountryCode)
            .ThenByDescending(c => c.Reputation)
            .FirstOrDefault(c => NeedOf(c) == candidate.MainPosition && CheckLoan(candidate, club, c) is null);
        if (target is null) return;
        ExecuteLoan(candidate, target, club, new ClubOffer { LoanMonths = 12, WageSharePercent = Rng.Pick(new[] { 50, 75, 100 }), RecallInWinter = Rng.Chance(0.3) });
    }
}
