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

    // ================================================================== Live

    private void StartTimer()
    {
        _wait = TimeSpan.Zero;
        Paused = false;
        if (!UseTimer || _timer is not null) return;
        _lastTick = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) =>
        {
            var now = DateTime.UtcNow;
            Tick(now - _lastTick);
            _lastTick = now;
        });
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>Spielt so viele Phasen, wie im gewählten Tempo in die vergangene Zeit passen.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (Stage != MatchStage.Live || Paused || _sim is null) return;
        _wait -= elapsed;
        var perStep = TimeSpan.FromSeconds(SecondsPerMatch[Math.Clamp(Profile.MatchSpeed, 1, 5) - 1] / PhasesPerMatch);
        var guard = 0;
        while (_wait <= TimeSpan.Zero && Stage == MatchStage.Live && !Paused && guard++ < 60)
        {
            StepOnce();
            _wait += perStep;
        }
        if (_wait < -perStep) _wait = TimeSpan.Zero;
    }

    private void StepOnce()
    {
        var sim = _sim!;
        sim.Step();
        ProcessEvents();
        if (sim.IsHalfTime)
        {
            Stage = MatchStage.HalfTime;
            Paused = false;
            OnPropertyChanged(nameof(IsTalk));
        }
        else if (sim.IsFinished) Finish();
        UpdateLive();
    }

    private void ProcessEvents()
    {
        var sim = _sim!;
        var ticker = Profile.MatchDisplay == MatchDisplayMode.Ticker;
        for (; _processedEvents < sim.Events.Count; _processedEvents++)
        {
            var e = sim.Events[_processedEvents];
            if (e.Type == MatchEventType.Shot || e.Type == MatchEventType.Goal)
            {
                var list = e.Side == Side.Home ? _xgHome : _xgAway;
                list.Add(new Point(Math.Min(90, e.Minute), list[^1].Y + e.Xg));
            }
            if (e.Type == MatchEventType.Goal) (e.Side == Side.Home ? _goalsHome : _goalsAway).Add(Math.Min(90, e.Minute));

            var marker = e.Type switch
            {
                MatchEventType.Goal => P.Gold,
                MatchEventType.YellowCard => Color.Parse("#F4D03F"),
                MatchEventType.RedCard or MatchEventType.SecondYellow => P.Bad,
                MatchEventType.Injury => P.Bad,
                MatchEventType.Substitution => P.Accent,
                _ => Colors.Transparent,
            };
            if (!string.IsNullOrEmpty(e.Narrative))
            {
                var goal = e.Type == MatchEventType.Goal;
                Ticker.Insert(0, new TickerLine(e.MinuteLabel, e.Narrative,
                    new SolidColorBrush(goal ? P.Gold : e.Key ? P.Text : P.Muted),
                    new SolidColorBrush(marker), goal ? FontWeight.Bold : FontWeight.Normal));
            }
            else if (_commentary!.Describe(e, ticker) is { } line)
            {
                Ticker.Insert(0, new TickerLine(line.Minute, line.Text,
                    new SolidColorBrush(line.Highlight ? P.Gold : e.Key ? P.Text : P.Muted),
                    new SolidColorBrush(marker), line.Highlight ? FontWeight.Bold : FontWeight.Normal));
            }

            var ours = e.Side == _side;
            if (e.Type == MatchEventType.Goal && Profile.PauseOnGoals) PauseWith(T.Format("pause.goal", e.HomeScore, e.AwayScore));
            if (e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow && Profile.PauseOnRedCards) PauseWith(T["pause.red"]);
            if (e.Type == MatchEventType.Injury && ours && e.PlayerId is { } pid) PauseWith(T.Format("pause.injury", Session.World.Players[pid].Name));
        }
    }

    private void PauseWith(string reason)
    {
        Paused = true;
        Status = reason;
    }

    private void UpdateLive()
    {
        var sim = _sim;
        if (sim is null) return;
        Score = $"{sim.Home.Goals} : {sim.Away.Goals}";
        if (sim.PenaltiesHome is { } ph && sim.PenaltiesAway is { } pa) Score += "  " + T.Format("score.penalties", ph, pa);
        else if (sim.IsExtraTime) Score += "  " + T["score.extraTime"];
        Clock = sim.IsFinished ? T["match.final"] : sim.StoppageMinute > 0 ? $"{sim.Minute}+{sim.StoppageMinute}'" : $"{sim.Minute}'";
        var h = sim.Home.Stats;
        var a = sim.Away.Stats;
        var total = h.PossessionSeconds + a.PossessionSeconds;
        var poss = total <= 0 ? 50 : Math.Round(100 * h.PossessionSeconds / total);
        Stats = new List<StatLine>
        {
            new(T["stat.possession"], $"{poss}%", $"{100 - poss}%", poss, 100 - poss),
            new(T["stat.shots"], h.Shots.ToString(), a.Shots.ToString(), h.Shots, a.Shots),
            new(T["stat.onTarget"], h.ShotsOnTarget.ToString(), a.ShotsOnTarget.ToString(), h.ShotsOnTarget, a.ShotsOnTarget),
            new(T["stat.xg"], Fmt.Num(h.Xg, "0.00"), Fmt.Num(a.Xg, "0.00"), h.Xg, a.Xg),
            new(T["stat.passes"], $"{h.Passes} ({h.PassAccuracy}%)", $"{a.Passes} ({a.PassAccuracy}%)", h.Passes, a.Passes),
            new(T["stat.duels"], $"{h.DuelRate}%", $"{a.DuelRate}%", h.DuelRate, a.DuelRate),
            new(T["stat.corners"], h.Corners.ToString(), a.Corners.ToString(), h.Corners, a.Corners),
            new(T["stat.fouls"], h.Fouls.ToString(), a.Fouls.ToString(), h.Fouls, a.Fouls),
            new(T["stat.cards"], $"{h.YellowCards}/{h.RedCards}", $"{a.YellowCards}/{a.RedCards}", h.YellowCards + 3 * h.RedCards, a.YellowCards + 3 * a.RedCards),
        };
        if (sim.IsFinished) Stats = Stats.Append(new StatLine(T["stat.distance"], Fmt.Num(h.DistanceKm, "0.0"), Fmt.Num(a.DistanceKm, "0.0"), h.DistanceKm, a.DistanceKm)).ToList();

        var own = sim.Team(_side);
        Players = own.OnPitch.OrderBy(p => p.SlotIndex).Select(mp => new LivePlayer(
            UiText.Position(mp.Position), mp.Player.LastName, mp.Condition, new SolidColorBrush(P.ConditionColor(mp.Condition)),
            UiText.Grade(mp.Grade), new SolidColorBrush(P.GradeColor(mp.Grade)),
            (mp.Goals > 0 ? new string('●', mp.Goals) + " " : "") + (mp.Yellow ? "▮" : "") + (mp.Injured ? " ✚" : ""))).ToList();
        SubsLeft = T.Format("match.subsLeftValue", sim.MaxSubstitutions - own.SubstitutionsUsed);
        XgSeriesHome = _xgHome.Append(new Point(Math.Min(90, sim.Minute), _xgHome[^1].Y)).ToList();
        XgSeriesAway = _xgAway.Append(new Point(Math.Min(90, sim.Minute), _xgAway[^1].Y)).ToList();
        GoalMarksHome = _goalsHome.ToList();
        GoalMarksAway = _goalsAway.ToList();
        if (!Paused && Stage == MatchStage.Live && own.Shout != Shout.None) Status = T.Format("match.shoutActive", T["shout." + own.Shout]);
        if (_conference) UpdateConference(sim);
    }

    [ObservableProperty] private bool _isConference;
    [ObservableProperty] private IReadOnlyList<ConferenceLine> _conferenceGames = Array.Empty<ConferenceLine>();
    [ObservableProperty] private IReadOnlyList<LiveTableRow> _liveTable = Array.Empty<LiveTableRow>();
    public bool HasLiveTable => LiveTable.Count > 0;
    partial void OnLiveTableChanged(IReadOnlyList<LiveTableRow> value) => OnPropertyChanged(nameof(HasLiveTable));

    /// <summary>Konferenz: Zwischenstände der anderen Partien, Torblitz und Live-Tabelle zur aktuellen Spielminute.</summary>
    private void UpdateConference(IMatchSimulation sim)
    {
        var world = Session.World;
        var minute = sim.IsFinished ? 200 : sim.Minute;
        var others = world.AllFixtures.Where(f => f.CompetitionId == _fixture.CompetitionId && f.Date == _fixture.Date && f != _fixture && f.Result is not null).ToList();
        var partial = new Dictionary<Fixture, (int home, int away)>();
        var lines = new List<ConferenceLine>();
        foreach (var f in others)
        {
            var goals = f.Result!.Goals.Where(g => g.Minute <= minute).ToList();
            var score = (goals.Count(g => g.Home), goals.Count(g => !g.Home));
            partial[f] = score;
            var fresh = goals.Any(g => g.Minute > minute - 3);
            for (var i = 0; i < goals.Count; i++)
            {
                if (!_announced.Add((f.Id, i))) continue;
                var g = goals[i];
                var scorer = world.Players.TryGetValue(g.ScorerId, out var p) ? p.ShortName : "";
                Ticker.Insert(0, new TickerLine($"{g.Minute}'", T.Format("match.conferenceGoal", world.Clubs[f.HomeId].Name,
                        $"{goals.Take(i + 1).Count(x => x.Home)}:{goals.Take(i + 1).Count(x => !x.Home)}", world.Clubs[f.AwayId].Name, scorer),
                    new SolidColorBrush(P.Gold), new SolidColorBrush(P.Gold), FontWeight.SemiBold));
            }
            lines.Add(new ConferenceLine(world.Clubs[f.HomeId].Name, $"{score.Item1} : {score.Item2}", world.Clubs[f.AwayId].Name,
                new SolidColorBrush(fresh ? P.Gold : P.Text), fresh ? FontWeight.Bold : FontWeight.Normal));
        }
        ConferenceGames = lines;
        partial[_fixture] = (sim.Home.Goals, sim.Away.Goals);

        if (_fixture.Kind != CompetitionKind.League || world.FindLeague(_fixture.CompetitionId) is not { } league)
        {
            LiveTable = Array.Empty<LiveTableRow>();
            return;
        }
        var rows = league.ClubIds.ToDictionary(id => id, _ => (pts: 0, gf: 0, ga: 0));
        void Add(int home, int away, int hg, int ag)
        {
            var h = rows[home];
            var a = rows[away];
            rows[home] = (h.pts + (hg > ag ? 3 : hg == ag ? 1 : 0), h.gf + hg, h.ga + ag);
            rows[away] = (a.pts + (ag > hg ? 3 : hg == ag ? 1 : 0), a.gf + ag, a.ga + hg);
        }
        foreach (var f in league.Fixtures.Where(f => f.Result is not null && f.Date < _fixture.Date))
            Add(f.HomeId, f.AwayId, f.Result!.HomeGoals, f.Result.AwayGoals);
        foreach (var (f, score) in partial) Add(f.HomeId, f.AwayId, score.home, score.away);
        var own = Session.HumanClub.Id;
        LiveTable = rows.Select(kv => (id: kv.Key, pts: kv.Value.pts - league.Deductions.GetValueOrDefault(kv.Key), kv.Value.gf, kv.Value.ga))
            .OrderByDescending(r => r.pts).ThenByDescending(r => r.gf - r.ga).ThenByDescending(r => r.gf)
            .Select((r, i) => new LiveTableRow($"{i + 1}.", world.Clubs[r.id].ShortName, $"{r.gf}:{r.ga}", r.pts.ToString(),
                r.id == own ? FontWeight.Bold : FontWeight.Normal,
                new SolidColorBrush(r.id == own ? P.Gold : partial.Keys.Any(f => f.Involves(r.id)) ? P.Text : P.Muted)))
            .ToList();
    }

    [RelayCommand]
    public void TogglePause()
    {
        if (Stage != MatchStage.Live) return;
        Paused = !Paused;
        if (!Paused) Status = "";
        else Status = T["match.paused"];
    }

    [RelayCommand]
    private void SetSpeed(string? value)
    {
        if (int.TryParse(value, out var s)) Speed = s;
    }

    [RelayCommand]
    private void ToggleTicker()
    {
        if (Profile.MatchDisplay == MatchDisplayMode.Scene)
        {
            TickerMode = true;
            return;
        }
        TickerMode = !TickerMode;
        if (!_conference) Profile.MatchDisplay = TickerMode ? MatchDisplayMode.Ticker : MatchDisplayMode.Commentary;
    }

    [RelayCommand]
    public void FinishNow()
    {
        if (_sim is null || _sim.IsFinished) return;
        while (!_sim.IsFinished)
        {
            _sim.Step();
            if (_sim.IsHalfTime) _sim.Step();
        }
        ProcessEvents();
        Finish();
        UpdateLive();
    }

    [RelayCommand]
    public void Substitute()
    {
        if (_sim is null || _sim.IsFinished) return;
        var sim = _sim;
        var team = sim.Team(_side);
        var wasPaused = Paused;
        Paused = true;
        if (team.SubstitutionsUsed >= sim.MaxSubstitutions)
        {
            _main.ShowDialog(new MessageDialogViewModel(T["match.substitution"], new[] { T["sub.noneLeft"] }));
            return;
        }
        var onPitch = team.OnPitch.OrderBy(p => p.SlotIndex).ToList();
        var options = onPitch.Select(p => new ChoiceOption($"{UiText.Position(p.Position)}  {p.Player.Name}",
            $"{p.Condition:0} %  ·  {T["col.grade"]} {UiText.Grade(p.Grade)}{(p.Injured ? "  ✚" : "")}{(p.Yellow ? "  ▮" : "")}")).ToList();
        _main.ShowDialog(new ChoiceDialogViewModel(T["match.subOut"], options, i =>
        {
            var outMp = onPitch[i];
            var bench = team.Bench.ToList();
            if (bench.Count == 0)
            {
                _main.ShowDialog(new MessageDialogViewModel(T["match.substitution"], new[] { T["sub.emptyBench"] }));
                return;
            }
            var benchOptions = bench.Select(b =>
            {
                var rating = Ratings.EffectiveRating(b.Player, outMp.Position, Data.PositionProfiles);
                return new ChoiceOption($"{UiText.Position(b.Player.MainPosition)}  {b.Player.Name}",
                    $"{UiText.Strength(Ratings.StrengthFromRating(rating))} {T["match.asPosition"]} {UiText.Position(outMp.Position)}");
            }).ToList();
            _main.ShowDialog(new ChoiceDialogViewModel(T.Format("match.subIn", outMp.Player.Name), benchOptions, j =>
            {
                if (!sim.Substitute(_side, outMp.Player.Id, bench[j].Player.Id, out var error))
                    _main.ShowDialog(new MessageDialogViewModel(T["match.substitution"], new[] { T[error ?? "sub.failed"] }));
                ProcessEvents();
                UpdateLive();
                Paused = wasPaused;
            }));
        }));
    }

    [RelayCommand]
    public void ChangeTactics()
    {
        if (_sim is null || _sim.IsFinished) return;
        Paused = true;
        _main.ShowDialog(new MatchTacticsViewModel(_sim, _side, () => UpdateLive()));
    }

    [RelayCommand]
    public void ShoutOut()
    {
        if (_sim is null || _sim.IsFinished) return;
        var shouts = Enum.GetValues<Shout>();
        var options = shouts.Select(s => new ChoiceOption(T["shout." + s], T["shoutHint." + s])).ToList();
        _main.ShowDialog(new ChoiceDialogViewModel(T["match.shout"], options, i =>
        {
            _sim.SetShout(_side, shouts[i]);
            UpdateLive();
        }, (int)_sim.Team(_side).Shout));
    }

    // ================================================================== Nachbericht

    private void Finish()
    {
        StopTimer();
        var sim = _sim!;
        ProcessEvents();
        Session.FinishMatch(_fixture, sim);
        Session.PlayOtherFixturesToday();

        GradeLine Grade(MatchPlayer mp)
        {
            var notes = new List<string>();
            if (mp.Goals > 0) notes.Add(T.Format("match.goalsShort", mp.Goals));
            if (mp.Assists > 0) notes.Add(T.Format("match.assistsShort", mp.Assists));
            if (mp.Red) notes.Add(T["match.redShort"]);
            else if (mp.Yellow) notes.Add(T["match.yellowShort"]);
            if (mp.Injured) notes.Add("✚");
            var name = mp.Started ? mp.Player.Name : $"↑ {mp.MinuteOn}'  {mp.Player.Name}";
            return new GradeLine(UiText.Position(mp.Position), name, UiText.Grade(mp.Grade), new SolidColorBrush(P.GradeColor(mp.Grade)), string.Join(" ", notes));
        }
        IEnumerable<MatchPlayer> Order(MatchTeamState t) => t.Participants.OrderBy(p => p.Started ? 0 : 1).ThenBy(p => p.MinuteOn).ThenBy(p => p.SlotIndex);
        HomeGrades = Order(sim.Home).Select(Grade).ToList();
        AwayGrades = Order(sim.Away).Select(Grade).ToList();
        var result = _fixture.Result!;
        if (sim.PlayerOfTheMatchId is { } potm && Session.World.Players.TryGetValue(potm, out var star))
            PlayerOfTheMatch = T.Format("match.potm", star.Name, Session.World.Clubs[star.ClubId ?? Home.Id].Name);
        var referee = Session.World.Referees.FirstOrDefault(r => r.Id == _fixture.RefereeId);
        PostInfo = T.Format("match.postInfo", Fmt.Int(result.Attendance), T["weather." + result.Weather], referee?.Name ?? "–")
                   + " · " + T.Format("match.halfTimeScore", result.HalfTimeHome, result.HalfTimeAway);
        if (result.NoteKeys.Count > 0)
            PostInfo += " · " + string.Join(" · ", result.NoteKeys.Select(k => T[k]));
        Heat = sim.Team(_side).Stats.Heat.ToList();

        var league = Session.World.LeagueOf(Session.HumanClub);
        OtherResults = Session.World.AllFixtures.Where(f => f.CompetitionId == _fixture.CompetitionId && f.Date == _fixture.Date).Select(f => new ResultLine(
            Session.World.Clubs[f.HomeId].Name, f.Result is { } r ? UiText.Score(r) : "– : –",
            Session.World.Clubs[f.AwayId].Name, f.Involves(Session.HumanClub.Id) ? FontWeight.Bold : FontWeight.Normal)).ToList();
        var row = LeagueTable.Compute(league).First(r => r.ClubId == Session.HumanClub.Id);
        TableInfo = T.Format("match.ownTable", row.Position, row.Points, row.GoalsFor, row.GoalsAgainst);
        Stage = MatchStage.PostMatch;
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

/// <summary>Taktikänderung während des Spiels: Formation, Mentalität und Mannschaftsanweisungen.</summary>
public sealed partial class MatchTacticsViewModel : ViewModelBase
{
    private readonly IMatchSimulation _sim;
    private readonly Side _side;
    private readonly Tactic _draft;
    private readonly Action _onApply;

    public MatchTacticsViewModel(IMatchSimulation sim, Side side, Action onApply)
    {
        _sim = sim;
        _side = side;
        _onApply = onApply;
        _draft = sim.Team(side).Tactic.Clone();
        var t = _draft;
        var ins = t.Instructions;
        string[] Names<TEnum>(string prefix) where TEnum : struct, Enum => Enum.GetValues<TEnum>().Select(v => T[prefix + v]).ToArray();
        int Idx<TEnum>(TEnum v) where TEnum : struct, Enum => Array.IndexOf(Enum.GetValues<TEnum>(), v);
        TEnum At<TEnum>(int i) where TEnum : struct, Enum => Enum.GetValues<TEnum>()[i];
        var yesNo = new[] { T["ui.no"], T["ui.yes"] };
        Settings = new List<ChoiceSetting>
        {
            new(T["tactic.formation"], Data.Formations.Select(f => f.Name).ToList(), Data.Formations.FindIndex(f => f.Id == t.FormationId), i => t.FormationId = Data.Formations[i].Id),
            new(T["tactic.mentality"], Names<Mentality>("mentality."), Idx(t.Mentality), i => t.Mentality = At<Mentality>(i)),
            new(T["tactic.passing"], Names<PassingLength>("passing."), Idx(ins.PassingLength), i => ins.PassingLength = At<PassingLength>(i)),
            new(T["tactic.tempo"], Names<Tempo>("tempo."), Idx(ins.Tempo), i => ins.Tempo = At<Tempo>(i)),
            new(T["tactic.width"], Names<Width>("width."), Idx(ins.Width), i => ins.Width = At<Width>(i)),
            new(T["tactic.counter"], yesNo, ins.Counter ? 1 : 0, i => ins.Counter = i == 1),
            new(T["tactic.pressing"], Names<PressingHeight>("pressing."), Idx(ins.Pressing), i => ins.Pressing = At<PressingHeight>(i)),
            new(T["tactic.line"], Names<DefensiveLine>("line."), Idx(ins.Line), i => ins.Line = At<DefensiveLine>(i)),
            new(T["tactic.tackling"], Names<TacklingStyle>("tackling."), Idx(ins.Tackling), i => ins.Tackling = At<TacklingStyle>(i)),
        };
    }

    public IReadOnlyList<ChoiceSetting> Settings { get; }

    [RelayCommand]
    private void Apply()
    {
        var team = _sim.Team(_side);
        if (_draft.FormationId != team.Tactic.FormationId) _sim.ChangeFormation(_side, Data.Formation(_draft.FormationId));
        if (_draft.Mentality != team.Tactic.Mentality) _sim.ChangeMentality(_side, _draft.Mentality);
        _sim.ChangeInstructions(_side, _draft.Instructions);
        MainWindowViewModel.Instance?.CloseDialog();
        _onApply();
    }

    [RelayCommand]
    private void Cancel() => MainWindowViewModel.Instance?.CloseDialog();
}
