using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;
using Fmh.MatchEngine;

namespace Fmh.Desktop.ViewModels;

public enum MatchStage { PreMatch, PreTalk, Live, HalfTime, PostMatch }

/// <summary>Ein anderes Spiel in der Konferenz: Spielstand bis zur aktuellen Minute.</summary>
public sealed record ConferenceLine(string Home, string Score, string Away, IBrush Brush, FontWeight Weight);

public sealed record LiveTableRow(string Position, string Club, string Goals, string Points, FontWeight Weight, IBrush Brush);

public sealed record TickerLine(string Minute, string Text, IBrush Brush, IBrush Marker, FontWeight Weight);
public sealed record StatLine(string Label, string Home, string Away, double HomeValue, double AwayValue);
public sealed record LivePlayer(string Position, string Name, double Condition, IBrush ConditionBrush, string Grade, IBrush GradeBrush, string Marks);
public sealed record GradeLine(string Position, string Name, string Grade, IBrush GradeBrush, string Notes);
public sealed record ToneOption(Tone? Tone, string Label, string Hint, int Number);
public sealed record ResultLine(string Home, string Score, string Away, FontWeight Weight);

/// <summary>
/// Ablauf eines Spieltags (Kapitel 6): Vorbericht, Kabinenansprache, Live-Spiel als Kommentar oder Ticker,
/// Halbzeit, Nachbericht. Die Simulation läuft Phase für Phase; das Tempo bestimmt die Wartezeit.
/// </summary>
public sealed partial class MatchViewModel : ViewModelBase
{
    /// <summary>Spieldauer je Tempostufe in Sekunden: "Echtzeit-Feeling" (ca. 8 Min.) bis "Blitz" (ca. 20 Sek.).</summary>
    private static readonly double[] SecondsPerMatch = { 480, 240, 120, 60, 20 };
    private const double PhasesPerMatch = 230;

    /// <summary>In Tests wird die Zeit manuell über <see cref="Tick"/> vorgespult.</summary>
    public static bool UseTimer { get; set; } = true;

    private readonly MainWindowViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly Fixture _fixture;
    private readonly Side _side;
    private DispatcherTimer? _timer;
    private DateTime _lastTick;
    private TimeSpan _wait;
    private IMatchSimulation? _sim;
    private CommentaryGenerator? _commentary;
    private int _processedEvents;
    private bool _halfTimeTalkDone;
    private readonly List<Point> _xgHome = new() { new Point(0, 0) };
    private readonly List<Point> _xgAway = new() { new Point(0, 0) };
    private readonly List<double> _goalsHome = new();
    private readonly List<double> _goalsAway = new();
    private bool _conference;
    private readonly HashSet<(int fixture, int goal)> _announced = new();

    public MatchViewModel(MainWindowViewModel main, ShellViewModel shell, Fixture fixture)
    {
        _main = main;
        _shell = shell;
        _fixture = fixture;
        _side = fixture.HomeId == Session.HumanClub.Id ? Side.Home : Side.Away;
        Home = Session.World.Clubs[fixture.HomeId];
        Away = Session.World.Clubs[fixture.AwayId];
        Tones = new[] { Tone.Calm, Tone.Demanding, Tone.Praising, Tone.Angry, Tone.Disappointed, Tone.Motivating }
            .Select((t, i) => new ToneOption(t, T["tone." + t], T["toneHint." + t], i + 1))
            .Append(new ToneOption(null, T["tone.none"], "", 7))
            .ToList();
        BuildPreview();
    }

