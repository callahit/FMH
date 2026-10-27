using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Finanzen (Kapitel 8): Zuschauer und Eintrittspreise, TV-Gelder, Sponsoren, Merchandising, Catering, Prämien,
/// Gehälter, Unterhalt, Kredite, Festgeld, Lizenzierung und Insolvenz.
/// </summary>
public sealed partial class FinanceModule : GameModule, IFinanceService
{
    public override string Id => "finance";
    public override IReadOnlyList<string> DependsOn => new[] { "facilities" };
    public override void OnDay(DayContext day) => FinanceDaily();

    private static readonly TransactionType[] ExpenseTypes =
    {
        TransactionType.Wages, TransactionType.StaffWages, TransactionType.Bonuses, TransactionType.Upkeep, TransactionType.Construction,
        TransactionType.Interest, TransactionType.LoanRepayment, TransactionType.Travel, TransactionType.Fines, TransactionType.TrainingCamp,
        TransactionType.TransferIn, TransactionType.SigningFee, TransactionType.AgentFee,
    };

    public DivisionFinance DivisionFinance(Club club) => ClubRules.DivisionFinance(World, club);

    public bool HasFinances(Club club) => World.FindLeague(club.LeagueId) is not null;

    // ================================================================== Kennzahlen

    /// <summary>Eigenkapital: Kontostand plus Festgeld minus Restschuld der Kredite.</summary>
    public long Equity(Club club) => club.Budget.Balance + (club.Finance.Deposit?.Amount ?? 0) - club.Finance.Loans.Sum(l => l.Remaining);

    /// <summary>Saisoneinnahmen (letzte Saison, sonst geschätzt) als Basis für Kreditrahmen und Budgets.</summary>
    public long ExpectedIncome(Club club)
    {
        var last = club.Finance.LastSeason.Where(kv => kv.Value > 0 && kv.Key is not (TransactionType.TransferOut or TransactionType.LoanPayout or TransactionType.Deposit)).Sum(kv => kv.Value);
        return last > 0 ? last : EstimateIncome(club);
    }

    /// <summary>Grobe Schätzung der Jahreseinnahmen aus Liga, Stadion und Vereinsgröße.</summary>
    public long EstimateIncome(Club club)
    {
        var div = DivisionFinance(club);
        var league = World.FindLeague(club.LeagueId);
        var teams = Math.Max(1, league?.ClubIds.Count ?? 18);
        var tv = div.TvPool / teams;
        var spectators = club.Stadium.Capacity * Math.Clamp(0.45 + club.Reputation * 0.025, 0.3, 0.95);
        var gate = spectators * (div.Standing * 0.3 + div.Seating * 0.7 + 4) * (teams - 1);
        var sponsors = Sponsors.SponsorValue(club) * 1.8;
        return (long)(tv + gate + sponsors);
    }

    /// <summary>Kreditrahmen der Hausbank: so weit darf das Konto ins Minus.</summary>
    public long CreditLimit(Club club) =>
        MarketValue.Round(ExpectedIncome(club) * World.Country(club.CountryCode).Finance.CreditLimitPercent / 100.0 * DifficultyBuffer(club));

    private double DifficultyBuffer(Club club) => !IsHuman(club) ? 1.0 : HumanManager.Profile.Difficulty switch
    {
        Difficulty.Easy => 2.0,
        Difficulty.Hard => 0.7,
        Difficulty.Realistic => 0.5,
        _ => 1.0,
    };

    /// <summary>Bonität: Zins 3–12 % nach Verschuldung im Verhältnis zu den Einnahmen.</summary>
    public double InterestRate(Club club)
    {
        var income = Math.Max(1, ExpectedIncome(club));
        var debt = club.Finance.Loans.Sum(l => l.Remaining) - Math.Min(0, club.Budget.Balance);
        return Math.Round(Math.Clamp(3 + debt / (double)income * 9, 3, 12), 1);
    }

    public long LoanCapacity(Club club) =>
        Math.Max(0, MarketValue.Round(ExpectedIncome(club) * 1.5) - club.Finance.Loans.Sum(l => l.Remaining));

    // ================================================================== Buchungen

    public void Book(Club club, TransactionType type, long amount, TextRef text)
    {
        club.Budget.Balance += amount;
        var f = club.Finance;
        f.Season[type] = f.Season.GetValueOrDefault(type) + amount;
        if (amount > 0) f.MonthIncome += amount;
        else f.MonthExpenses -= amount;
        // Nur für den menschlichen Verein werden Einzelbuchungen aufgehoben (Spielstandgröße).
        if (IsHuman(club) || type is TransactionType.TransferIn or TransactionType.TransferOut)
            World.Transactions.Add(new Transaction { Date = World.Date, ClubId = club.Id, Type = type, Amount = amount, TextRef = text });
    }

    // ================================================================== Spieltag

