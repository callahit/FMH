using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record AttrItem(string Name, int Value, IBrush Brush, string Symbol, string Stars, bool ShowNumber, bool ShowBar, bool ShowStars, string Display);
public sealed record ActionButton(string Label, IRelayCommand Command, bool Primary = false);
public sealed record ValueOption(string Label, object? Value);
public sealed record AttrGroupVm(string Title, IReadOnlyList<AttrItem> Items);
public sealed record InfoLine(string Label, string Value, IBrush? Brush = null)
{
    public IBrush Fore => Brush ?? new SolidColorBrush(Palette.Current.Text);
}
public sealed record PositionChip(string Position, string Suitability, string Strength, IBrush Brush);
public sealed record SeasonLine(string Season, string Club, int Apps, int Minutes, int Goals, int Assists, int Yellow, int Red, string Grade);

/// <summary>Spielerprofil (Kapitel 4): Stammdaten, Attribute, Radar, Formkurve, Zustand, Statistik.</summary>
public sealed partial class PlayerPageViewModel : PageViewModel
{
    private readonly IReadOnlyList<Player> _players;

    public PlayerPageViewModel(ShellViewModel shell, IReadOnlyList<Player> players, int index) : base(shell)
    {
        _players = players;
        _index = Math.Clamp(index, 0, players.Count - 1);
        Load();
    }

    public override string Title => Player.Name;

    [ObservableProperty] private int _index;
    [ObservableProperty] private Player _player = null!;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _strength = "";
    [ObservableProperty] private IBrush _strengthBrush = Brushes.Gray;
    [ObservableProperty] private string _clubName = "";
    [ObservableProperty] private string _clubPrimary = "Gray";
    [ObservableProperty] private string _clubSecondary = "White";
    [ObservableProperty] private string _clubInitials = "";
    [ObservableProperty] private IReadOnlyList<AttrGroupVm> _groups = Array.Empty<AttrGroupVm>();
    [ObservableProperty] private bool _attributesHidden;
    [ObservableProperty] private IReadOnlyList<double> _radarValues = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<string> _radarLabels = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<Point> _formPoints = Array.Empty<Point>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _personal = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _stateLines = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<PositionChip> _positions = Array.Empty<PositionChip>();
    [ObservableProperty] private IReadOnlyList<SeasonLine> _seasons = Array.Empty<SeasonLine>();
    [ObservableProperty] private double _condition;
    [ObservableProperty] private double _practice;
    [ObservableProperty] private IBrush _conditionBrush = Brushes.Gray;
    [ObservableProperty] private string _navText = "";

