using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record SwapOption(string Label, int? PlayerId);

/// <summary>
/// Verhandlungsdialog (Kapitel 7): erst mit dem Verein (Ablöse, Raten, Weiterverkauf, Tausch bzw. Leihbedingungen),
/// dann mit dem Spieler (Laufzeit, Gehalt, Handgeld, Prämien, Klauseln, Status). Höchstens drei Runden je Stufe.
/// </summary>
public sealed partial class NegotiationViewModel : ViewModelBase
{
    private readonly Negotiation _n;
    private readonly Action? _onClose;

    public NegotiationViewModel(Negotiation n, Action? onClose = null)
    {
        _n = n;
        _onClose = onClose;
        Player = Session.World.Players[n.PlayerId];
        var profiles = Data.PositionProfiles;
        Currency = UiText.Currency(Session.HumanClub);
        StatusOptions = Enum.GetValues<SquadStatus>().Select(s => T["squadStatus." + s]).ToList();
        SwapOptions = new[] { new SwapOption(T["neg.noSwap"], null) }
            .Concat(Session.World.SquadOf(Session.HumanClub).Where(p => p.Loan is null)
                .OrderByDescending(p => Ratings.Strength(p, profiles))
                .Select(p => new SwapOption($"{p.Name} ({UiText.Position(p.MainPosition)}, {Fmt.Money(Session.ValueOf(p), Currency)})", p.Id)))
            .ToList();
        _swap = SwapOptions[0];
        LoadOffer();
        LoadContract();
        Update();
    }

    public Player Player { get; }
    public string Currency { get; }
    public string Title => T.Format("neg.title." + _n.Type, Player.Name);
    public string PlayerLine => $"{UiText.Position(Player.MainPosition)} · {T.Format("player.ageShort", Player.Age(Session.World.Date))} · " +
                                (Player.ClubId is { } c ? Session.World.Clubs[c].Name : T["transfer.freeAgent"]);
    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<SwapOption> SwapOptions { get; }
    public bool IsLoan => _n.Type == DealType.Loan;
    public bool IsTransfer => _n.Type == DealType.Transfer;

    [ObservableProperty] private bool _clubStage;
    [ObservableProperty] private bool _playerStage;
    [ObservableProperty] private bool _finished;
    [ObservableProperty] private string _stageText = "";
    [ObservableProperty] private string _response = "";
    [ObservableProperty] private IBrush _responseBrush = Brushes.Gray;
    [ObservableProperty] private IReadOnlyList<InfoLine> _facts = Array.Empty<InfoLine>();
    [ObservableProperty] private string? _error;

    // Angebot an den Verein
    [ObservableProperty] private decimal _fee;
    [ObservableProperty] private decimal _installmentYears;
    [ObservableProperty] private decimal _sellOnPercent;
    [ObservableProperty] private SwapOption _swap;
    [ObservableProperty] private decimal _loanMonths = 12;
    [ObservableProperty] private decimal _wageShare = 100;
    [ObservableProperty] private bool _hasPurchaseOption;
    [ObservableProperty] private decimal _purchaseFee;
    [ObservableProperty] private bool _purchaseObligation;
    [ObservableProperty] private bool _recallInWinter;

    // Vertrag mit dem Spieler
    [ObservableProperty] private decimal _years = 3;
    [ObservableProperty] private decimal _weeklyWage;
    [ObservableProperty] private decimal _signingFee;
    [ObservableProperty] private int _statusIndex;
    [ObservableProperty] private decimal _appearanceBonus;
    [ObservableProperty] private decimal _goalBonus;
    [ObservableProperty] private bool _hasReleaseClause;
    [ObservableProperty] private decimal _releaseClause;
    [ObservableProperty] private decimal _yearlyRaise;
    [ObservableProperty] private decimal _relegationPercent;
    [ObservableProperty] private decimal _promotionPercent;

    private void LoadOffer()
    {
        var o = _n.Offer;
        Fee = o.Fee;
        InstallmentYears = o.InstallmentYears;
        SellOnPercent = o.SellOnPercent;
        LoanMonths = o.LoanMonths;
        WageShare = o.WageSharePercent;
        HasPurchaseOption = o.PurchaseFee is not null;
        PurchaseFee = o.PurchaseFee ?? Session.ValueOf(Player);
        PurchaseObligation = o.PurchaseObligation;
        RecallInWinter = o.RecallInWinter;
    }

    private void LoadContract()
    {
        var c = _n.Contract;
        Years = c.Years;
        WeeklyWage = c.WeeklyWage;
        SigningFee = c.SigningFee;
        StatusIndex = (int)c.Status;
        AppearanceBonus = c.AppearanceBonus;
        GoalBonus = c.GoalBonus;
        HasReleaseClause = c.ReleaseClause is not null;
        ReleaseClause = c.ReleaseClause ?? MarketValue.Round(Session.ValueOf(Player) * 2.5);
        YearlyRaise = c.YearlyRaisePercent;
        var (rel, pro) = ContractClauses.Defaults(Session.World, Session.World.Clubs[_n.BuyerId]);
        RelegationPercent = c.RelegationPercent ?? rel;
        PromotionPercent = c.PromotionPercent ?? pro;
    }

