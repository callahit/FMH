using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Zeile der Transfersuche; Stärke nur, soweit der Kenntnisstand es erlaubt.</summary>
public sealed class TransferRow
{
    public TransferRow(Player p)
    {
        var s = AppState.Current.Session!;
        var w = s.World;
        var profiles = AppState.Current.Data.PositionProfiles;
        var currency = UiText.Currency(s.HumanClub);
        Player = p;
        Position = UiText.Position(p.MainPosition);
        PositionOrder = (int)p.MainPosition;
        Name = p.Name;
        Age = p.Age(w.Date);
        Club = p.ClubId is { } c ? w.Clubs[c].Name : AppState.Current.T["transfer.freeAgent"];
        var known = s.StrengthKnown(p);
        Strength = known ? Ratings.Strength(p, profiles) : 0;
        StrengthText = known ? UiText.Strength(Strength) : "?";
        StrengthBrush = new SolidColorBrush(known ? Palette.Current.AttributeColor((int)Math.Round(Ratings.PositionRating(p, p.MainPosition, profiles))) : Palette.Current.Muted);
        Value = s.ValueOf(p);
        ValueText = Fmt.Money(Value, currency);
        ContractOrder = p.Contract?.Until.DayNumber ?? 0;
        Contract = p.Contract?.Until.ToString("MM/yyyy") ?? "–";
        Report = s.ReportFor(p)?.Recommendation.ToString() ?? "";
        Knowledge = s.Knowledge(p);
        var flags = new List<string>();
        if (p.Transfer.Listed) flags.Add(AppState.Current.T["transfer.flag.listed"]);
        if (p.Transfer.LoanListed) flags.Add(AppState.Current.T["transfer.flag.loan"]);
        if (p.Loan is not null) flags.Add(AppState.Current.T["transfer.flag.onLoan"]);
        if (s.HumanClub.Shortlist.Contains(p.Id)) flags.Add("★");
        Flags = string.Join(" ", flags);
    }

    public Player Player { get; }
    public string Position { get; }
    public int PositionOrder { get; }
    public string Name { get; }
    public int Age { get; }
    public string Club { get; }
    public double Strength { get; }
    public string StrengthText { get; }
    public IBrush StrengthBrush { get; }
    public long Value { get; }
    public string ValueText { get; }
    public string Contract { get; }
    public int ContractOrder { get; }
    public string Report { get; }
    public int Knowledge { get; }
    public string Flags { get; }
}

public sealed record NegotiationRow(Negotiation Negotiation, string Player, string Type, string Stage, string Response, bool CanOpen);
public sealed record HistoryRow(string Date, string Player, string From, string To, string Fee, IBrush Brush);

public sealed partial class ScoutRow : ObservableObject
{
    public required Scout Scout { get; init; }
    public required string Name { get; init; }
    public required string Abilities { get; init; }
    [ObservableProperty] private string _assignment = "";
    [ObservableProperty] private bool _busy;
}

/// <summary>Transfermarkt (Kapitel 7): Suche, Beobachtungsliste, Verhandlungen, Scouts und Transferhistorie.</summary>
public sealed partial class TransfersPageViewModel : PageViewModel, IRefreshable
{
    public TransfersPageViewModel(ShellViewModel shell) : base(shell)
    {
        var w = Session.World;
        Filters = new List<FilterOption>
        {
            new(T["filter.all"], _ => true),
            new(T["filter.gk"], p => p == Core.Model.Position.GK),
            new(T["filter.def"], PositionInfo.IsDefender),
            new(T["filter.mid"], PositionInfo.IsMidfielder),
            new(T["filter.att"], PositionInfo.IsAttacker),
        };
        _filter = Filters[0];
        Leagues = new[] { new ValueOption(T["transfer.allLeagues"], null) }
            .Concat(w.Leagues.Select(l => new ValueOption(l.Name, l.Id)))
            .Concat(w.Countries.SelectMany(c => c.Pools).Select(p => new ValueOption(p.Name, p.Id)))
            .Append(new ValueOption(T["transfer.freeAgents"], ""))
            .ToList();
        _league = Leagues[0];
        SearchPositions = new[] { new ValueOption(T["transfer.anyPosition"], null) }
            .Concat(PositionInfo.All.Select(p => new ValueOption(UiText.Position(p), p))).ToList();
        _searchPosition = SearchPositions[0];
        _searchLeague = Leagues[0];
        Refresh();
    }

