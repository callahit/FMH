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
        Overview = new List<InfoLine>
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
        };

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
        SponsorText = T.Format("fin.sponsorTotal", M(club.Sponsors.Where(x => x.UntilSeason >= s.World.SeasonYear).Sum(x => x.PerSeason)));
    }

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
