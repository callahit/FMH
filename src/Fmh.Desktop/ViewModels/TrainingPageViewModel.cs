using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Eine Einheit im Wochenplan (Vormittag oder Nachmittag eines Tages).</summary>
public sealed partial class SessionSlot : ObservableObject
{
    private readonly TrainingPageViewModel _owner;

    public SessionSlot(TrainingPageViewModel owner, int index, int selected)
    {
        _owner = owner;
        Index = index;
        _selectedIndex = selected;
    }

    public int Index { get; }
    public IReadOnlyList<string> Options => _owner.BlockOptions;

    [ObservableProperty] private int _selectedIndex;

    partial void OnSelectedIndexChanged(int value) => _owner.SetSession(Index, value);
}

public sealed record TrainingDay(string Day, string Fixture, IBrush FixtureBrush, SessionSlot Morning, SessionSlot Afternoon);
public sealed record FamiliarityLine(string Formation, double Value, string Text, IBrush Brush, FontWeight Weight);
public sealed record IndividualLine(string Name, string Position, string Focus, string Retrain, double Progress, bool HasRetrain);

/// <summary>Training (Kapitel 5): Wochenplan, Intensität, Taktikvertrautheit, Trainingslager und Individualtraining.</summary>
public sealed partial class TrainingPageViewModel : PageViewModel, IRefreshable
{
    private static readonly string[] DayKeys = { "day.mo", "day.tu", "day.we", "day.th", "day.fr", "day.sa", "day.su" };
    private bool _loading;

    public TrainingPageViewModel(ShellViewModel shell) : base(shell)
    {
        BlockOptions = Enum.GetValues<TrainingBlock>().Select(b => T["block." + b]).ToList();
        Intensities = Enum.GetValues<TrainingIntensity>().Select(i => T["intensity." + i]).ToList();
        CampGoals = Enum.GetValues<CampGoal>().Select(g => T["camp.goal." + g]).ToList();
        Refresh();
    }

    public override string Title => T["menu.training"];
    public IReadOnlyList<string> BlockOptions { get; }
    public IReadOnlyList<string> Intensities { get; }
    public IReadOnlyList<string> CampGoals { get; }

    private Club Club => Session.HumanClub;

    [ObservableProperty] private IReadOnlyList<TrainingDay> _days = Array.Empty<TrainingDay>();
    [ObservableProperty] private int _intensityIndex;
    [ObservableProperty] private string _loadText = "";
    [ObservableProperty] private IBrush _loadBrush = Brushes.Gray;
    [ObservableProperty] private IReadOnlyList<FamiliarityLine> _familiarity = Array.Empty<FamiliarityLine>();
    [ObservableProperty] private IReadOnlyList<IndividualLine> _individual = Array.Empty<IndividualLine>();
    [ObservableProperty] private string _coachText = "";
    [ObservableProperty] private int _campGoalIndex;
    [ObservableProperty] private decimal _campInDays = 7;
    [ObservableProperty] private decimal _campLength = 5;
    [ObservableProperty] private string _campLocation = "";
    [ObservableProperty] private string _campCost = "";
    [ObservableProperty] private string? _campInfo;
    [ObservableProperty] private bool _hasCamp;
    [ObservableProperty] private string? _message;