    public override string Title => T["menu.transfers"];
    public IReadOnlyList<FilterOption> Filters { get; }
    public IReadOnlyList<ValueOption> Leagues { get; }
    public IReadOnlyList<ValueOption> SearchPositions { get; }

    [ObservableProperty] private FilterOption _filter;
    [ObservableProperty] private ValueOption _league;
    [ObservableProperty] private decimal _maxAge = 40;
    [ObservableProperty] private bool _onlyListed;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private IReadOnlyList<TransferRow> _results = Array.Empty<TransferRow>();
    [ObservableProperty] private string _resultInfo = "";
    [ObservableProperty] private TransferRow? _selected;
    [ObservableProperty] private IReadOnlyList<TransferRow> _shortlist = Array.Empty<TransferRow>();
    [ObservableProperty] private TransferRow? _selectedShortlist;
    [ObservableProperty] private IReadOnlyList<NegotiationRow> _negotiations = Array.Empty<NegotiationRow>();
    [ObservableProperty] private IReadOnlyList<ScoutRow> _scouts = Array.Empty<ScoutRow>();
    [ObservableProperty] private ValueOption _searchLeague;
    [ObservableProperty] private ValueOption _searchPosition;
    [ObservableProperty] private decimal _searchMaxAge = 30;
    [ObservableProperty] private bool _onlyOwnTransfers = true;
    [ObservableProperty] private IReadOnlyList<HistoryRow> _history = Array.Empty<HistoryRow>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _budget = Array.Empty<InfoLine>();
    [ObservableProperty] private string _windowText = "";
    [ObservableProperty] private IBrush _windowBrush = Brushes.Gray;
    [ObservableProperty] private string? _message;

    partial void OnFilterChanged(FilterOption value) => Search();
    partial void OnLeagueChanged(ValueOption value) => Search();
    partial void OnMaxAgeChanged(decimal value) => Search();
    partial void OnOnlyListedChanged(bool value) => Search();
    partial void OnNameFilterChanged(string value) => Search();
    partial void OnOnlyOwnTransfersChanged(bool value) => LoadHistory();

    public void Refresh()
    {
        LoadBudget();
        Search();
        Shortlist = Session.HumanClub.Shortlist.Select(id => Session.World.Players[id]).Where(p => !p.Retired)
            .Select(p => new TransferRow(p)).ToList();
        LoadNegotiations();
        LoadScouts();
        LoadHistory();
    }

    private void LoadBudget()
    {
        var club = Session.HumanClub;
        var b = club.Budget;
        var cur = UiText.Currency(club);
        var bill = Budgets.WageBill(club, Session.World);
        Budget = new List<InfoLine>
        {
            new(T["transfer.balance"], Fmt.Money(b.Balance, cur)),
            new(T["neg.transferBudget"], Fmt.Money(b.TransferBudget, cur), new SolidColorBrush(P.Good)),
            new(T["transfer.wageBudget"], $"{Fmt.Money(bill, cur)} / {Fmt.Money(b.WageBudget, cur)}{T["unit.perWeek"]}",
                new SolidColorBrush(bill > b.WageBudget ? P.Bad : P.Text)),
        };
        var open = Session.IsTransferWindowOpen();
        WindowText = open ? T["transfer.windowOpen"] : T["transfer.windowClosed"];
        WindowBrush = new SolidColorBrush(open ? P.Good : P.Muted);
    }

    [RelayCommand]
    private void SetFilter(FilterOption? f)
    {
        if (f is not null) Filter = f;
    }

