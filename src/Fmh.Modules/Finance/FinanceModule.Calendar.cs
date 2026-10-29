using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Finanzen: wöchentliche, monatliche und saisonale Buchungen.</summary>
public sealed partial class FinanceModule
{
    // ================================================================== Kalender

    /// <summary>Täglich: Montag Gehälter und Wocheneinnahmen, Monatserster Unterhalt, TV, Sponsoren, Kredite.</summary>
    private void FinanceDaily()
    {
        var monday = World.Date.DayOfWeek == DayOfWeek.Monday;
        var firstOfMonth = World.Date.Day == 1;
        foreach (var club in World.Clubs.Values)
        {
            if (!HasFinances(club)) continue;
            if (monday) Weekly(club);
            if (firstOfMonth) Monthly(club);
            CheckSolvency(club);
        }
        SeasonTicketSales();
    }

    private void Weekly(Club club)
    {
        var wages = Budgets.WageBill(club, World);
        if (wages > 0) Book(club, TransactionType.Wages, -wages, TR("tx.wages"));
        var staff = World.Staff.Where(s => s.ClubId == club.Id).Sum(s => (long)s.WeeklyWage)
                    + World.Scouts.Where(s => s.ClubId == club.Id).Sum(s => (long)ClubRules.ScoutWage(s))
                    + (IsHuman(club) ? club.Board.ManagerWeeklyWage : (long)(club.Board.ManagerWeeklyWage > 0 ? club.Board.ManagerWeeklyWage : ClubRules.ManagerWage(club)));
        if (staff > 0) Book(club, TransactionType.StaffWages, -staff, TR("tx.staffWages"));
        var weekly = (Facilities.Effect(club, "income.weekly") + Facilities.Effect(club, "income.events") + Facilities.Effect(club, "income.patients")) * 0.5
                     + club.Grounds.Buildings.Where(b => b.Leased).Sum(b => Facilities.MonthlyUpkeep(club, b) * 0.4);
        if (weekly > 0) Book(club, TransactionType.BuildingIncome, (long)weekly, TR("tx.buildings"));
    }

    private void Monthly(Club club)
    {
        var f = club.Finance;
        // Monatsbilanz für Verlauf und Diagramm abschließen.
        var prev = World.Date.AddMonths(-1);
        Facilities.FacilitiesMonthly(club);
        var reduction = Math.Min(0.4, Facilities.Effect(club, "upkeep.reduction"));
        var upkeep = club.Grounds.Buildings.Where(b => !b.Leased && b.Level > 0).Sum(b => Facilities.MonthlyUpkeep(club, b))
                     + ClubRules.StadiumUpkeep(club.Stadium);
        upkeep = (long)(upkeep * (1 - reduction));
        if (upkeep > 0) Book(club, TransactionType.Upkeep, -upkeep, TR("tx.upkeep"));

        // TV: Grundbetrag und Leistungsanteil in 10 Monatsraten (August–Mai); der Leistungsanteil nach dem aktuellen
        // Tabellenplatz. Bis 2026/10 floss er erst zum Saisonende, die Lücke trieb Vereine in die Insolvenz (Review F-01).
        var div = DivisionFinance(club);
        if (World.Date.Month is >= 8 or <= 5 && div.TvPool > 0 && World.FindLeague(club.LeagueId) is { } league)
        {
            var equal = div.TvPool * div.TvEqualShare / Math.Max(1, league.ClubIds.Count) / 10;
            Book(club, TransactionType.Tv, (long)equal, TR("tx.tv"));
            if (PerformanceShares(league).TryGetValue(club.Id, out var share) && share.Amount > 0)
                Book(club, TransactionType.Tv, share.Amount / 10, TR("tx.tvPerformance", share.Position));
        }
        if (World.Date.Month is >= 8 or <= 5) Parachute(club);
        // Sponsoren monatlich, Mitgliedsbeiträge.
        var sponsorMonthly = club.Sponsors.Where(s => s.UntilSeason >= World.SeasonYear).Sum(s => s.PerSeason) / 12;
        if (sponsorMonthly > 0) Book(club, TransactionType.Sponsorship, sponsorMonthly, TR("tx.sponsors"));
        var members = (long)(club.Fans.Members * 4);
        if (members > 0) Book(club, TransactionType.BuildingIncome, members, TR("tx.members"));

        Operations(club, div);

        // Kredite: Annuität, Zinsanteil getrennt.
        foreach (var loan in f.Loans.ToList())
        {
            var interest = (long)(loan.Remaining * loan.InterestPercent / 100 / 12);
            var principal = Math.Min(loan.Remaining, Math.Max(0, loan.MonthlyPayment - interest));
            Book(club, TransactionType.Interest, -interest, TR("tx.interest"));
            Book(club, TransactionType.LoanRepayment, -principal, TR("tx.repayment"));
            loan.Remaining -= principal;
            if (loan.Remaining <= 0) f.Loans.Remove(loan);
        }
        // Festgeld fällig.
        if (f.Deposit is { } dep && World.Date >= dep.Until)
        {
            var years = (dep.Until.DayNumber - dep.Start.DayNumber) / 365.0;
            var interest = (long)(dep.Amount * dep.InterestPercent / 100 * years);
            Book(club, TransactionType.Deposit, dep.Amount, TR("tx.depositBack"));
            Book(club, TransactionType.Interest, interest, TR("tx.depositInterest"));
            f.Deposit = null;
            if (IsHuman(club)) Notify(MessageType.Finance, "msg.sender.bank", TR("msg.deposit.subject"), TR("msg.deposit.body", TextArg.Money(dep.Amount + interest, Currency)));
        }
        // Dispozinsen auf das Minus.
        if (club.Budget.Balance < 0)
            Book(club, TransactionType.Interest, (long)(club.Budget.Balance * (InterestRate(club) + 3) / 100 / 12), TR("tx.overdraft"));

        // Monatsbilanz des Vormonats (die Buchungen dieses Monatsersten zählen schon zum neuen Monat).
        f.History.Add(new MonthRecord { Year = prev.Year, Month = prev.Month, Balance = club.Budget.Balance, Income = f.MonthIncome, Expenses = f.MonthExpenses });
        f.MonthIncome = 0;
        f.MonthExpenses = 0;
        while (f.History.Count > 36) f.History.RemoveAt(0);
        if (!IsHuman(club)) AiFinance(club);
    }

