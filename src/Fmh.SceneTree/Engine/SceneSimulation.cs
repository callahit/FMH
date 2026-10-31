using Fmh.Core;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

/// <summary>
/// Ein Spiel als Gang durch den Szenenbaum. Ein Schritt ist eine Textzeile.
/// Offene Szenen wählen die Startkategorie; Ecken, Freistöße, Karten und Verletzungen hängen als Folgeszene daran.
/// </summary>
public sealed partial class SceneSimulation : IMatchSimulation
{
    private enum CardPending { None, Yellow, Second, Red }

    private readonly SceneTreeAsset _tree;
    private readonly SceneWeightOptions _opt;
    private readonly Rng _rng;
    private readonly MatchTeamState[] _teams;
    private readonly Dictionary<MatchPlayer, MatchTeamState> _teamOf = new();
    private readonly List<MatchEvent> _events = new();
    private readonly Queue<int> _follow = new();
    private readonly int[] _lastAiSub = { 0, 0 };

    private int _period = 1;
    private double _clock;
    private int _stoppage = -1;
    private bool _started, _halfTime, _finished, _shootout, _incidentDriven;
    private int _parent = -1;
    private int _steps, _followChain, _soHome, _soAway;
    private Side _attack = Side.Home;
    private Side _soSide = Side.Home;
    private double _sceneMinutes = 4;
    private bool _kickScored;
    private CardPending _pendingCard;
    private MatchPlayer? _cardPlayer, _victim, _injuryPlayer, _lastAttacker;
    private double _extraStoppage;
    private readonly List<PlannedIncident> _plan = new();
    private readonly List<string> _rareTags = new();
    private byte _reason;
    private string _injuryLabel = "Verletzung";
    private int? _penHome, _penAway;

    public SceneSimulation(MatchSetup setup, SceneTreeAsset tree, SceneWeightOptions options)
    {
        Setup = setup;
        _tree = tree;
        _opt = options;
        _rng = new Rng(setup.Seed);
        _teams = new[] { Build(Side.Home, setup.Home), Build(Side.Away, setup.Away) };
    }

    public MatchSetup Setup { get; }
    public MatchTeamState Home => _teams[0];
    public MatchTeamState Away => _teams[1];
    public MatchTeamState Team(Side side) => _teams[(int)side];
    public IReadOnlyList<MatchEvent> Events => _events;
    public bool IsHalfTime => _halfTime;
    public bool IsFinished => _finished;
    public bool IsExtraTime => _period >= 3 || (_halfTime && _period == 2);
    public int? PenaltiesHome => _penHome;
    public int? PenaltiesAway => _penAway;
    public int? PlayerOfTheMatchId { get; private set; }
    /// <summary>Schlüssel der sonstigen Texte, die in dieser Partie vorkamen.</summary>
    public IReadOnlyList<string> RareTags => _rareTags;
    public int MaxSubstitutions => Setup.Substitutions + (_period >= 3 || (_halfTime && _period == 2) ? 1 : 0);

    private double PeriodSeconds => _period <= 2 ? 45 * 60 : 15 * 60;
    private int PeriodLengthMin => _period <= 2 ? 45 : 15;
    private int PeriodStart => _period switch { 1 => 0, 2 => 45, 3 => 90, _ => 105 };

    public int Minute => Math.Min(PeriodLengthMin, (int)(_clock / 60) + 1) + PeriodStart;

    public int StoppageMinute =>
        _clock < PeriodSeconds ? 0 : Math.Min(Math.Max(_stoppage, 1), (int)((_clock - PeriodSeconds) / 60) + 1);

    public IReadOnlyList<MatchEvent> Step()
    {
        if (_finished) return Array.Empty<MatchEvent>();
        var start = _events.Count;
        if (++_steps > 8000) Finish();
        else if (!_started) Kickoff();
        else if (_halfTime) Resume();
        else Play();
        if (!_finished) UpdateGrades();
        return _events.GetRange(start, _events.Count - start);
    }

    public void RunToEnd()
    {
        while (!_finished) Step();
    }

    private MatchTeamState Build(Side side, TeamSetup source)
    {
        var team = new MatchTeamState
        {
            Side = side,
            Club = source.Club,
            Tactic = source.Tactic,
            Formation = source.Formation,
            HumanControlled = source.HumanControlled,
            Orders = source.Orders,
            Familiarity = source.Familiarity,
        };
        for (var i = 0; i < source.Starting.Count && i < source.Formation.Slots.Count; i++)
        {
            var mp = NewPlayer(source.Starting[i]);
            mp.SlotIndex = i;
            mp.Position = source.Formation.Slots[i].Position;
            mp.Started = true;
            team.OnPitch.Add(mp);
        }
        foreach (var p in source.Bench)
        {
            var mp = NewPlayer(p);
            mp.SlotIndex = -1;
            mp.Position = p.MainPosition;
            team.Bench.Add(mp);
        }
        foreach (var mp in team.OnPitch.Concat(team.Bench)) _teamOf[mp] = team;
        return team;
    }

    private MatchPlayer NewPlayer(Player player) => new()
    {
        Player = player,
        Condition = player.State.Condition,
        Morale = player.State.Morale,
        DayForm = _rng.Normal(0, 0.3 + (20 - player.Hidden.Consistency) / 20.0 * 1.1),
        RatingScore = _rng.Normal(0, 0.12),
    };

    private void Kickoff()
    {
        _started = true;
        _attack = Side.Home;
        Emit(MatchEventType.KickOff, Side.Home, key: true);
        PlanPeriod();
    }

