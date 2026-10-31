using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record FinanceRow(string Label, string Current, string Last, IBrush Brush, FontWeight Weight);

public sealed record LoanLine(BankLoan Loan, string Text, string Detail, IRelayCommand Repay);

public sealed record SeasonFinanceLine(string Season, string Revenue, string Expenses, string BreakEven, string Ratio, IBrush Brush);

public sealed record SponsorLine(string Slot, string Company, string Value, string Until, IBrush Brush);

/// <summary>Finanzen (Kapitel 8): Überblick, Saisonbilanz, Verlauf, Kredite, Festgeld, Budgets, Eintrittspreise, Sponsoren.</summary>
public sealed partial class FinancesPageViewModel : PageViewModel, IRefreshable
{
    public static readonly int[] LoanYearOptions = { 1, 2, 3, 5, 10 };
    public static readonly int[] DepositMonthOptions = { 6, 12, 24 };

    public FinancesPageViewModel(ShellViewModel shell) : base(shell)
    {
        var prices = Club.Finance.Prices;
        _standing = prices.Standing;
        _seating = prices.Seating;
        _box = prices.Box;
        _seasonDiscount = prices.SeasonTicketDiscount;
        _topSurcharge = prices.TopMatchSurcharge;
        LoanYears = LoanYearOptions.Select(y => T.Format("fin.years", y)).ToList();
        DepositMonths = DepositMonthOptions.Select(m => T.Format("fin.months", m, Fmt.Num(GameSession.DepositRate(m), "0.0"))).ToList();
        Refresh();
    }

    private Club Club => Session.HumanClub;
    private string Currency => UiText.Currency(Club);
    public override string Title => T["menu.finances"];

    [ObservableProperty] private IReadOnlyList<InfoLine> _overview = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<FinanceRow> _rows = Array.Empty<FinanceRow>();
    [ObservableProperty] private IReadOnlyList<Point> _balanceSeries = Array.Empty<Point>();
    [ObservableProperty] private IReadOnlyList<Point> _incomeSeries = Array.Empty<Point>();
    [ObservableProperty] private double _chartMax = 1;
    [ObservableProperty] private double _chartMin;
    [ObservableProperty] private double _chartX = 1;
    [ObservableProperty] private string _chartText = "";
    [ObservableProperty] private IReadOnlyList<LoanLine> _loans = Array.Empty<LoanLine>();
    [ObservableProperty] private IReadOnlyList<SponsorLine> _sponsors = Array.Empty<SponsorLine>();
    [ObservableProperty] private string _depositText = "";
    [ObservableProperty] private bool _hasDeposit;
    [ObservableProperty] private string _loanText = "";
    [ObservableProperty] private string _priceText = "";
    [ObservableProperty] private string _sponsorText = "";
    [ObservableProperty] private string? _result;
    [ObservableProperty] private IReadOnlyList<InfoLine> _licenseLines = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _ffpLines = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<SeasonFinanceLine> _seasonLines = Array.Empty<SeasonFinanceLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _europeLines = Array.Empty<InfoLine>();

    [ObservableProperty] private decimal _loanAmount = 1_000_000;
    [ObservableProperty] private int _loanYearIndex = 2;
    [ObservableProperty] private decimal _depositAmount = 500_000;
    [ObservableProperty] private int _depositMonthIndex = 1;
    [ObservableProperty] private decimal _shiftAmount = 500_000;
    [ObservableProperty] private decimal _standing;
    [ObservableProperty] private decimal _seating;
    [ObservableProperty] private decimal _box;
    [ObservableProperty] private decimal _seasonDiscount;
    [ObservableProperty] private decimal _topSurcharge;

    public IReadOnlyList<string> LoanYears { get; }
    public IReadOnlyList<string> DepositMonths { get; }
    public bool NoLoans => Loans.Count == 0;
    partial void OnLoansChanged(IReadOnlyList<LoanLine> value) => OnPropertyChanged(nameof(NoLoans));