    private void Search()
    {
        var w = Session.World;
        var human = Session.HumanClub;
        var name = NameFilter.Trim();
        var query = w.Players.Values
            .Where(p => !p.Retired && !p.IsYouth && p.ClubId != human.Id)
            .Where(p => Filter.Match(p.MainPosition))
            .Where(p => p.Age(w.Date) <= MaxAge)
            .Where(p => !OnlyListed || p.Transfer.Listed || p.Transfer.LoanListed || p.ClubId is null);
        if (League.Value is string id)
            query = id == "" ? query.Where(p => p.ClubId is null) : query.Where(p => p.ClubId is { } c && w.Clubs[c].LeagueId == id);
        if (name.Length > 0) query = query.Where(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        // Bekannte Spieler zuerst, dann nach Marktwert.
        var list = query.OrderByDescending(p => Session.StrengthKnown(p)).ThenByDescending(Session.ValueOf).ToList();
        Results = list.Take(250).Select(p => new TransferRow(p)).ToList();
        ResultInfo = T.Format("transfer.results", list.Count, Results.Count);
    }

    private void LoadNegotiations()
    {
        var w = Session.World;
        Negotiations = Session.OpenNegotiations.Concat(w.Negotiations.Where(n => n.BuyerId == Session.HumanClub.Id && n.Stage is NegotiationStage.Completed or NegotiationStage.Failed).TakeLast(8))
            .Distinct()
            .Select(n => new NegotiationRow(n, w.Players[n.PlayerId].Name, T["dealType." + n.Type] + (n.Incoming ? " · " + T["transfer.incoming"] : ""),
                T["neg.stage." + n.Stage], n.LastResponse, !n.Incoming && n.Stage is NegotiationStage.ClubTalks or NegotiationStage.PlayerTalks))
            .ToList();
    }

    private void LoadScouts()
    {
        var w = Session.World;
        Scouts = Session.HumanScouts.Select(sc =>
        {
            var a = Session.AssignmentOf(sc);
            return new ScoutRow
            {
                Scout = sc, Name = sc.Name,
                Abilities = T.Format("scout.abilities", sc.JudgingAbility, sc.JudgingPotential, sc.RegionKnowledge),
                Busy = a is not null,
                Assignment = a is null ? T["scout.idle"] : a.Task switch
                {
                    ScoutTask.WatchPlayer => T.Format("scout.task.watch", w.Players[a.PlayerId!.Value].Name, Fmt.Date(a.End)),
                    ScoutTask.SearchRegion => T.Format("scout.task.search", a.LeagueId is { } l ? w.CompetitionName(l) : T["transfer.allLeagues"], Fmt.Date(a.End)),
                    _ => T.Format("scout.task.opponent", Fmt.Date(a.End)),
                },
            };
        }).ToList();
    }

    private void LoadHistory()
    {
        var w = Session.World;
        var human = Session.HumanClub.Id;
        var cur = UiText.Currency(Session.HumanClub);
        string Name(int? id) => id is { } c ? w.Clubs[c].ShortName : T["transfer.freeAgent"];
        History = w.Transfers.Where(t => !OnlyOwnTransfers || t.FromClubId == human || t.ToClubId == human)
            .Reverse().Take(200)
            .Select(t => new HistoryRow(Fmt.Date(t.Date), w.Players[t.PlayerId].Name, Name(t.FromClubId), Name(t.ToClubId),
                t.Loan ? T["transfer.loanFee"] : t.Fee > 0 ? Fmt.Money(t.Fee, cur) : T["transfer.free"],
                new SolidColorBrush(t.ToClubId == human ? P.Good : t.FromClubId == human ? P.Warn : P.Text)))
            .ToList();
    }

    [RelayCommand]
    public void OpenPlayer(TransferRow? row)
    {
        row ??= Selected ?? SelectedShortlist;
        if (row is null) return;
        var list = (Shortlist.Contains(row) ? Shortlist : Results).Select(r => r.Player).ToList();
        Shell.ShowSubPage(new PlayerPageViewModel(Shell, list, Math.Max(0, list.IndexOf(row.Player))));
    }

    [RelayCommand]
    private void ContinueNegotiation(NegotiationRow? row)
    {
        if (row is null || !row.CanOpen) return;
        Shell.ShowDialog(new NegotiationViewModel(row.Negotiation, () => { Refresh(); Shell.RefreshHeader(); }));
    }

    [RelayCommand]
    private void ScoutSearch(ScoutRow? row)
    {
        if (row is null) return;
        Message = Session.AssignScout(row.Scout, ScoutTask.SearchRegion, leagueId: SearchLeague.Value as string,
            position: SearchPosition.Value as Core.Model.Position?, maxAge: (int)SearchMaxAge) is { } err ? T[err] : T["scout.sent"];
        LoadScouts();
    }

    [RelayCommand]
    private void ScoutOpponent(ScoutRow? row)
    {
        if (row is null) return;
        Message = Session.AssignScout(row.Scout, ScoutTask.NextOpponent) is { } err ? T[err] : T["scout.sent"];
        LoadScouts();
    }

    [RelayCommand]
    private void CancelScout(ScoutRow? row)
    {
        if (row is null) return;
        Session.CancelAssignment(row.Scout);
        LoadScouts();
    }
}