    private void Load()
    {
        var p = _players[Index];
        Player = p;
        var world = Session.World;
        var profiles = Data.PositionProfiles;
        var club = p.ClubId is { } cid ? world.Clubs[cid] : null;
        ClubName = club?.Name ?? T["player.noClub"];
        ClubPrimary = club?.PrimaryColor ?? "Gray";
        ClubSecondary = club?.SecondaryColor ?? "White";
        ClubInitials = club is null ? "" : Crest.InitialsOf(club.Name);
        Subtitle = $"{UiText.Position(p.MainPosition)} · {T.Format("player.ageValue", p.Age(world.Date), Fmt.Date(p.BirthDate))} · {string.Join(", ", p.Nationalities)}";
        var rating = Ratings.PositionRating(p, p.MainPosition, profiles);
        Strength = Session.StrengthKnown(p) ? UiText.Strength(Ratings.Strength(p, profiles)) : "?";
        StrengthBrush = new SolidColorBrush(Session.StrengthKnown(p) ? P.AttributeColor((int)Math.Round(rating)) : P.Muted);
        NavText = $"{Index + 1} / {_players.Count}";

        var mode = Session.HumanManager.Profile.AttributeDisplay;
        AttributesHidden = mode == AttributeDisplay.StrengthOnly;
        var groups = p.MainPosition == Position.GK
            ? new[] { ("group.goalkeeping", AttrInfo.Goalkeeping), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) }
            : new[] { ("group.technical", AttrInfo.Technical), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) };
        // Fremde Spieler: Attribute je nach Kenntnisstand als Spanne (Kapitel 7, Scouting).
        Knowledge = Session.Knowledge(p);
        Groups = groups.Select(g => new AttrGroupVm(T[g.Item1], g.Item2.Select(a =>
        {
            var (lo, hi) = Session.AttributeRange(p, a);
            var v = (lo + hi) / 2;
            var display = lo == hi ? lo.ToString() : $"{lo}–{hi}";
            return new AttrItem(T[AttrInfo.TextKey(a)], v, new SolidColorBrush(lo == hi ? P.AttributeColor(v) : P.Muted), UiText.AttributeSymbol(v),
                UiText.Stars(v / 4.0), mode != AttributeDisplay.Stars, mode == AttributeDisplay.Bars || mode == AttributeDisplay.Numbers, mode == AttributeDisplay.Stars, display);
        }).ToList())).ToList();

        // Radar aus den sichtbaren Werten (Mitte der Spanne), damit unbekannte Spieler nichts verraten.
        int Seen(Attr a) { var (lo, hi) = Session.AttributeRange(p, a); return (lo + hi) / 2; }
        double Avg(params Attr[] attrs) => attrs.Average(Seen);
        if (p.MainPosition == Position.GK)
        {
            RadarLabels = new[] { T["radar.reflexes"], T["radar.handling"], T["radar.area"], T["radar.oneOnOne"], T["radar.distribution"], T["radar.mental"] };
            RadarValues = new[]
            {
                Avg(Attr.Reflexes), Avg(Attr.Handling), Avg(Attr.CommandOfArea, Attr.RushingOut), Avg(Attr.OneOnOnes),
                Avg(Attr.Kicking, Attr.Throwing), AttrInfo.Mental.Average(Seen),
            };
        }
        else
        {
            RadarLabels = new[] { T["radar.attack"], T["radar.technique"], T["radar.pace"], T["radar.physical"], T["radar.defence"], T["radar.mental"] };
            RadarValues = new[]
            {
                Avg(Attr.Finishing, Attr.OffTheBall, Attr.Composure, Attr.LongShots), Avg(Attr.FirstTouch, Attr.Passing, Attr.Technique, Attr.Dribbling),
                Avg(Attr.Pace, Attr.Acceleration), Avg(Attr.Strength, Attr.Stamina, Attr.Agility, Attr.JumpingReach),
                Avg(Attr.Tackling, Attr.Marking, Attr.Positioning, Attr.Anticipation), AttrInfo.Mental.Average(Seen),
            };
        }
        FormPoints = p.State.RecentGrades.Select((g, i) => new Point(i, g)).ToList();

        var currency = club is null ? "EUR" : UiText.Currency(club);
        var personal = new List<InfoLine>
        {
            new(T["player.body"], $"{p.Height} cm, {p.Weight} kg"),
            new(T["player.foot"], $"{T["foot." + p.Foot]} (L {p.LeftFoot}, R {p.RightFoot})"),
            new(T["player.reputation"], $"{p.Reputation}/20"),
        };
        if (p.Contract is { } contract)
        {
            personal.Add(new(T["player.contract"], Fmt.Date(contract.Until)));
            personal.Add(new(T["player.wage"], UiText.Wage(contract.WeeklyWage, Session.HumanManager.Profile, currency)));
        }
        Personal = personal;

        Condition = p.State.Condition;
        Practice = p.State.MatchPractice;
        ConditionBrush = new SolidColorBrush(P.ConditionColor(p.State.Condition));
        var state = new List<InfoLine>
        {
            new(T["player.condition"], $"{p.State.Condition:0} %", ConditionBrush),
            new(T["player.morale"], UiText.Morale(p.State.Morale)),
            new(T["player.form"], $"{p.State.Form}/10"),
            new(T["player.practice"], $"{p.State.MatchPractice:0} %"),
        };
        if (p.State.Injury is { } inj)
            state.Add(new(T["player.injury"], T.Format("player.injuryValue", T["injury." + inj.Type], inj.DaysRemaining), new SolidColorBrush(P.Bad)));
        if (p.State.SuspendedMatches > 0)
            state.Add(new(T["player.suspension"], T.Format("status.suspended", p.State.SuspendedMatches), new SolidColorBrush(P.Bad)));
        StateLines = state;

        Positions = p.Positions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv =>
        {
            var r = Ratings.EffectiveRating(p, kv.Key, profiles);
            return new PositionChip(UiText.Position(kv.Key), T["suit." + kv.Value], UiText.Strength(Ratings.StrengthFromRating(r)),
                new SolidColorBrush(P.AttributeColor((int)Math.Round(r))));
        }).ToList();

        Seasons = new[] { p.SeasonStats }.Concat(p.History.AsEnumerable().Reverse()).Where(s => s.SeasonYear > 0)
            .Select(s => new SeasonLine(GameSession.SeasonLabel(s.SeasonYear),
                s.ClubId is { } id && world.Clubs.TryGetValue(id, out var c) ? c.ShortName : "–",
                s.Appearances, s.Minutes, s.Goals, s.Assists, s.YellowCards, s.RedCards, UiText.Grade(s.AverageGrade)))
            .ToList();
        LoadTransfer(p, club);
        OnPropertyChanged(nameof(Title));
    }

    // ================================================================== Transfer, Vertrag, Training

    [ObservableProperty] private int _knowledge;
    [ObservableProperty] private IReadOnlyList<InfoLine> _transferLines = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<ActionButton> _actions = Array.Empty<ActionButton>();
    [ObservableProperty] private bool _isOwn;
    [ObservableProperty] private IReadOnlyList<ValueOption> _focusOptions = Array.Empty<ValueOption>();
    [ObservableProperty] private ValueOption? _focus;
    [ObservableProperty] private IReadOnlyList<ValueOption> _retrainOptions = Array.Empty<ValueOption>();
    [ObservableProperty] private ValueOption? _retrain;
    [ObservableProperty] private string? _actionMessage;
    [ObservableProperty] private string _reportText = "";
    [ObservableProperty] private bool _hasReport;
    private bool _loading;

    private void LoadTransfer(Player p, Club? club)
    {
        _loading = true;
        var s = Session;
        var human = s.HumanClub;
        IsOwn = p.ClubId == human.Id;
        var currency = UiText.Currency(human);
        var lines = new List<InfoLine> { new(T["neg.value"], Fmt.Money(s.ValueOf(p), currency)) };
        if (!IsOwn) lines.Add(new(T["player.knowledge"], $"{Knowledge} %", new SolidColorBrush(Knowledge >= 70 ? P.Good : Knowledge >= 40 ? P.Warn : P.Muted)));
        if (p.Contract is { } c) lines.Add(new(T["neg.status"], T["squadStatus." + c.Status]));
        if (p.Contract?.ReleaseClause is { } rc) lines.Add(new(T["neg.releaseClause"], Fmt.Money(rc, currency)));
        if (p.Contract is { ExtensionOptionYears: > 0 } option)
            lines.Add(new(T["player.extensionOption"], T.Format("player.extensionOptionValue", option.ExtensionOptionYears)));
        if (p.Transfer.Listed) lines.Add(new(T["neg.listed"], p.Transfer.AskingPrice is { } ask ? Fmt.Money(ask, currency) : "–", new SolidColorBrush(P.Good)));
        var sellOns = TransferTerms.ActiveSellOns(p.Transfer);
        if (sellOns.Count > 0)
            lines.Add(new(T["neg.sellOn"], string.Join(", ", sellOns.Select(r => $"{(s.World.Clubs.TryGetValue(r.ClubId, out var club) ? club.ShortName : "?")} {r.Percent}%"))));
        if (p.Transfer.LoanListed) lines.Add(new(T["player.loanListed"], T["ui.yes"], new SolidColorBrush(P.Good)));
        if (p.Loan is { } loan)
        {
            lines.Add(new(T["player.onLoan"], T.Format("player.onLoanValue", s.World.Clubs[loan.ParentClubId].Name, Fmt.Date(loan.Until))));
            if (loan.PlayingTimeGuarantee)
                lines.Add(new(T["player.guarantee"], T.Format("player.guaranteeValue", loan.Appearances, loan.ClubMatches),
                    new SolidColorBrush(TransferTerms.GuaranteeBreached(loan) ? P.Bad : P.Good)));
        }
        if (p.PreContract is { } pre) lines.Add(new(T["player.preContract"], s.World.Clubs[pre.ClubId].Name, new SolidColorBrush(P.Warn)));
        if (p.IsYouth) lines.Add(new(T["player.youth"], T["ui.yes"]));
        TransferLines = lines;

        var report = s.ReportFor(p);
        HasReport = report is not null && !IsOwn;
        ReportText = report is null ? "" : $"{T["player.report"]} {report.Recommendation} · {UiText.Stars(report.StrengthStars)} / {UiText.Stars(report.PotentialStars)}\n{report.SummaryText(T)}";

        var actions = new List<ActionButton>();
        if (IsOwn && p.IsYouth)
        {
            actions.Add(new(T["player.action.promote"], new RelayCommand(() => Act(s.PromoteYouth(p))), true));
            actions.Add(new(T["player.action.releaseYouth"], new RelayCommand(() => Act(s.ReleaseYouth(p)))));
        }
        else if (IsOwn)
        {
            if (p.Loan is null) actions.Add(new(T["player.action.renew"], new RelayCommand(() => Negotiate(DealType.Renewal)), true));
            actions.Add(new(p.Transfer.Listed ? T["player.action.unlist"] : T["player.action.list"], new RelayCommand(() => { s.SetTransferListed(p, !p.Transfer.Listed); Load(); })));
            actions.Add(new(p.Transfer.LoanListed ? T["player.action.unlistLoan"] : T["player.action.listLoan"], new RelayCommand(() => { s.SetLoanListed(p, !p.Transfer.LoanListed); Load(); })));
            if (p.Loan is { PurchaseFee: not null }) actions.Add(new(T["player.action.buyOption"], new RelayCommand(() => Act(s.ExercisePurchaseOption(p)))));
            if (s.CanExerciseExtensionOption(p)) actions.Add(new(T["player.action.extensionOption"], new RelayCommand(() => Act(s.ExerciseExtensionOption(p)))));
            if (p.Age(s.World.Date) <= 18 && p.Loan is null) actions.Add(new(T["player.action.toYouth"], new RelayCommand(() => Act(s.DemoteToYouth(p)))));
        }
        else if (!p.Retired)
        {
            if (p.ClubId is null) actions.Add(new(T["player.action.signFree"], new RelayCommand(() => Negotiate(DealType.FreeAgent)), true));
            else if (!p.IsYouth)
            {
                actions.Add(new(T["player.action.buy"], new RelayCommand(() => Negotiate(DealType.Transfer)), true));
                actions.Add(new(T["player.action.loan"], new RelayCommand(() => Negotiate(DealType.Loan))));
                if (s.CanPreContract(p)) actions.Add(new(T["player.action.preContract"], new RelayCommand(() => Negotiate(DealType.PreContract))));
            }
            if (p.Loan is { } l && l.ParentClubId == human.Id && (l.RecallInWinter || TransferTerms.GuaranteeBreached(l)))
                actions.Add(new(T["player.action.recall"], new RelayCommand(() => Act(s.RecallLoan(p)))));
            if (s.CanExerciseExtensionOption(p))
                actions.Add(new(T["player.action.extensionOption"], new RelayCommand(() => Act(s.ExerciseExtensionOption(p)))));
            actions.Add(new(human.Shortlist.Contains(p.Id) ? T["player.action.unwatch"] : T["player.action.watch"], new RelayCommand(() => { s.ToggleShortlist(p); Load(); })));
            foreach (var scout in s.HumanScouts.Where(sc => s.AssignmentOf(sc) is null).Take(1))
                actions.Add(new(T.Format("player.action.scout", scout.Name), new RelayCommand(() => Act(s.AssignScout(scout, ScoutTask.WatchPlayer, p.Id), "player.scoutSent"))));
        }
        Actions = actions;

        FocusOptions = new[] { new ValueOption(T["training.noFocus"], null) }
            .Concat((p.MainPosition == Core.Model.Position.GK ? AttrInfo.Goalkeeping.Concat(AttrInfo.Mental) : AttrInfo.Technical.Concat(AttrInfo.Mental).Concat(AttrInfo.Physical))
                .Select(a => new ValueOption(T[AttrInfo.TextKey(a)], a))).ToList();
        Focus = FocusOptions.FirstOrDefault(o => Equals(o.Value, p.Training.Focus)) ?? FocusOptions[0];
        RetrainOptions = new[] { new ValueOption(T["training.noRetrain"], null) }
            .Concat(PositionInfo.All.Where(x => p.SuitabilityFor(x) != Suitability.Natural)
                .Select(x => new ValueOption($"{UiText.Position(x)} ({T["suit." + p.SuitabilityFor(x)]})", x))).ToList();
        Retrain = RetrainOptions.FirstOrDefault(o => Equals(o.Value, p.Training.Retrain)) ?? RetrainOptions[0];
        _loading = false;
    }

    partial void OnFocusChanged(ValueOption? value)
    {
        if (_loading || value is null || !IsOwn) return;
        Player.Training.Focus = value.Value as Attr?;
        Player.Training.FocusProgress = 0;
    }

    partial void OnRetrainChanged(ValueOption? value)
    {
        if (_loading || value is null || !IsOwn) return;
        Player.Training.Retrain = value.Value as Core.Model.Position?;
        Player.Training.RetrainProgress = 0;
    }

    private void Act(string? errorKey, string? successKey = null)
    {
        ActionMessage = errorKey is null ? successKey is null ? null : T[successKey] : T[errorKey];
        Load();
        Shell.RefreshHeader();
    }

    private void Negotiate(DealType type)
    {
        var (n, error) = Session.StartNegotiation(Player, type);
        if (n is null)
        {
            ActionMessage = T[error!];
            return;
        }
        ActionMessage = null;
        Shell.ShowDialog(new NegotiationViewModel(n, () => { Load(); Shell.RefreshHeader(); }));
    }

    [RelayCommand]
    private void Previous()
    {
        Index = (Index + _players.Count - 1) % _players.Count;
        Load();
    }

    [RelayCommand]
    private void Next()
    {
        Index = (Index + 1) % _players.Count;
        Load();
    }

    [RelayCommand]
    private void Back() => Shell.Back();

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        switch (key)
        {
            case Avalonia.Input.Key.Left: Previous(); return true;
            case Avalonia.Input.Key.Right: Next(); return true;
        }
        return false;
    }
}
