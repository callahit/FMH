using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>
/// Finanzen (Kapitel 8): Zuschauer und Eintrittspreise, TV-Gelder, Sponsoren, Merchandising, Catering, Prämien,
/// Gehälter, Unterhalt, Kredite, Festgeld, Lizenzierung und Insolvenz.
/// </summary>
public sealed partial class GameSession
{
    private static readonly TransactionType[] ExpenseTypes =
    {
        TransactionType.Wages, TransactionType.StaffWages, TransactionType.Bonuses, TransactionType.Upkeep, TransactionType.Construction,
        TransactionType.Interest, TransactionType.LoanRepayment, TransactionType.Travel, TransactionType.Fines, TransactionType.TrainingCamp,
        TransactionType.TransferIn, TransactionType.SigningFee, TransactionType.AgentFee,
    };

    public DivisionFinance DivisionFinance(Club club) =>
        World.Country(club.CountryCode).Finance.Divisions.TryGetValue(club.LeagueId, out var d) ? d : new DivisionFinance();

    private bool HasFinances(Club club) => World.FindLeague(club.LeagueId) is not null;

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
        var sponsors = SponsorValue(club) * 1.8;
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

    private void Book(Club club, TransactionType type, long amount, TextRef text)
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
        demand += Effect(home, "attendance");
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
    private void MatchdayFinances(Fixture f, MatchResult result, int standing, int seating, int boxes)
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
        var catering = (long)(attendance * 1.2 * (1 + Effect(home, "income.catering") * 0.2) + boxes * 600 * (1 + Effect(home, "income.box")));
        var merch = (long)(attendance * 0.8 * (1 + Effect(home, "income.merch") * 0.2) * Math.Clamp(home.Fans.Mood / 60, 0.5, 1.4));
        var extra = (long)(Effect(home, "income.matchday") + home.Stadium.Scoreboard * 6_000);
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
        var travel = (long)(DivisionFinance(away).TravelPerAwayMatch * (1 - Effect(away, "travel.reduction")));
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
                    + World.Scouts.Where(s => s.ClubId == club.Id).Sum(s => (long)ScoutWage(s))
                    + (IsHuman(club) ? club.Board.ManagerWeeklyWage : (long)(club.Board.ManagerWeeklyWage > 0 ? club.Board.ManagerWeeklyWage : 2_000 * Scale(club)));
        if (staff > 0) Book(club, TransactionType.StaffWages, -staff, TR("tx.staffWages"));
        var weekly = (Effect(club, "income.weekly") + Effect(club, "income.events") + Effect(club, "income.patients")) * 0.5
                     + club.Grounds.Buildings.Where(b => b.Leased).Sum(b => MonthlyUpkeep(club, b) * 0.4);
        if (weekly > 0) Book(club, TransactionType.BuildingIncome, (long)weekly, TR("tx.buildings"));
    }

    private void Monthly(Club club)
    {
        var f = club.Finance;
        // Monatsbilanz für Verlauf und Diagramm abschließen.
        var prev = World.Date.AddMonths(-1);
        FacilitiesMonthly(club);
        var reduction = Math.Min(0.4, Effect(club, "upkeep.reduction"));
        var upkeep = club.Grounds.Buildings.Where(b => !b.Leased && b.Level > 0).Sum(b => MonthlyUpkeep(club, b))
                     + StadiumUpkeep(club.Stadium);
        upkeep = (long)(upkeep * (1 - reduction));
        if (upkeep > 0) Book(club, TransactionType.Upkeep, -upkeep, TR("tx.upkeep"));

        // TV: Grundbetrag in 10 Monatsraten (August–Mai), Leistungsanteil zum Saisonende.
        var div = DivisionFinance(club);
        if (World.Date.Month is >= 8 or <= 5 && div.TvPool > 0 && World.FindLeague(club.LeagueId) is { } league)
        {
            var equal = div.TvPool * div.TvEqualShare / Math.Max(1, league.ClubIds.Count) / 10;
            Book(club, TransactionType.Tv, (long)equal, TR("tx.tv"));
        }
        // Sponsoren monatlich, Mitgliedsbeiträge.
        var sponsorMonthly = club.Sponsors.Where(s => s.UntilSeason >= World.SeasonYear).Sum(s => s.PerSeason) / 12;
        if (sponsorMonthly > 0) Book(club, TransactionType.Sponsorship, sponsorMonthly, TR("tx.sponsors"));
        var members = (long)(club.Fans.Members * 4);
        if (members > 0) Book(club, TransactionType.BuildingIncome, members, TR("tx.members"));

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

    public static long StadiumUpkeep(Stadium st) =>
        (long)(st.Capacity * 0.6 + st.Boxes * 400 + st.Floodlight * 3_000 + (st.UndersoilHeating ? 6_000 : 0) + st.Scoreboard * 1_500);

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
            var share = Math.Clamp(0.15 + club.Reputation * 0.02 + (club.Fans.Mood - 60) / 300 + Effect(club, "seasonTickets") - (priceRatio - 0.8) * 0.4, 0.05, 0.7);
            var count = (int)(club.Stadium.Capacity * share);
            var home = World.FindLeague(club.LeagueId) is { } l ? l.ClubIds.Count - 1 : 17;
            club.Finance.SeasonTickets = count;
            var income = (long)(count * (p.Standing * 0.45 + p.Seating * 0.55) * discount * home);
            Book(club, TransactionType.SeasonTickets, income, TR("tx.seasonTickets"));
            if (IsHuman(club))
                Notify(MessageType.Finance, "msg.sender.assistant", TR("msg.seasonTickets.subject"), TR("msg.seasonTickets.body", count, TextArg.Money(income, Currency)));
        }
    }

    /// <summary>Saisonende: TV-Leistungsanteil nach Abschlussplatz, Saisonstatistik übertragen.</summary>
    private void SeasonEndFinances()
    {
        foreach (var league in World.Leagues)
        {
            if (!World.Country(league.CountryCode).Finance.Divisions.TryGetValue(league.Id, out var div) || div.TvPool == 0) continue;
            var table = LeagueTable.Compute(league);
            var pool = div.TvPool * (1 - div.TvEqualShare);
            var weights = table.Select(r => (double)(table.Count - r.Position + 1)).ToList();
            var total = weights.Sum();
            for (var i = 0; i < table.Count; i++)
                Book(World.Clubs[table[i].ClubId], TransactionType.Tv, (long)(pool * weights[i] / total), TR("tx.tvPerformance", table[i].Position));
        }
    }

    private void RollSeasonFinances()
    {
        foreach (var club in World.Clubs.Values)
        {
            if (HasFinances(club)) RecordSeasonFinance(club);
            club.Finance.LastSeason = club.Finance.Season;
            club.Finance.Season = new Dictionary<TransactionType, long>();
        }
        // Einzelbuchungen älter als zwei Saisons verwerfen.
        var cutoff = World.Date.AddYears(-2);
        World.Transactions.RemoveAll(t => t.Date < cutoff);
    }

    /// <summary>Pokalprämie für das Erreichen einer Runde bzw. den Sieg.</summary>
    private void CupPrize(Cup cup, int roundReached, IEnumerable<int> clubs)
    {
        var fin = World.Country(cup.CountryCode).Finance;
        long amount = cup.Kind == CompetitionKind.Playoff ? fin.PlayoffPrize
            : roundReached - 1 < fin.CupPrizes.Count ? fin.CupPrizes[Math.Max(0, roundReached - 1)] : fin.CupWinnerPrize;
        if (amount <= 0) return;
        foreach (var id in clubs)
            Book(World.Clubs[id], TransactionType.PrizeMoney, amount, TR("tx.cupPrize", Comp(cup.Id)));
    }

    // ================================================================== Kredite, Festgeld, Budgets

    public string? TakeLoan(long amount, int years) => TakeLoan(HumanClub, amount, years, silent: false);

    private string? TakeLoan(Club club, long amount, int years, bool silent, long? capacity = null)
    {
        // Stadionkredite (eigener Rahmen) laufen bis zu 20 Jahre.
        years = Math.Clamp(years, 1, capacity is null ? 10 : 20);
        if (amount <= 0) return "fin.error.amount";
        if (amount > (capacity ?? LoanCapacity(club))) return "fin.error.loanLimit";
        var rate = InterestRate(club);
        var r = rate / 100 / 12;
        var n = years * 12;
        var monthly = (long)Math.Ceiling(amount * r / (1 - Math.Pow(1 + r, -n)));
        club.Finance.Loans.Add(new BankLoan
        {
            Id = World.NextLoanId++, Principal = amount, Remaining = amount, InterestPercent = rate, Years = years,
            MonthlyPayment = monthly, Start = World.Date, End = World.Date.AddYears(years),
        });
        Book(club, TransactionType.LoanPayout, amount, TR("tx.loan", years));
        if (IsHuman(club) && !silent)
            Notify(MessageType.Finance, "msg.sender.bank", TR("msg.bankLoan.subject"), TR("msg.bankLoan.body", TextArg.Money(amount, Currency), TextArg.Num(rate), years, TextArg.Money(monthly, Currency)));
        return null;
    }

    /// <summary>Sondertilgung eines Kredits (1 % Vorfälligkeitsentschädigung).</summary>
    public string? RepayLoan(BankLoan loan)
    {
        var club = HumanClub;
        var cost = loan.Remaining + loan.Remaining / 100;
        if (cost > club.Budget.Balance) return "neg.error.balance";
        Book(club, TransactionType.LoanRepayment, -loan.Remaining, TR("tx.repayment"));
        Book(club, TransactionType.Interest, -(loan.Remaining / 100), TR("tx.prepaymentFee"));
        club.Finance.Loans.Remove(loan);
        return null;
    }

    public static double DepositRate(int months) => months switch { >= 24 => 3.0, >= 12 => 2.5, _ => 1.5 };

    public string? MakeDeposit(long amount, int months)
    {
        var club = HumanClub;
        if (club.Finance.Deposit is not null) return "fin.error.depositExists";
        if (amount <= 0 || amount > club.Budget.Balance) return "neg.error.balance";
        months = Math.Clamp(months, 6, 36);
        club.Finance.Deposit = new FixedDeposit { Amount = amount, InterestPercent = DepositRate(months), Start = World.Date, Until = World.Date.AddMonths(months) };
        Book(club, TransactionType.Deposit, -amount, TR("tx.deposit"));
        return null;
    }

    /// <summary>
    /// Budgets umschichten: 1 € Gehalt pro Jahr = 1 € Transfer (Kapitel 8). Positiver Betrag = mehr Transferbudget
    /// auf Kosten des Gehaltsbudgets.
    /// </summary>
    public string? ShiftBudget(long transferDelta)
    {
        var b = HumanClub.Budget;
        var weekly = transferDelta / 52;
        if (transferDelta > 0 && b.WageBudget - weekly < Budgets.WageBill(HumanClub, World)) return "fin.error.wageBelowBill";
        if (transferDelta < 0 && b.TransferBudget + transferDelta < 0) return "fin.error.transferNegative";
        b.WageBudget -= weekly;
        b.TransferBudget += transferDelta;
        return null;
    }

    public string? SetTicketPrices(TicketPrices prices)
    {
        var div = DivisionFinance(HumanClub);
        prices.Standing = Math.Clamp(prices.Standing, 1, div.Standing * 4);
        prices.Seating = Math.Clamp(prices.Seating, 1, div.Seating * 4);
        prices.Box = Math.Clamp(prices.Box, 100, div.Box * 4);
        prices.SeasonTicketDiscount = Math.Clamp(prices.SeasonTicketDiscount, 0, 50);
        prices.TopMatchSurcharge = Math.Clamp(prices.TopMatchSurcharge, 0, 100);
        HumanClub.Finance.Prices = prices;
        UpdatePriceSatisfaction(HumanClub);
        return null;
    }

    /// <summary>Fans reagieren auf Preise im Vergleich zum Ligaschnitt.</summary>
    private void UpdatePriceSatisfaction(Club club)
    {
        var div = DivisionFinance(club);
        var p = club.Finance.Prices;
        var ratio = (p.Standing / (double)Math.Max(1, div.Standing) + p.Seating / (double)Math.Max(1, div.Seating)) / 2;
        club.Finance.PriceSatisfaction = ratio <= 1 ? Math.Min(1.1, 1 + (1 - ratio) * 0.15) : Math.Clamp(1 - (ratio - 1) * 0.3, 0.6, 1);
    }

    /// <summary>Saisonbudget aus erwarteten Einnahmen: Gehaltsanteil je Schwierigkeit, Transferbudget aus dem Kontostand.</summary>
    public void SetSeasonBudget(Club club)
    {
        var b = club.Budget;
        if (!HasFinances(club))
        {
            Budgets.SetSeasonBudget(club, World);
            return;
        }
        // Anteil des frei verfügbaren Geldes (Einnahmen minus Fixkosten), der in Spielergehälter fließt.
        var share = !IsHuman(club) ? 0.85 : HumanManager.Profile.Difficulty switch
        {
            Difficulty.Easy => 0.95,
            Difficulty.Hard => 0.78,
            Difficulty.Realistic => 0.72,
            _ => 0.85,
        };
        var goal = club.Board.Goal switch { SeasonGoal.Title or SeasonGoal.Europe => 1.08, SeasonGoal.Promotion => 1.05, SeasonGoal.Survival => 0.95, _ => 1.0 };
        // Nach Auf- oder Abstieg passt die Vorjahreszahl nicht mehr: zur Hälfte mit der Schätzung für die aktuelle Liga mischen.
        var last = ExpectedIncome(club);
        var income = club.Finance.LastSeason.Count == 0 ? last : (last + EstimateIncome(club)) / 2;
        var fixedCosts = -club.Finance.LastSeason.Where(kv => kv.Key is TransactionType.StaffWages or TransactionType.Upkeep or TransactionType.Travel or TransactionType.Interest or TransactionType.Youth)
            .Sum(kv => Math.Min(0, kv.Value));
        if (fixedCosts == 0) fixedCosts = (long)(income * 0.22);
        var bill = Budgets.WageBill(club, World);
        var target = (long)(Math.Max(income * 0.3, income - fixedCosts) * share * goal / 52);
        // KI: Budget folgt den Einnahmen (im Minus sparsamer), sonst schaukeln sich die Gehälter jedes Jahr hoch.
        // Mensch: etwas Luft über den laufenden Gehältern, damit Verlängerungen und Neuzugänge möglich bleiben.
        b.WageBudget = IsHuman(club) ? Math.Max((long)(bill * 1.05), target) : b.Balance < 0 ? (long)(target * 0.9) : target;
        b.TransferBudget = MarketValue.Round(Math.Max(0, b.Balance) * 0.45);
        if (club.Finance.LastLicense is LicenseOutcome.Conditions or LicenseOutcome.PointDeduction) b.TransferBudget = Math.Min(b.TransferBudget, MarketValue.Round(b.Balance * 0.2));
    }

    // ================================================================== Lizenz und Insolvenz

    private void CheckSolvency(Club club)
    {
        var f = club.Finance;
        if (club.Budget.Balance < -CreditLimit(club)) f.DaysBelowLimit++;
        else f.DaysBelowLimit = 0;
        if (f.DaysBelowLimit == 15 && IsHuman(club))
            Notify(MessageType.Finance, "msg.sender.bank", TR("msg.insolvencyWarning.subject"), TR("msg.insolvencyWarning.body", TextArg.Money(CreditLimit(club), Currency)), required: false);
        if (f.DaysBelowLimit < 30 || f.Insolvent) return;
        if (!IsHuman(club))
        {
            // KI-Vereine werden von einem Investor aufgefangen, steigen aber zwangsweise ab.
            f.Insolvent = true;
            club.Budget.Balance = 0;
            f.DaysBelowLimit = 0;
            if (World.FindLeague(club.LeagueId) is { } league) league.Deductions[club.Id] = league.Deductions.GetValueOrDefault(club.Id) + 9;
            Notify(MessageType.Finance, "msg.sender.league", TR("msg.aiInsolvency.subject", club.Name), TR("msg.aiInsolvency.body", club.Name));
            return;
        }
        f.Insolvent = true;
        Notify(MessageType.Finance, "msg.sender.board", TR("msg.insolvency.subject"), TR("msg.insolvency.body", club.Name));
        SackManager(TR("career.exit.insolvency"));
    }

    /// <summary>Lizenzierung am Saisonende: Eigenkapital und Infrastruktur für die Liga der neuen Saison.</summary>
    /// <summary>KI: Schulden abbauen, bei Überschuss Stadion und Anlagen ausbauen.</summary>
    private void AiFinance(Club club)
    {
        if (World.Date.Month != 6) return;
        var b = club.Budget;
        if (b.Balance < -CreditLimit(club) / 2 && club.Finance.Loans.Count == 0 && LoanCapacity(club) > 0)
            TakeLoan(club, Math.Min(LoanCapacity(club), -b.Balance), 5, silent: true);
        var income = Math.Max(1, ExpectedIncome(club));
        AiLicenseInvestments(club);
        if (b.Balance > income * 0.6)
        {
            // Wachsender Verein: Anlagen ausbauen, wo es am meisten bringt.
            var candidates = club.Grounds.Buildings.Where(x => Data.Building(x.Type) is { } d && !d.Land && x.Level < d.MaxLevel && !x.UnderConstruction).ToList();
            if (candidates.Count > 0)
            {
                var pick = _rng.Pick(candidates);
                var def = Data.Building(pick.Type)!;
                var cost = LevelCost(def, pick.Level + 1);
                if (cost < b.Balance * 0.3) Order(club, new ConstructionProject { Kind = ProjectKind.UpgradeBuilding, BuildingId = pick.Id, BuildingType = pick.Type, X = pick.X, Y = pick.Y, Amount = pick.Level + 1 }, cost, def.BuildDays / 2, Contractor.Standard, false);
            }
            if (club.Stadium.Capacity < 10_000 + club.Reputation * 3_500 && b.Balance > income)
                Order(club, new ConstructionProject { Kind = ProjectKind.Seating, Side = _rng.Pick(new[] { StandSide.East, StandSide.West }), Amount = 1000 }, 1_300_000, 75, Contractor.Standard, false);
        }
    }

    /// <summary>KI: zuerst die Lizenzkriterien (Flutlicht, Leistungszentrum), ambitionierte Vereine auch für die höhere Liga und Europa.</summary>
    private void AiLicenseInvestments(Club club)
    {
        var div = DivisionFinance(club);
        var league = World.FindLeague(club.LeagueId);
        var floodlight = div.FloodlightRequired;
        var stars = div.YouthCenterRequired;
        if (league is not null && World.Leagues.FirstOrDefault(l => l.CountryCode == league.CountryCode && l.Level == league.Level - 1) is { } higher
            && World.Country(club.CountryCode).Finance.Divisions.TryGetValue(higher.Id, out var up) && club.Reputation >= 8)
        {
            floodlight = Math.Max(floodlight, up.FloodlightRequired);
            stars = Math.Max(stars, up.YouthCenterRequired);
        }
        if (World.Country(club.CountryCode).Europe is { } europe && club.LeagueId == europe.League) floodlight = Math.Max(floodlight, europe.License.Floodlight);
        var running = club.Grounds.Projects.Any(p => p.State is ProjectState.Building or ProjectState.AwaitingPermit);
        if (!running && club.Stadium.Floodlight < floodlight && StadiumProjectQuote(club, ProjectKind.Floodlight, null) is { } q && q.cost < club.Budget.Balance + CreditLimit(club) / 2)
            Order(club, new ConstructionProject { Kind = ProjectKind.Floodlight, Amount = 1 }, q.cost, q.days, Contractor.Premium, false);
        if (club.Academy.Stars < stars && club.Grounds.Buildings.FirstOrDefault(x => x.Type == "youthCentre") is { UnderConstruction: false } centre
            && Data.Building("youthCentre") is { } def && centre.Level < def.MaxLevel && LevelCost(def, centre.Level + 1) < club.Budget.Balance + CreditLimit(club) / 2)
            Order(club, new ConstructionProject { Kind = ProjectKind.UpgradeBuilding, BuildingId = centre.Id, BuildingType = centre.Type, X = centre.X, Y = centre.Y, Amount = centre.Level + 1 },
                LevelCost(def, centre.Level + 1), def.BuildDays / 2, Contractor.Premium, false);
    }

    /// <summary>Wochengehalt eines Scouts aus seiner Urteilskraft.</summary>
    public static int ScoutWage(Scout s) => (int)(Math.Round(250 * Math.Pow(1.2, (s.JudgingAbility + s.JudgingPotential) / 2.0) / 100) * 100);
}