    public Club Home { get; }
    public Club Away { get; }
    public string HomeInitials => Crest.InitialsOf(Home.Name);
    public string AwayInitials => Crest.InitialsOf(Away.Name);
    public IReadOnlyList<ToneOption> Tones { get; }
    public ObservableCollection<TickerLine> Ticker { get; } = new();
    private ManagerProfile Profile => Session.HumanManager.Profile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreMatch), nameof(IsTalk), nameof(IsLiveOrHalf), nameof(IsPostMatch), nameof(TalkTitle), nameof(IsHalfTime))]
    private MatchStage _stage = MatchStage.PreMatch;

    public bool IsPreMatch => Stage == MatchStage.PreMatch;
    public bool IsTalk => Stage == MatchStage.PreTalk || (Stage == MatchStage.HalfTime && !_halfTimeTalkDone);
    public bool IsHalfTime => Stage == MatchStage.HalfTime;
    public bool IsLiveOrHalf => Stage is MatchStage.Live or MatchStage.HalfTime or MatchStage.PreTalk;
    public bool IsPostMatch => Stage == MatchStage.PostMatch;
    public string TalkTitle => Stage == MatchStage.HalfTime ? (_sim is { Minute: >= 90 } ? T["match.beforeExtraTime"] : T["match.halfTime"]) : T["match.preTalk"];

    [ObservableProperty] private string _score = "– : –";
    [ObservableProperty] private string _clock = "";
    [ObservableProperty] private string _subline = "";
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private IReadOnlyList<StatLine> _stats = Array.Empty<StatLine>();
    [ObservableProperty] private IReadOnlyList<LivePlayer> _players = Array.Empty<LivePlayer>();
    [ObservableProperty] private IReadOnlyList<Point> _xgSeriesHome = Array.Empty<Point>();
    [ObservableProperty] private IReadOnlyList<Point> _xgSeriesAway = Array.Empty<Point>();
    [ObservableProperty] private IReadOnlyList<double> _goalMarksHome = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _goalMarksAway = Array.Empty<double>();
    [ObservableProperty] private string _subsLeft = "";
    [ObservableProperty] private int _speed;
    [ObservableProperty] private bool _tickerMode;

    // Vorbericht
    [ObservableProperty] private IReadOnlyList<InfoLine> _preview = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<AdviceVm> _checks = Array.Empty<AdviceVm>();
    [ObservableProperty] private IReadOnlyList<string> _displayModes = Array.Empty<string>();
    [ObservableProperty] private int _displayModeIndex;
    [ObservableProperty] private PageViewModel? _editor;

    // Nachbericht
    [ObservableProperty] private IReadOnlyList<GradeLine> _homeGrades = Array.Empty<GradeLine>();
    [ObservableProperty] private IReadOnlyList<GradeLine> _awayGrades = Array.Empty<GradeLine>();
    [ObservableProperty] private string _playerOfTheMatch = "";
    [ObservableProperty] private string _postInfo = "";
    [ObservableProperty] private IReadOnlyList<int> _heat = Array.Empty<int>();
    [ObservableProperty] private IReadOnlyList<ResultLine> _otherResults = Array.Empty<ResultLine>();
    [ObservableProperty] private string _tableInfo = "";

    public IBrush HomeBrush => new SolidColorBrush(Palette.ClubColor(Home.PrimaryColor) is var c && c.R + c.G + c.B < 90 ? Palette.Current.Accent : c);
    public IBrush AwayBrush => new SolidColorBrush(Palette.ClubColor(Away.PrimaryColor) is var c && (c.R + c.G + c.B < 90 || c == Palette.ClubColor(Home.PrimaryColor)) ? Palette.Current.Gold : c);

    // ================================================================== Vorbericht

    /// <summary>"Liga, 5. Spieltag" bzw. "Pokal, Achtelfinale" / "Relegation, Hinspiel".</summary>
    private string CompetitionLabel()
    {
        var world = Session.World;
        if (_fixture.Kind == CompetitionKind.League)
            return T.Format("match.matchdayLabel", UiText.Competition(_fixture.CompetitionId), _fixture.Matchday);
        // Testspiel bzw. Sommerturnier (AP 9): kein Pokal, keine Hin- und Rückspiele.
        if (_fixture.Kind == CompetitionKind.Friendly)
            return world.SummerTournaments.FirstOrDefault(t => world.Friendlies.Any(x => x.Fixture.Id == _fixture.Id && x.TournamentId == t.Id)) is { } summer
                ? summer.Name
                : T["competition.FRIENDLY"];
        var cup = world.FindCup(_fixture.CompetitionId);
        var round = cup?.Rounds.FirstOrDefault(r => r.Number == _fixture.Matchday)?.Name ?? "";
        var leg = _fixture.Leg switch { 1 => " · " + T["cup.firstLeg"], 2 => " · " + T["cup.secondLeg"], _ => "" };
        // Rückspiel: Ergebnis des Hinspiels anzeigen.
        if (_fixture.Leg == 2 && cup?.Fixtures.FirstOrDefault(f => f.TieId == _fixture.TieId && f.Leg == 1) is { Result: { } first })
            leg += " · " + T.Format("cup.firstLegResult", first.HomeGoals, first.AwayGoals);
        return $"{cup?.Name}, {round}{leg}";
    }

    private void BuildPreview()
    {
        var world = Session.World;
        var own = Session.HumanClub;
        var opp = _side == Side.Home ? Away : Home;
        var ownRow = LeagueTable.Compute(world.LeagueOf(own)).First(r => r.ClubId == own.Id);
        var oppLeague = world.FindLeague(opp.LeagueId);
        var oppRow = oppLeague is null ? null : LeagueTable.Compute(oppLeague).First(r => r.ClubId == opp.Id);
        var referee = world.Referees.FirstOrDefault(r => r.Id == _fixture.RefereeId);
        var weather = Session.WeatherFor(_fixture);
        var profiles = Data.PositionProfiles;
        Subline = $"{CompetitionLabel()} · {Home.StadiumName} · {T["weather." + weather]}";
        Clock = Fmt.Date(_fixture.Date);

        var keyPlayers = world.SquadOf(opp).Where(p => p.IsAvailable)
            .OrderByDescending(p => Ratings.Strength(p, profiles)).Take(3)
            .Select(p => $"{p.Name} ({UiText.Position(p.MainPosition)}, {UiText.Strength(Ratings.Strength(p, profiles))})");
        Preview = new List<InfoLine>
        {
            new(T["match.opponent"], $"{opp.Name} ({(_side == Side.Home ? T["match.atHome"] : T["match.away"])})"),
            new(T["match.tablePosition"], oppRow is null
                ? T.Format("match.positionsOther", ownRow.Position, ownRow.Points, UiText.Competition(opp.LeagueId))
                : oppLeague == world.LeagueOf(own)
                    ? T.Format("match.positions", ownRow.Position, ownRow.Points, oppRow.Position, oppRow.Points)
                    : T.Format("match.positionsLeague", ownRow.Position, ownRow.Points, oppRow.Position, UiText.Competition(oppLeague!.Id))),
            new(T["match.oppForm"], oppRow is null || oppRow.Form.Count == 0 ? "–" : string.Join(" ", oppRow.Form.Select(f => T["form." + f]))),
            new(T["match.oppFormation"], Data.Formation(opp.Tactic.FormationId).Name),
            new(T["match.keyPlayers"], string.Join(", ", keyPlayers)),
            new(T["match.strength"], T.Format("match.strengthValue",
                UiText.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(own, world, Data))),
                UiText.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(opp, world, Data))))),
            new(T["match.venue"], $"{Home.StadiumName} ({Fmt.Int(Home.StadiumCapacity)} {T["unit.seats"]})"),
            new(T["match.weather"], T["weather." + weather]),
            new(T["match.referee"], referee is null ? "–" : T.Format("match.refereeValue", referee.Name, referee.Strictness)),
        };

        var checks = LineupSelector.Validate(own, world, Data, AiTactics.ReserveEligibility(own, world, Data))
            .Select(p => new AdviceVm(T[p], new SolidColorBrush(P.Bad))).ToList();
        if (checks.Count > 0) checks.Add(new AdviceVm(T["match.autoFixHint"], new SolidColorBrush(P.Warn)));
        checks.AddRange(TacticAdvisor.Analyse(own, world, Data).Select(a =>
            new AdviceVm(T.Format(a.Key, a.Args.Select(x => x is Position pos ? (object)UiText.Position(pos) : x).ToArray()),
                new SolidColorBrush(a.Warning ? P.Warn : P.Good))));
        Checks = checks;
        DisplayModes = Enum.GetValues<MatchDisplayMode>().Select(m => T["matchDisplay." + m]).ToList();
        DisplayModeIndex = (int)Profile.MatchDisplay;
        Speed = Profile.MatchSpeed;
    }

    partial void OnDisplayModeIndexChanged(int value)
    {
        if (value >= 0) Profile.MatchDisplay = (MatchDisplayMode)value;
    }

    partial void OnSpeedChanged(int value) => Profile.MatchSpeed = Math.Clamp(value, 1, 5);

    [RelayCommand]
    private void EditTactics()
    {
        Editor = Editor is null ? new TacticsPageViewModel(_shell) : null;
        if (Editor is null) BuildPreview();
    }

    [RelayCommand]
    private void RepairLineup()
    {
        LineupSelector.Repair(Session.HumanClub, Session.World, Data, Session.BenchSize(Session.HumanClub));
        BuildPreview();
    }

    [RelayCommand]
    public void Kickoff()
    {
        Editor = null;
        var instant = Profile.MatchDisplay == MatchDisplayMode.Instant;
        var scene = Profile.MatchDisplay == MatchDisplayMode.Scene;
        _sim = Session.StartMatch(_fixture, humanControlled: !instant, sceneText: scene && !instant);
        _commentary = new CommentaryGenerator(Data, _sim);
        TickerMode = scene || Profile.MatchDisplay is MatchDisplayMode.Ticker or MatchDisplayMode.Conference;
        _conference = Profile.MatchDisplay == MatchDisplayMode.Conference;
        // Konferenz: die übrigen Spiele werden vorab berechnet und minutengenau aufgedeckt.
        if (_conference) Session.PlayOtherFixturesToday();
        IsConference = _conference;
        if (instant)
        {
            _sim.RunToEnd();
            Finish();
            return;
        }
        Stage = MatchStage.PreTalk;
        UpdateLive();
    }

    // ================================================================== Ansprache

    [RelayCommand]
    public void Talk(ToneOption? option)
    {
        if (option is null || _sim is null) return;
        if (option.Tone is { } tone)
        {
            var reactions = _sim.TeamTalk(_side, tone);
            Status = T.Format("match.talkResult", reactions.Count(r => r.MoraleDelta > 0), reactions.Count(r => r.MoraleDelta < 0));
        }
        else Status = T["match.noTalk"];

        if (Stage == MatchStage.PreTalk)
        {
            Stage = MatchStage.Live;
            StartTimer();
        }
        else
        {
            _halfTimeTalkDone = true;
            OnPropertyChanged(nameof(IsTalk));
        }
    }

    [RelayCommand]
    public void SecondHalf()
    {
        if (Stage != MatchStage.HalfTime) return;
        _halfTimeTalkDone = true;
        Stage = MatchStage.Live;
        StepOnce();
        StartTimer();
    }


    [RelayCommand]
    public void Close()
    {
        StopTimer();
        _shell.ReturnFromMatch();
    }

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        switch (Stage)
        {
            case MatchStage.PreMatch:
                if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space && Editor is null) { Kickoff(); return true; }
                if (key == Avalonia.Input.Key.Escape && Editor is not null) { EditTactics(); return true; }
                return Editor?.HandleKey(key, modifiers) ?? false;
            case MatchStage.PreTalk:
            case MatchStage.HalfTime:
                if (IsTalk && key >= Avalonia.Input.Key.D1 && key <= Avalonia.Input.Key.D7) { Talk(Tones[key - Avalonia.Input.Key.D1]); return true; }
                if (Stage == MatchStage.HalfTime && key == Avalonia.Input.Key.Space) { SecondHalf(); return true; }
                if (Stage == MatchStage.HalfTime && key == Avalonia.Input.Key.W) { Substitute(); return true; }
                return false;
            case MatchStage.Live:
                switch (key)
                {
                    case Avalonia.Input.Key.Space or Avalonia.Input.Key.P: TogglePause(); return true;
                    case Avalonia.Input.Key.W: Substitute(); return true;
                    case Avalonia.Input.Key.T: ChangeTactics(); return true;
                    case Avalonia.Input.Key.Z: ShoutOut(); return true;
                    case Avalonia.Input.Key.M: ToggleTicker(); return true;
                    case Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add: Speed = Math.Min(5, Speed + 1); return true;
                    case Avalonia.Input.Key.OemMinus or Avalonia.Input.Key.Subtract: Speed = Math.Max(1, Speed - 1); return true;
                }
                return false;
            case MatchStage.PostMatch:
                if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space or Avalonia.Input.Key.Escape) { Close(); return true; }
                return false;
        }
        return false;
    }
}