    public void Refresh()
    {
        var s = Session;
        var club = Club;
        var b = club.Budget;
        var f = club.Finance;
        string M(long v) => Fmt.Money(v, Currency);
        var bill = Budgets.WageBill(club, s.World);
        var forecast = TransferForecast.ForClub(s.World, club);
        var overview = new List<InfoLine>
        {
            new(T["transfer.balance"], M(b.Balance), new SolidColorBrush(b.Balance < 0 ? P.Bad : P.Text)),
            new(T["neg.transferBudget"], M(b.TransferBudget)),
            new(T["fin.wageBudget"], M(b.WageBudget) + T["unit.perWeek"]),
            new(T["fin.wageBill"], M(bill) + T["unit.perWeek"], new SolidColorBrush(bill > b.WageBudget ? P.Warn : P.Text)),
            new(T["fin.equity"], M(s.Equity(club))),
            new(T["fin.creditLimit"], M(s.CreditLimit(club))),
            new(T["fin.expectedIncome"], M(s.ExpectedIncome(club))),
            new(T["fin.license"], T["fin.license." + f.LastLicense]),
            new(T["fin.seasonTickets"], Fmt.Int(f.SeasonTickets)),
            new(T["fin.stadium"], $"{s.StadiumDisplayName(club)} ({Fmt.Int(club.Stadium.Capacity)})"),
            new(T["fin.obligations"], M(forecast.Sum(x => x.TotalOut)), new SolidColorBrush(P.Warn)),
        };
        foreach (var line in forecast)
        {
            overview.Add(new(
                T.Format("fin.obligations.season", GameSession.SeasonLabel(line.SeasonYear)),
                $"{T["fin.obligations.wages"]} {M(line.Wages)} · {T["fin.obligations.out"]} {M(line.InstalmentsOut)} · {T["fin.obligations.in"]} {M(line.InstalmentsIn)}",
                new SolidColorBrush(P.Muted)));
        }
        overview.Add(new(T["fin.obligations.total"], M(forecast.Sum(x => x.TotalOut - x.TotalIn))));
        Overview = overview;

        var types = f.Season.Keys.Concat(f.LastSeason.Keys).Distinct()
            .Where(t => t is not (TransactionType.LoanPayout or TransactionType.LoanRepayment or TransactionType.Deposit))
            .OrderByDescending(t => f.Season.GetValueOrDefault(t) + f.LastSeason.GetValueOrDefault(t)).ToList();
        var rows = types.Select(t =>
        {
            var cur = f.Season.GetValueOrDefault(t);
            return new FinanceRow(T["txType." + t], M(cur), M(f.LastSeason.GetValueOrDefault(t)),
                new SolidColorBrush(cur < 0 ? P.Bad : cur > 0 ? P.Good : P.Muted), FontWeight.Normal);
        }).ToList();
        long Sum(Dictionary<TransactionType, long> d) => d.Where(kv => types.Contains(kv.Key)).Sum(kv => kv.Value);
        var total = Sum(f.Season);
        rows.Add(new FinanceRow(T["fin.result"], M(total), M(Sum(f.LastSeason)), new SolidColorBrush(total < 0 ? P.Bad : P.Good), FontWeight.Bold));
        Rows = rows;

        var history = f.History.TakeLast(24).ToList();
        BalanceSeries = history.Select((h, i) => new Point(i, h.Balance / 1_000_000.0)).ToList();
        IncomeSeries = history.Select((h, i) => new Point(i, (h.Income + h.Expenses) / 1_000_000.0)).ToList();
        ChartX = Math.Max(1, history.Count - 1);
        var values = BalanceSeries.Concat(IncomeSeries).Select(p => p.Y).DefaultIfEmpty(0).ToList();
        ChartMin = Math.Min(0, values.Min());
        ChartMax = Math.Max(0.1, values.Max());
        ChartText = history.Count == 0 ? T["fin.chartEmpty"] : T.Format("fin.chartRange", $"{history[0].Month:00}/{history[0].Year}", $"{history[^1].Month:00}/{history[^1].Year}");

        Loans = f.Loans.Select(l => new LoanLine(l,
            T.Format("fin.loanLine", M(l.Remaining), Fmt.Num(l.InterestPercent, "0.0")),
            T.Format("fin.loanDetail", M(l.MonthlyPayment), Fmt.Date(l.End)),
            new RelayCommand(() => Show(s.RepayLoan(l), "fin.repaid")))).ToList();
        LoanText = T.Format("fin.loanOffer", M(s.LoanCapacity(club)), Fmt.Num(s.InterestRate(club), "0.0"));
        HasDeposit = f.Deposit is not null;
        DepositText = f.Deposit is { } d
            ? T.Format("fin.depositLine", M(d.Amount), Fmt.Num(d.InterestPercent, "0.0"), Fmt.Date(d.Until))
            : T["fin.noDeposit"];
        PriceText = T.Format("fin.priceMood", (int)Math.Round(f.PriceSatisfaction * 100));
        Sponsors = s.AllSlots().Select(x =>
        {
            var slot = T["slot." + x.slot] + (x.slot == SponsorSlot.Board ? $" {x.index + 1}" : "");
            if (s.DealFor(club, x.slot, x.index) is { } deal)
                return new SponsorLine(slot, deal.Company, M(deal.PerSeason), GameSession.SeasonLabel(deal.UntilSeason), new SolidColorBrush(P.Text));
            var offers = club.SponsorOffers.Count(o => o.Slot == x.slot && o.Index == x.index);
            return new SponsorLine(slot, offers > 0 ? T.Format("fin.offers", offers) : T["fin.free"], "", "", new SolidColorBrush(offers > 0 ? P.Gold : P.Muted));
        }).ToList();
        RefreshLicense();
        SponsorText = T.Format("fin.sponsorTotal", M(club.Sponsors.Where(x => x.UntilSeason >= s.World.SeasonYear).Sum(x => x.PerSeason)));
    }

