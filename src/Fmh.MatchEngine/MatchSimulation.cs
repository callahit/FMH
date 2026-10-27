using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Text-Matchengine nach Kapitel 6: Phasen, Zonen auf dem 5x7-Raster, Duelle mit Modifikatoren.</summary>
public sealed class TextMatchEngine : IMatchEngine
{
    public string Name => "FMH Text-Engine";

    public IMatchSimulation Create(MatchSetup setup) => new MatchSimulation(setup);
}

internal enum Restart { None, KickOff, GoalKick, Corner, FreeKick, Penalty }

internal enum ActionType { Pass, Dribble, Cross, Shot }

/// <remarks>Zustandsmodell: Spielstand, Uhr, Ballbesitz und Ereignisliste; die Teilschritte stehen in den Dateien MatchSimulation.*.cs.</remarks>
internal sealed partial class MatchSimulation : IMatchSimulation
{
    /// <summary>Spielabschnitte: 1 und 2 = Halbzeiten, 3 und 4 = Verlängerung.</summary>
    private static double PeriodSeconds(int period) => period <= 2 ? 45 * 60 : 15 * 60;
    private static int PeriodStartMinute(int period) => period switch { 1 => 0, 2 => 45, 3 => 90, _ => 105 };

    private readonly EngineConfig C;
    private readonly Rng _rng;
    private readonly MatchTeamState[] _teams;
    private readonly Dictionary<MatchPlayer, MatchTeamState> _teamOf = new();
    private readonly List<MatchEvent> _events = new();

    private int _half = 1;
    private int? _penHome, _penAway;
    private double _clock;
    private int _stoppageMinutes = -1;
    private double _stoppageWeight;
    private bool _started, _halfTime, _finished;

    // Ballbesitz und Spielsituation
    private Side _ball = Side.Home;
    private int _row = 2, _col = 2;
    private bool _counter;
    private Restart _restart = Restart.KickOff;
    private int _restartRow, _restartCol;
    private MatchPlayer? _carrier;
    private MatchPlayer? _lastPasser;
    private double _phaseDuration;

    // KI-Steuerung im Spiel
    private readonly int[] _lastAiSubMinute = { 0, 0 };
    private readonly int[] _aiMentalityStage = { 0, 0 };

    public MatchSimulation(MatchSetup setup)
    {
        Setup = setup;
        C = setup.Config;
        _rng = new Rng(setup.Seed);
        _teams = new[] { BuildTeam(Side.Home, setup.Home), BuildTeam(Side.Away, setup.Away) };
    }

    public MatchSetup Setup { get; }
    public MatchTeamState Home => _teams[0];
    public MatchTeamState Away => _teams[1];
    public MatchTeamState Team(Side side) => _teams[(int)side];
    public IReadOnlyList<MatchEvent> Events => _events;
    public bool IsHalfTime => _halfTime;
    public bool IsFinished => _finished;
    public int? PlayerOfTheMatchId { get; private set; }

    public int Minute => Math.Min((int)(PeriodSeconds(_half) / 60), (int)(_clock / 60) + 1) + PeriodStartMinute(_half);

    public int StoppageMinute =>
        _clock < PeriodSeconds(_half) ? 0 : Math.Min(Math.Max(_stoppageMinutes, 1), (int)((_clock - PeriodSeconds(_half)) / 60) + 1);

    public bool IsExtraTime => _half >= 3 || (_halfTime && _half == 2);
    public int? PenaltiesHome => _penHome;
    public int? PenaltiesAway => _penAway;

    // ------------------------------------------------------------------ Aufbau

    private MatchTeamState BuildTeam(Side side, TeamSetup ts)
    {
        var team = new MatchTeamState
        {
            Side = side,
            Club = ts.Club,
            Tactic = ts.Tactic,
            Formation = ts.Formation,
            HumanControlled = ts.HumanControlled,
            Orders = ts.Orders,
            Familiarity = ts.Familiarity,
        };
        for (var i = 0; i < ts.Starting.Count && i < ts.Formation.Slots.Count; i++)
        {
            var mp = NewPlayer(ts.Starting[i], team);
            mp.SlotIndex = i;
            mp.Position = ts.Formation.Slots[i].Position;
            mp.Started = true;
            team.OnPitch.Add(mp);
        }
        foreach (var p in ts.Bench)
        {
            var mp = NewPlayer(p, team);
            mp.SlotIndex = -1;
            mp.Position = p.MainPosition;
            team.Bench.Add(mp);
        }
        return team;
    }

    private MatchPlayer NewPlayer(Player p, MatchTeamState team)
    {
        var mp = new MatchPlayer
        {
            Player = p,
            Condition = p.State.Condition,
            Morale = p.State.Morale,
            DayForm = _rng.Normal(0, 0.3 + (20 - p.Hidden.Consistency) / 20.0 * 1.1),
            RatingScore = _rng.Normal(0, 0.12),
        };
        _teamOf[mp] = team;
        return mp;
    }

    // ------------------------------------------------------------------ Ereignisse

    private MatchEvent Emit(MatchEventType type, Side side, MatchPlayer? player = null, MatchPlayer? other = null,
                            int row = 0, int col = 0, double xg = 0, ShotOutcome? outcome = null, ShotKind? kind = null,
                            int value = 0, bool key = false)
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
        };
        _events.Add(e);
        return e;
    }
}