    /// <summary>Zuschauernachfrage 0–1 je Bereich aus Reputation, Tabelle, Gegner, Wetter, Fanstimmung, Preis und Umfeld.</summary>
    public (int standing, int seating, int boxes) Attendance(Club home, Club away, Fixture f, Weather weather)
    {
        var st = home.Stadium;
        var div = DivisionFinance(home);
        var prices = home.Finance.Prices;
        double demand = 0.45 + home.Reputation * 0.025;
        if (World.FindLeague(home.LeagueId) is { } league && league.CurrentMatchday > 2)
        {
            var table = LeagueTable.Compute(league);
            var pos = table.FindIndex(r => r.ClubId == home.Id);
            if (pos >= 0) demand += (1 - pos / (double)Math.Max(1, table.Count - 1)) * 0.15 - 0.06;
        }
        demand += away.Reputation * 0.008 - 0.08;
        var top = IsTopMatch(home, away, f);
        if (top) demand += 0.15;
        if (weather is Weather.Rain or Weather.Snow) demand -= 0.08 * (1 - st.RoofShare);
        demand += (home.Fans.Mood - 60) / 250;
        demand += Facilities.Effect(home, "attendance");
        demand *= home.Finance.PriceSatisfaction;
        var surcharge = top ? 1 + prices.TopMatchSurcharge / 100.0 : 1;
        double PriceFactor(double price, double reference)
        {
            var r = price / Math.Max(1, reference);
            return r <= 1 ? 1 + (1 - r) * 0.35 : Math.Max(0.15, 1 - (r - 1) * 0.75);
        }
        var fs = PriceFactor(prices.Standing * surcharge, div.Standing);
        var fz = PriceFactor(prices.Seating * surcharge, div.Seating);
        var rng = new Rng(Rng.Mix(World.Seed, (ulong)f.Id * 7919));
        var noise = rng.Range(0.95, 1.05);
        var available = st.Available / (double)Math.Max(1, st.Capacity);
        var standing = (int)(st.StandingTotal * available * Math.Clamp(demand * fs * noise, 0.05, 1));
        var seating = (int)(st.SeatingTotal * available * Math.Clamp(demand * fz * noise, 0.05, 1));
        // Dauerkarteninhaber kommen fast immer.
        var season = Math.Min(home.Finance.SeasonTickets, st.Available);
        var total = Math.Max(standing + seating, (int)(season * 0.95));
        var scale = standing + seating == 0 ? 0 : total / (double)(standing + seating);
        standing = Math.Min(st.StandingTotal, (int)(standing * scale));
        seating = Math.Min(st.SeatingTotal, (int)(seating * scale));
        var boxes = (int)Math.Round(st.Boxes * Math.Clamp(0.35 + home.Reputation * 0.03 + (top ? 0.15 : 0), 0, 1));
        return (standing, seating, boxes);
    }

    public bool IsTopMatch(Club home, Club away, Fixture f) =>
        home.Rivals.Contains(away.Id) || away.Reputation >= home.Reputation + 4 || (f.Kind != CompetitionKind.League && away.Reputation >= home.Reputation);

    /// <summary>Einnahmen eines Heimspiels: Tickets (ohne Dauerkarten), Logen, Catering, Merchandising, Umfeld.</summary>
    public void MatchdayFinances(Fixture f, MatchResult result, int standing, int seating, int boxes)
    {
        var home = World.Clubs[f.HomeId];
        var away = World.Clubs[f.AwayId];
        if (!HasFinances(home) && !HasFinances(away)) return;
        var prices = home.Finance.Prices;
        var top = IsTopMatch(home, away, f);
        var surcharge = top ? 1 + prices.TopMatchSurcharge / 100.0 : 1;
        var attendance = standing + seating;
        var seasonHolders = Math.Min(home.Finance.SeasonTickets, attendance);
        var paying = Math.Max(0, attendance - seasonHolders);
        var standingShare = attendance == 0 ? 0 : standing / (double)attendance;
        var gate = (long)(paying * (standingShare * prices.Standing + (1 - standingShare) * prices.Seating) * surcharge)
                   + (long)(boxes * prices.Box * surcharge);
        // Gebäudewirkungen auf Catering und Fanartikel wirken anteilig (Wert je Stufe x 20 %).
        var catering = (long)(attendance * 1.2 * (1 + Facilities.Effect(home, "income.catering") * 0.2) + boxes * 600 * (1 + Facilities.Effect(home, "income.box")));
        var merch = (long)(attendance * 0.8 * (1 + Facilities.Effect(home, "income.merch") * 0.2) * Math.Clamp(home.Fans.Mood / 60, 0.5, 1.4));
        var extra = (long)(Facilities.Effect(home, "income.matchday") + home.Stadium.Scoreboard * 6_000);
        var text = TR("tx.match", away.Name);
        if (f.Kind == CompetitionKind.League)
        {
            if (gate > 0) Book(home, TransactionType.Tickets, gate, text);
        }
        else
        {
            // Pokal: Zuschauereinnahmen werden geteilt.
            if (gate > 0)
            {
                Book(home, TransactionType.Tickets, gate / 2, text);
                Book(away, TransactionType.Tickets, gate - gate / 2, TR("tx.match", home.Name));
            }
        }
        if (catering > 0) Book(home, TransactionType.Catering, catering, text);
        if (merch > 0) Book(home, TransactionType.Merchandise, merch, text);
        if (extra > 0) Book(home, TransactionType.BuildingIncome, extra, text);
        var travel = (long)(DivisionFinance(away).TravelPerAwayMatch * (1 - Facilities.Effect(away, "travel.reduction")));
        if (travel > 0 && HasFinances(away)) Book(away, TransactionType.Travel, -travel, TR("tx.travel", home.Name));
        // Prämien aus den Verträgen.
        foreach (var club in new[] { home, away })
        {
            var participants = result.Goals.Where(g => g.Home == (club == home)).Select(g => g.ScorerId).ToList();
            long bonus = 0;
            foreach (var p in World.SquadOf(club))
            {
                if (p.Contract is not { } c) continue;
                if (p.SeasonStats.Appearances > 0 && c.AppearanceBonus > 0 && club.Tactic.Lineup.Contains(p.Id)) bonus += c.AppearanceBonus;
                bonus += c.GoalBonus * participants.Count(id => id == p.Id);
            }
            if (bonus > 0) Book(club, TransactionType.Bonuses, -bonus, TR("tx.bonuses", f.Kind == CompetitionKind.League ? TR("ui.league") : TR("ui.cup")));
        }
    }
}