    private void RefreshLicense()
    {
        var s = Session;
        var club = Club;
        var lic = club.License;
        var country = s.World.Country(club.CountryCode);
        var fin = country.Finance;
        string M(long v) => Fmt.Money(v, Currency);
        string D(string md)
        {
            var parts = md.Split('-');
            var month = int.Parse(parts[0]);
            var year = month < country.Season.StartMonth ? s.World.SeasonYear + 1 : s.World.SeasonYear;
            return Fmt.Date(new DateOnly(year, month, int.Parse(parts[1])));
        }
        IBrush B(bool ok) => new SolidColorBrush(ok ? P.Good : P.Bad);
        var div = s.DivisionFinance(club);
        var lines = new List<InfoLine>
        {
            new(T["license.stage"], T["licenseStage." + lic.Stage]),
            new(T["license.outcome"], T["fin.license." + lic.Outcome], B(lic.Outcome is LicenseOutcome.Granted or LicenseOutcome.Conditions)),
            new(T["license.dates"], T.Format("license.datesValue", D(fin.LicenseApplication), D(fin.LicenseDecision), D(fin.LicenseDeadline))),
            new(T["license.equity"], $"{M(s.Equity(club))} / {M(div.MinEquity)}", B(s.Equity(club) >= div.MinEquity)),
            new(T["license.floodlight"], $"{club.Stadium.Floodlight} / {div.FloodlightRequired}", B(club.Stadium.Floodlight >= div.FloodlightRequired)),
            new(T["license.academy"], $"{club.Academy.Stars} / {div.YouthCenterRequired}", B(club.Academy.Stars >= div.YouthCenterRequired)),
            new(T["license.conditions"], lic.Conditions.Count == 0 ? T["ui.none"] : string.Join(", ", lic.Conditions.Select(c => T["condition." + c]))),
            new(T["license.promotion"], lic.PromotionAllowed ? T["ui.yes"] : T["ui.no"], B(lic.PromotionAllowed)),
            new(T["license.uefa"], lic.UefaLicense ? T["ui.yes"] : lic.UefaReasons.Count == 0 ? T["license.notDecided"] : string.Join(", ", lic.UefaReasons.Select(r => T[r])), B(lic.UefaLicense)),
        };
        LicenseLines = lines;
        var rules = country.Europe?.Ffp;
        FfpLines = new List<InfoLine>
        {
            new(T["ffp.status"], T["ffp." + lic.Ffp], B(lic.Ffp is FfpStatus.Compliant)),
            new(T["ffp.breakEven"], M(lic.FfpBreakEven) + (rules is null ? "" : " / " + M(-rules.AcceptableDeviation)), B(rules is null || lic.FfpBreakEven >= -rules.AcceptableDeviation)),
            new(T["ffp.squadCost"], Fmt.Num(lic.FfpSquadCost * 100, "0") + " %" + (rules is null ? "" : " / " + Fmt.Num(rules.SquadCostRatio * 100, "0") + " %"), B(rules is null || lic.FfpSquadCost <= rules.SquadCostRatio)),
            new(T["ffp.excluded"], lic.ExcludedUntilSeason >= s.World.SeasonYear ? GameSession.SeasonLabel(lic.ExcludedUntilSeason) : T["ui.no"]),
        };
        SeasonLines = club.Finance.Seasons.AsEnumerable().Reverse().Select(x => new SeasonFinanceLine(GameSession.SeasonLabel(x.SeasonYear), M(x.Revenue), M(x.Expenses),
            M(x.BreakEven), Fmt.Num(x.SquadCostRatio * 100, "0") + " %", new SolidColorBrush(x.BreakEven < 0 ? P.Bad : P.Good))).ToList();
        var europe = new List<InfoLine>();
        foreach (var year in new[] { s.World.SeasonYear, s.World.SeasonYear + 1 })
            foreach (var e in s.EuropeanEntriesOf(year))
            {
                var stage = e.Stage < 0 ? T["europe.notStarted"]
                    : s.EuropeCompetitionOf(e.CompetitionId) is { } comp && e.Stage < comp.Stages.Count ? T[comp.Stages[e.Stage].Key]
                    : s.World.FindCup(e.CompetitionId)?.Rounds.ElementAtOrDefault(e.Stage) is { NameKey: { Length: > 0 } key } ? T[key] : "";
                if (e.Eliminated) stage += " · " + T["europe.out"];
                europe.Add(new InfoLine($"{GameSession.SeasonLabel(year)} · {T["europe." + e.CompetitionId]}", $"{s.World.Clubs[e.ClubId].Name} – {stage}",
                    e.ClubId == club.Id ? new SolidColorBrush(P.Gold) : null));
            }
        EuropeLines = europe;
    }