    private void Update()
    {
        ClubStage = _n.Stage == NegotiationStage.ClubTalks;
        PlayerStage = _n.Stage == NegotiationStage.PlayerTalks;
        Finished = !ClubStage && !PlayerStage;
        StageText = _n.Stage switch
        {
            NegotiationStage.ClubTalks => T.Format("neg.stage.club", _n.ClubRounds + 1, Negotiations.MaxRounds),
            NegotiationStage.PlayerTalks => T.Format("neg.stage.player", _n.PlayerRounds + 1, Negotiations.MaxRounds),
            _ => T["neg.stage." + _n.Stage],
        };
        if (_n.ResponseText(T) is { Length: > 0 } text) Response = text;
        ResponseBrush = new SolidColorBrush(_n.Stage switch
        {
            NegotiationStage.Completed => P.Good,
            NegotiationStage.Failed or NegotiationStage.Withdrawn => P.Bad,
            _ => P.Warn,
        });

        var club = Session.HumanClub;
        var b = club.Budget;
        var facts = new List<InfoLine>
        {
            new(T["neg.value"], Fmt.Money(Session.ValueOf(Player), Currency)),
        };
        if (Player.Contract is { } pc)
        {
            facts.Add(new(T["player.contract"], Fmt.Date(pc.Until)));
            facts.Add(new(T["neg.currentWage"], UiText.Wage(pc.WeeklyWage, Session.HumanManager.Profile, Currency)));
            if (pc.ReleaseClause is { } rc && _n.Type == DealType.Transfer) facts.Add(new(T["neg.releaseClause"], Fmt.Money(rc, Currency), new SolidColorBrush(P.Gold)));
        }
        if (Player.Transfer.Listed) facts.Add(new(T["neg.listed"], Player.Transfer.AskingPrice is { } ask ? Fmt.Money(ask, Currency) : "–", new SolidColorBrush(P.Good)));
        if (_n.CounterFee is { } cf && ClubStage) facts.Add(new(T["neg.counterFee"], Fmt.Money(cf, Currency), new SolidColorBrush(P.Warn)));
        if (_n.WageDemand is { } wd && PlayerStage) facts.Add(new(T["neg.wageDemand"], Fmt.Money(wd, Currency) + T["unit.perWeek"], new SolidColorBrush(P.Warn)));
        facts.Add(new(T["neg.transferBudget"], Fmt.Money(b.TransferBudget, Currency)));
        var bill = Budgets.WageBill(club, Session.World);
        facts.Add(new(T["neg.wageRoom"], Fmt.Money(Math.Max(0, b.WageBudget - bill), Currency) + T["unit.perWeek"]));
        Facts = facts;
    }

    [RelayCommand]
    private void Submit()
    {
        Error = null;
        NegotiationResponse r;
        if (ClubStage)
        {
            var offer = new ClubOffer
            {
                Fee = (long)Fee,
                InstallmentYears = (int)InstallmentYears,
                SellOnPercent = (int)SellOnPercent,
                SwapPlayerId = Swap.PlayerId,
                LoanMonths = (int)LoanMonths,
                WageSharePercent = (int)WageShare,
                PurchaseFee = HasPurchaseOption ? (long)PurchaseFee : null,
                PurchaseObligation = HasPurchaseOption && PurchaseObligation,
                RecallInWinter = RecallInWinter,
            };
            r = Session.SubmitClubOffer(_n, offer);
            if (r.Kind == ResponseKind.Counter && _n.CounterFee is { } cf && IsTransfer) Fee = cf;
            if (_n.Stage == NegotiationStage.PlayerTalks) LoadContract();
        }
        else if (PlayerStage)
        {
            var contract = new ContractOffer
            {
                Years = (int)Years,
                WeeklyWage = (int)WeeklyWage,
                SigningFee = (long)SigningFee,
                Status = (SquadStatus)StatusIndex,
                AppearanceBonus = (int)AppearanceBonus,
                GoalBonus = (int)GoalBonus,
                ReleaseClause = HasReleaseClause ? (long)ReleaseClause : null,
                YearlyRaisePercent = (int)YearlyRaise,
                RelegationPercent = (int)RelegationPercent,
                PromotionPercent = (int)PromotionPercent,
            };
            r = Session.SubmitContract(_n, contract);
            if (r.Kind == ResponseKind.Counter && _n.WageDemand is { } wd) WeeklyWage = wd;
        }
        else return;

        // Prüffehler (Budget, Fenster) ändern die Verhandlung nicht und erscheinen als Hinweis.
        if (r.TextKey.StartsWith("neg.error.", StringComparison.Ordinal)) Error = T[r.TextKey];
        Update();
    }

    [RelayCommand]
    private void Withdraw()
    {
        Session.Withdraw(_n);
        Close();
    }

    [RelayCommand]
    private void Close()
    {
        MainWindowViewModel.Instance?.CloseDialog();
        _onClose?.Invoke();
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (key == Key.Escape) { Close(); return true; }
        return false;
    }
}