    /// <summary>Laufende Einnahmen, an denen sich die Betriebskosten bemessen (ohne Transfers, Kredite und Festgeld).</summary>
    private static readonly TransactionType[] OperatingIncome =
    {
        TransactionType.Tickets, TransactionType.SeasonTickets, TransactionType.Tv, TransactionType.Sponsorship, TransactionType.Merchandise,
        TransactionType.Catering, TransactionType.BuildingIncome, TransactionType.PrizeMoney, TransactionType.European,
    };

    /// <summary>
    /// Betriebskosten monatlich nachbuchen: Anteil der bisherigen Saisoneinnahmen abzüglich des schon Gebuchten. Ergibt sich
    /// vollständig aus den Saisonsummen, braucht also keinen eigenen Zustand im Spielstand.
    /// </summary>
    private void Operations(Club club, DivisionFinance div)
    {
        if (div.OperatingCostShare <= 0) return;
        var income = OperatingIncome.Sum(t => Math.Max(0, club.Finance.Season.GetValueOrDefault(t)));
        var due = (long)(income * div.OperatingCostShare) + club.Finance.Season.GetValueOrDefault(TransactionType.Operations);
        if (due > 0) Book(club, TransactionType.Operations, -due, TR("tx.operations"));
    }

    /// <summary>
    /// Fallschirmzahlungen in 10 Monatsraten: Absteiger aus einer Spielklasse mit <see cref="DivisionFinance.ParachuteShares"/>
    /// erhalten in der k-ten Saison danach den k-ten Anteil an deren TV-Grundbetrag je Verein.
    /// </summary>
    private void Parachute(Club club)
    {
        foreach (var (id, upper) in World.Country(club.CountryCode).Finance.Divisions.Where(kv => kv.Value.ParachuteShares.Count > 0 && kv.Value.TvPool > 0))
        {
            bool InDivision(string leagueId) => World.FindLeague(leagueId) is { } l && (l.Id == id || l.Format.Id == id);
            if (InDivision(club.LeagueId)) continue;
            // Saisons seit dem letzten Jahr in der Spielklasse und Dauer der Zugehörigkeit davor.
            var since = 0;
            for (var k = 1; k <= upper.ParachuteShares.Count && since == 0; k++)
                if (World.History.Any(h => h.SeasonYear == World.SeasonYear - k && InDivision(h.LeagueId) && h.FinalOrder.Contains(club.Id))) since = k;
            if (since == 0) continue;
            var tenure = World.History.Any(h => h.SeasonYear == World.SeasonYear - since - 1 && InDivision(h.LeagueId) && h.FinalOrder.Contains(club.Id)) ? 2 : 1;
            if (tenure == 1 && upper.ParachuteShortTenure > 0 && since > upper.ParachuteShortTenure) continue;
            var teams = World.Leagues.Where(l => l.Id == id || l.Format.Id == id).Sum(l => l.ClubIds.Count);
            var amount = upper.TvPool * upper.TvEqualShare / Math.Max(1, teams) * upper.ParachuteShares[since - 1] / 10;
            if (amount > 0) Book(club, TransactionType.Tv, (long)amount, TR("tx.parachute"));
        }
    }