    public void Refresh()
    {
        _loading = true;
        var plan = Club.Training;
        var monday = Session.World.Date.AddDays(-(((int)Session.World.Date.DayOfWeek + 6) % 7));
        var fixtures = Session.AllFixtures.Where(f => f.Involves(Club.Id) && f.Date >= monday && f.Date < monday.AddDays(7)).ToList();
        Days = Enumerable.Range(0, 7).Select(d =>
        {
            var date = monday.AddDays(d);
            var f = fixtures.FirstOrDefault(x => x.Date == date);
            var fixtureText = f is null ? Fmt.ShortDate(date)
                : $"{Fmt.ShortDate(date)} · {T["training.matchDay"]}: {Session.World.Clubs[f.HomeId == Club.Id ? f.AwayId : f.HomeId].ShortName}";
            return new TrainingDay(T[DayKeys[d]], fixtureText, new SolidColorBrush(f is null ? P.Muted : P.Gold),
                new SessionSlot(this, d * 2, (int)plan.Sessions[d * 2]), new SessionSlot(this, d * 2 + 1, (int)plan.Sessions[d * 2 + 1]));
        }).ToList();
        IntensityIndex = (int)plan.Intensity;
        UpdateLoad();

        var current = Club.Tactic.FormationId;
        Familiarity = Data.Formations
            .Select(f => (f, v: Session.Familiarity(Club, f.Id)))
            .Where(x => x.f.Id == current || x.v > 30)
            .OrderByDescending(x => x.f.Id == current).ThenByDescending(x => x.v)
            .Select(x => new FamiliarityLine(x.f.Name, x.v, $"{x.v:0} %", new SolidColorBrush(x.v >= 70 ? P.Good : x.v >= 45 ? P.Warn : P.Bad),
                x.f.Id == current ? FontWeight.Bold : FontWeight.Normal))
            .ToList();

        Individual = Session.World.SquadOf(Club)
            .Where(p => p.Training.Focus is not null || p.Training.Retrain is not null)
            .Select(p => new IndividualLine(p.Name, UiText.Position(p.MainPosition),
                p.Training.Focus is { } a ? T[AttrInfo.TextKey(a)] : "–",
                p.Training.Retrain is { } r ? $"{UiText.Position(r)} ({T["suit." + p.SuitabilityFor(r)]})" : "–",
                p.Training.RetrainProgress * 100, p.Training.Retrain is not null))
            .ToList();
        CoachText = T.Format("training.coach", Club.CoachQuality, Club.YouthFacilities);

        HasCamp = Club.Camp is not null;
        CampInfo = Club.Camp is { } camp
            ? T.Format("camp.booked", camp.Location, Fmt.Date(camp.Start), Fmt.Date(camp.End), T["camp.goal." + camp.Goal])
            : null;
        if (string.IsNullOrEmpty(CampLocation)) CampLocation = T["camp.defaultLocation"];
        UpdateCampCost();
        _loading = false;
    }

    public void SetSession(int index, int block)
    {
        if (_loading || block < 0) return;
        Club.Training.Sessions[index] = (TrainingBlock)block;
        UpdateLoad();
    }

    partial void OnIntensityIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        Club.Training.Intensity = (TrainingIntensity)value;
        UpdateLoad();
    }

    /// <summary>Wochenbelastung: Summe des Konditionsverbrauchs aller Einheiten.</summary>
    private void UpdateLoad()
    {
        var plan = Club.Training;
        var load = plan.Sessions.Sum(b => GameSession.ConditionCost(b, plan.Intensity));
        var key = load switch { > 30 => "training.load.high", > 15 => "training.load.normal", _ => "training.load.low" };
        LoadText = T.Format(key, Fmt.Num(load, "0"));
        LoadBrush = new SolidColorBrush(load > 30 ? P.Bad : load > 15 ? P.Warn : P.Good);
    }

    [RelayCommand]
    private void ResetPlan()
    {
        Club.Training.Sessions = TrainingPlan.Default();
        Club.Training.Intensity = TrainingIntensity.Normal;
        Refresh();
    }

    partial void OnCampLengthChanged(decimal value) => UpdateCampCost();

    private void UpdateCampCost() => CampCost = Fmt.Money(GameSession.CampCost(Club, (int)CampLength), UiText.Currency(Club));

    [RelayCommand]
    private void BookCamp()
    {
        var error = Session.BookCamp((CampGoal)CampGoalIndex, Session.World.Date.AddDays((int)CampInDays), (int)CampLength, CampLocation);
        Message = error is null ? T["camp.success"] : T[error];
        Refresh();
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void CancelCamp()
    {
        Session.CancelCamp();
        Message = null;
        Refresh();
    }
}