    public bool NoEurope => EuropeLines.Count == 0;
    partial void OnEuropeLinesChanged(IReadOnlyList<InfoLine> value) => OnPropertyChanged(nameof(NoEurope));

    private void Show(string? error, string okKey, params object[] args)
    {
        Result = error is null ? T.Format(okKey, args) : T[error];
        Refresh();
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void TakeLoan() =>
        Show(Session.TakeLoan((long)LoanAmount, LoanYearOptions[Math.Clamp(LoanYearIndex, 0, LoanYearOptions.Length - 1)]), "fin.loanTaken");

    [RelayCommand]
    private void MakeDeposit() =>
        Show(Session.MakeDeposit((long)DepositAmount, DepositMonthOptions[Math.Clamp(DepositMonthIndex, 0, DepositMonthOptions.Length - 1)]), "fin.depositMade");

    [RelayCommand]
    private void ShiftToTransfer() => Show(Session.ShiftBudget((long)ShiftAmount), "fin.shifted");

    [RelayCommand]
    private void ShiftToWages() => Show(Session.ShiftBudget(-(long)ShiftAmount), "fin.shifted");

    [RelayCommand]
    private void SavePrices()
    {
        var p = new TicketPrices
        {
            Standing = (int)Standing, Seating = (int)Seating, Box = (int)Box,
            SeasonTicketDiscount = (int)SeasonDiscount, TopMatchSurcharge = (int)TopSurcharge,
        };
        Show(Session.SetTicketPrices(p), "fin.pricesSaved");
        var saved = Club.Finance.Prices;
        Standing = saved.Standing;
        Seating = saved.Seating;
        Box = saved.Box;
        SeasonDiscount = saved.SeasonTicketDiscount;
        TopSurcharge = saved.TopMatchSurcharge;
    }
}