    /// <summary>Dauerkarten werden zum Stichtag verkauft (Kapitel 8).</summary>
    private void SeasonTicketSales()
    {
        var country = World.Countries[0];
        var deadline = ScheduleGenerator.ParseDate(country.Finance.SeasonTicketDeadline, World.SeasonYear, country.Season.StartMonth);
        if (World.Date != deadline) return;
        foreach (var club in World.Clubs.Values.Where(HasFinances))
        {
            var p = club.Finance.Prices;
            var div = DivisionFinance(club);
            var discount = 1 - p.SeasonTicketDiscount / 100.0;
            var priceRatio = (p.Standing * 0.4 + p.Seating * 0.6) * discount / Math.Max(1, div.Standing * 0.4 + div.Seating * 0.6);
            var share = Math.Clamp(0.15 + club.Reputation * 0.02 + (club.Fans.Mood - 60) / 300 + Facilities.Effect(club, "seasonTickets") - (priceRatio - 0.8) * 0.4, 0.05, 0.7);
            var count = (int)(club.Stadium.Capacity * share);
            var home = World.FindLeague(club.LeagueId) is { } l ? l.ClubIds.Count - 1 : 17;
            club.Finance.SeasonTickets = count;
            var income = (long)(count * (p.Standing * 0.45 + p.Seating * 0.55) * discount * home);
            Book(club, TransactionType.SeasonTickets, income, TR("tx.seasonTickets"));
            if (IsHuman(club))
                Notify(MessageType.Finance, "msg.sender.assistant", TR("msg.seasonTickets.subject"), TR("msg.seasonTickets.body", count, TextArg.Money(income, Currency)));
        }
    }

    private (DateOnly Date, string League, Dictionary<int, (int Position, long Amount)> Shares) _performanceCache;

    /// <summary>Leistungsanteile einer Liga am heutigen Tag (eine Tabellenberechnung je Liga und Monatsersten).</summary>
    private Dictionary<int, (int Position, long Amount)> PerformanceShares(League league)
    {
        if (_performanceCache.Date != World.Date || _performanceCache.League != league.Id)
            _performanceCache = (World.Date, league.Id, TvPerformanceShares(league).ToDictionary(s => s.club.Id, s => (s.position, s.amount)));
        return _performanceCache.Shares;
    }

    /// <summary>TV-Leistungsanteile nach aktuellem Tabellenplatz (zum Saisonende ausgezahlt).</summary>
    private IEnumerable<(Club club, int position, long amount)> TvPerformanceShares(League league)
    {
        if (!World.Country(league.CountryCode).Finance.Divisions.TryGetValue(league.Id, out var div) || div.TvPool == 0) yield break;
        var table = LeagueTable.Compute(league);
        var pool = div.TvPool * (1 - div.TvEqualShare);
        var weights = table.Select(r => (double)(table.Count - r.Position + 1)).ToList();
        var total = weights.Sum();
        for (var i = 0; i < table.Count; i++)
            yield return (World.Clubs[table[i].ClubId], table[i].Position, (long)(pool * weights[i] / total));
    }

    /// <summary>Monate der TV-Raten in Saisonreihenfolge.</summary>
    private static readonly int[] TvMonths = { 8, 9, 10, 11, 12, 1, 2, 3, 4, 5 };

    /// <summary>
    /// Eigenkapital für die Lizenzprüfung: Die noch ausstehenden Raten des TV-Leistungsanteils stehen dem Verein nach
    /// aktuellem Tabellenplatz schon zu und zählen bis zur Auszahlung als Forderung mit.
    /// </summary>
    public long LicenseEquity(Club club)
    {
        var index = Array.IndexOf(TvMonths, World.Date.Month);
        var open = index < 0 ? 0 : TvMonths.Length - 1 - index;
        var receivable = !World.SeasonFinished && open > 0 && World.FindLeague(club.LeagueId) is { } league
            ? TvPerformanceShares(league).Where(s => s.club == club).Sum(s => s.amount) / 10 * open : 0;
        return Equity(club) + receivable;
    }

    public void RollSeasonFinances()
    {
        foreach (var club in World.Clubs.Values)
        {
            if (HasFinances(club)) License.RecordSeasonFinance(club);
            club.Finance.LastSeason = club.Finance.Season;
            club.Finance.Season = new Dictionary<TransactionType, long>();
        }
        // Einzelbuchungen älter als zwei Saisons verwerfen.
        var cutoff = World.Date.AddYears(-2);
        World.Transactions.RemoveAll(t => t.Date < cutoff);
    }

    /// <summary>Pokalprämie für das Erreichen einer Runde bzw. den Sieg.</summary>
    public void CupPrize(Cup cup, int roundReached, IEnumerable<int> clubs)
    {
        var fin = World.Country(cup.CountryCode).Finance;
        // Prämien des Pokals selbst, sonst die des Landes.
        var prizes = cup.Config.Prizes ?? fin.CupPrizes;
        long amount = cup.Kind == CompetitionKind.Playoff ? fin.PlayoffPrize
            : roundReached - 1 < prizes.Count ? prizes[Math.Max(0, roundReached - 1)] : cup.Config.WinnerPrize ?? fin.CupWinnerPrize;
        if (amount <= 0) return;
        foreach (var id in clubs)
            Book(World.Clubs[id], TransactionType.PrizeMoney, amount, TR("tx.cupPrize", Comp(cup.Id)));
    }
}
