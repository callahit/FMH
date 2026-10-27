using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Finanzen: Kredite, Festgeld, Budgets, Lizenz und Insolvenz.</summary>
public sealed partial class FinanceModule
{
    // ================================================================== Kredite, Festgeld, Budgets

    public string? TakeLoan(long amount, int years) => TakeLoan(HumanClub, amount, years, silent: false);

    public string? TakeLoan(Club club, long amount, int years, bool silent, long? capacity = null)
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
        Board.SackManager(TR("career.exit.insolvency"));
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
                var pick = Rng.Pick(candidates);
                var def = Data.Building(pick.Type)!;
                var cost = ClubRules.LevelCost(def, pick.Level + 1);
                if (cost < b.Balance * 0.3) Facilities.Order(club, new ConstructionProject { Kind = ProjectKind.UpgradeBuilding, BuildingId = pick.Id, BuildingType = pick.Type, X = pick.X, Y = pick.Y, Amount = pick.Level + 1 }, cost, def.BuildDays / 2, Contractor.Standard, false);
            }
            if (club.Stadium.Capacity < 10_000 + club.Reputation * 3_500 && b.Balance > income)
                Facilities.Order(club, new ConstructionProject { Kind = ProjectKind.Seating, Side = Rng.Pick(new[] { StandSide.East, StandSide.West }), Amount = 1000 }, 1_300_000, 75, Contractor.Standard, false);
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
        if (!running && club.Stadium.Floodlight < floodlight && Facilities.StadiumProjectQuote(club, ProjectKind.Floodlight, null) is { } q && q.cost < club.Budget.Balance + CreditLimit(club) / 2)
            Facilities.Order(club, new ConstructionProject { Kind = ProjectKind.Floodlight, Amount = 1 }, q.cost, q.days, Contractor.Premium, false);
        if (club.Academy.Stars < stars && club.Grounds.Buildings.FirstOrDefault(x => x.Type == "youthCentre") is { UnderConstruction: false } centre
            && Data.Building("youthCentre") is { } def && centre.Level < def.MaxLevel && ClubRules.LevelCost(def, centre.Level + 1) < club.Budget.Balance + CreditLimit(club) / 2)
            Facilities.Order(club, new ConstructionProject { Kind = ProjectKind.UpgradeBuilding, BuildingId = centre.Id, BuildingType = centre.Type, X = centre.X, Y = centre.Y, Amount = centre.Level + 1 },
                ClubRules.LevelCost(def, centre.Level + 1), def.BuildDays / 2, Contractor.Premium, false);
    }
}