    private void Resume()
    {
        _halfTime = false;
        _period++;
        _clock = 0;
        _stoppage = -1;
        _parent = -1;
        _attack = _period % 2 == 0 ? Side.Away : Side.Home;
        Emit(_period == 2 ? MatchEventType.SecondHalfKickOff : MatchEventType.ExtraTimeStart, _attack, key: true);
        PlanPeriod();
    }

    private void Advance(double minutes)
    {
        _clock += minutes * 60;
        Team(_attack).Stats.PossessionSeconds += minutes * 60;
        if (_stoppage >= 0 || _clock < PeriodSeconds) return;
        var max = _period switch { 1 => 4, 2 => 6, 3 => 2, _ => 3 };
        var min = _period >= 3 ? 0 : 1;
        _stoppage = (int)Math.Clamp(Math.Round(0.6 + _extraStoppage + _rng.Range(0, 1.4)), min, Math.Max(max, Math.Min(12, max + _extraStoppage)));
        if (_stoppage > 0) Emit(MatchEventType.Stoppage, _attack, value: _stoppage, key: true);
    }

    private bool ClockDone() => _stoppage < 0 ? _clock >= PeriodSeconds : _clock >= PeriodSeconds + _stoppage * 60;

    private bool Level()
    {
        var k = Setup.Knockout;
        if (k is null) return Home.Goals == Away.Goals;
        var home = Home.Goals + k.FirstLegHome;
        var away = Away.Goals + k.FirstLegAway;
        if (home != away) return false;
        if (k.AwayGoals && k.TwoLegged && k.FirstLegHome != Away.Goals) return false;
        return true;
    }

    private void EndPeriod()
    {
        _parent = -1;
        switch (_period)
        {
            case 1:
                _halfTime = true;
                Emit(MatchEventType.HalfTime, _attack, key: true);
                break;
            case 2 when Setup.Knockout is not null && Level():
                _halfTime = true;
                Emit(MatchEventType.EndOfRegularTime, _attack, key: true);
                break;
            case 3:
                _period = 4;
                _clock = 0;
                _stoppage = -1;
                Emit(MatchEventType.ExtraTimeSecondHalf, _attack);
                break;
            case 4 when Setup.Knockout is not null && Level():
                _shootout = true;
                _penHome = 0;
                _penAway = 0;
                _soHome = _soAway = 0;
                _soSide = Side.Home;
                _parent = -1;
                Emit(MatchEventType.ShootoutStart, Side.Home, key: true);
                break;
            default:
                Finish();
                break;
        }
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        var diff = Home.Goals - Away.Goals;
        foreach (var team in _teams)
        {
            var own = team.Side == Side.Home ? diff : -diff;
            var conceded = team.Side == Side.Home ? Away.Goals : Home.Goals;
            foreach (var mp in team.Participants)
            {
                if (own > 0) mp.RatingScore += 0.25;
                if (own < 0) mp.RatingScore -= 0.25;
                var minutes = mp.MinutesPlayed(Minute);
                if (conceded == 0 && minutes >= 60 && (mp.Position == Position.GK || PositionInfo.IsDefender(mp.Position)))
                    mp.RatingScore += 0.3;
                var baseKm = mp.Position == Position.GK
                    ? 5.2
                    : 9.0 + mp.Player.Attributes[Attr.WorkRate] / 20.0 * 2.4 + mp.Player.Attributes[Attr.Stamina] / 20.0 * 0.9;
                mp.DistanceKm = Math.Round(baseKm * minutes / 90.0 * _rng.Range(0.95, 1.05), 1);
                team.Stats.DistanceKm += mp.DistanceKm;
            }
            team.Stats.DistanceKm = Math.Round(team.Stats.DistanceKm, 1);
        }
        Emit(MatchEventType.FullTime, _attack, key: true);
        UpdateGrades();
        PlayerOfTheMatchId = _teams.SelectMany(t => t.Participants)
            .Where(p => p.Grade.HasValue)
            .OrderBy(p => p.Grade).ThenByDescending(p => p.Goals).ThenByDescending(p => p.RatingScore)
            .FirstOrDefault()?.Player.Id;
    }

    private void UpdateGrades()
    {
        var minute = Minute;
        foreach (var mp in _teams.SelectMany(t => t.Participants))
        {
            var raw = Setup.Config.GradeBase - mp.RatingScore * Setup.Config.GradeScale;
            var grade = Math.Clamp(Math.Round(raw * 2) / 2, 1.0, 6.0);
            mp.Grade = _finished && mp.MinutesPlayed(minute) < Setup.Config.GradeMinMinutes ? null : grade;
        }
    }

    private MatchEvent Emit(MatchEventType type, Side side, MatchPlayer? player = null, MatchPlayer? other = null,
        double xg = 0, ShotOutcome? outcome = null, ShotKind? kind = null, int value = 0, bool key = false,
        string? narrative = null, int row = 0, int col = 0)
    {
        var e = new MatchEvent
        {
            Index = _events.Count,
            Minute = Minute,
            Stoppage = StoppageMinute,
            Type = type,
            Side = side,
            PlayerId = player?.Player.Id,
            OtherPlayerId = other?.Player.Id,
            Row = row,
            Col = col,
            Xg = Math.Round(xg, 3),
            Outcome = outcome,
            Kind = kind,
            HomeScore = Home.Goals,
            AwayScore = Away.Goals,
            Value = value,
            Key = key,
            Narrative = narrative,
        };
        _events.Add(e);
        return e;
    }
}
