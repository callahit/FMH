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

internal sealed class MatchSimulation : IMatchSimulation
{
    private const double HalfSeconds = 45 * 60;

    private readonly EngineConfig C;
    private readonly Rng _rng;
    private readonly MatchTeamState[] _teams;
    private readonly Dictionary<MatchPlayer, MatchTeamState> _teamOf = new();
    private readonly List<MatchEvent> _events = new();

    private int _half = 1;
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

    public int Minute => Math.Min(45, (int)(_clock / 60) + 1) + (_half == 2 ? 45 : 0);

    public int StoppageMinute =>
        _clock < HalfSeconds ? 0 : Math.Min(Math.Max(_stoppageMinutes, 1), (int)((_clock - HalfSeconds) / 60) + 1);

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

    // ------------------------------------------------------------------ Ablauf

    public IReadOnlyList<MatchEvent> Step()
    {
        if (_finished) return Array.Empty<MatchEvent>();
        var start = _events.Count;

        if (!_started)
        {
            _started = true;
            _ball = Side.Home;
            _restart = Restart.KickOff;
            Emit(MatchEventType.KickOff, Side.Home, key: true);
        }
        else if (_halfTime)
        {
            _halfTime = false;
            _half = 2;
            _clock = 0;
            _stoppageMinutes = -1;
            _stoppageWeight = 0;
            _ball = Side.Away;
            _restart = Restart.KickOff;
            _counter = false;
            Emit(MatchEventType.SecondHalfKickOff, Side.Away, key: true);
        }
        else
        {
            SimulatePhase();
            AiManage(Side.Home);
            AiManage(Side.Away);
            CheckClock();
        }

        UpdateGrades();
        return _events.GetRange(start, _events.Count - start);
    }

    public void RunToEnd()
    {
        while (!_finished) Step();
    }

    private void CheckClock()
    {
        if (_clock >= HalfSeconds && _stoppageMinutes < 0)
        {
            var max = _half == 1 ? 4 : 6;
            _stoppageMinutes = (int)Math.Clamp(Math.Round(0.6 + _stoppageWeight + _rng.Range(0, 1.4)), 1, max);
            Emit(MatchEventType.Stoppage, _ball, value: _stoppageMinutes, key: true);
        }
        if (_stoppageMinutes < 0 || _clock < HalfSeconds + _stoppageMinutes * 60) return;

        if (_half == 1)
        {
            _halfTime = true;
            Emit(MatchEventType.HalfTime, _ball, key: true);
        }
        else
        {
            Finish();
        }
    }

    private void Finish()
    {
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
                var minutes = mp.MinutesPlayed(90);
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
        Emit(MatchEventType.FullTime, _ball, key: true);
        UpdateGrades();

        PlayerOfTheMatchId = _teams.SelectMany(t => t.Participants)
            .Where(p => p.Grade.HasValue)
            .OrderBy(p => p.Grade)
            .ThenByDescending(p => p.Goals)
            .ThenByDescending(p => p.RatingScore)
            .FirstOrDefault()?.Player.Id;
    }

    private void UpdateGrades()
    {
        var minute = _finished ? 90 : Minute;
        foreach (var mp in _teams.SelectMany(t => t.Participants))
        {
            var raw = C.GradeBase - mp.RatingScore * C.GradeScale;
            var grade = Math.Clamp(Math.Round(raw * 2) / 2, 1.0, 6.0);
            var minutes = mp.MinutesPlayed(minute);
            mp.Grade = _finished && minutes < C.GradeMinMinutes ? null : grade;
        }
    }

    // ------------------------------------------------------------------ Phase

    private void SimulatePhase()
    {
        var att = Team(_ball);
        var def = Team(_ball.Other());
        _phaseDuration = 0;
        PlayPhase(att, def);
        EndPhase(att);
    }

    private void PlayPhase(MatchTeamState att, MatchTeamState def)
    {
        _carrier = null;
        _lastPasser = null;
        var restart = _restart;
        _restart = Restart.None;

        switch (restart)
        {
            case Restart.KickOff:
                _row = 2; _col = 2; _counter = false;
                _phaseDuration += C.RestartSeconds;
                break;
            case Restart.GoalKick:
                _phaseDuration += C.RestartSeconds * 1.5;
                _col = _rng.Next(1, 3);
                _counter = false;
                if (att.Tactic.Instructions.GoalkeeperDistribution == Distribution.Fast && _rng.Chance(0.2))
                {
                    _row = 2;
                    _counter = _rng.Chance(0.4);
                }
                else if (att.Tactic.Instructions.PassingLength == PassingLength.Long || _rng.Chance(0.35))
                {
                    // Langer Abschlag: Kopfballduell im Mittelfeld.
                    if (!_rng.Chance(0.5))
                    {
                        Turnover(att, def, 3, _col, allowCounter: false);
                        return;
                    }
                    _row = 3;
                }
                else _row = 1;
                break;
            case Restart.Corner:
                _phaseDuration += C.SetPieceSeconds;
                ResolveCorner(att, def);
                return;
            case Restart.Penalty:
                _phaseDuration += C.SetPieceSeconds;
                ResolvePenalty(att, def);
                return;
            case Restart.FreeKick:
                _phaseDuration += C.SetPieceSeconds;
                if (ResolveFreeKick(att, def)) return;
                break;
        }

        _carrier ??= ChooseAttacker(att, _row, _col, null, ActionType.Pass);
        var announced = false;
        for (var step = 0; step < C.MaxStepsPerPhase; step++)
        {
            _phaseDuration += StepSeconds(att);
            att.Stats.Heat[_row * Grid.Cols + _col]++;
            DrainAction(_carrier);

            var action = ChooseAction(att, _carrier, _row, _col);
            var continues = action switch
            {
                ActionType.Pass => DoPass(att, def),
                ActionType.Dribble => DoDribble(att, def),
                ActionType.Cross => DoCross(att, def),
                _ => DoShot(att, def),
            };
            if (!continues) return;

            if (!announced && _row >= 4)
            {
                announced = true;
                Emit(_counter ? MatchEventType.Counter : MatchEventType.Attack, att.Side, _carrier, row: _row, col: _col);
            }
        }

        // Angriff festgefahren: Ball zurückspielen und neu aufbauen oder verlieren.
        if (_rng.Chance(C.RecycleChance))
        {
            _row = Math.Max(1, _row - 2);
            _counter = false;
        }
        else Turnover(att, def, _row, _col);
    }

    private void EndPhase(MatchTeamState att)
    {
        _clock += _phaseDuration;
        att.Stats.PossessionSeconds += _phaseDuration;

        foreach (var team in _teams)
        {
            var intensity = Intensity(team);
            foreach (var mp in team.OnPitch)
            {
                var drain = _phaseDuration * C.ConditionPerSecond * StaminaFactor(mp) * intensity;
                if (mp.Position == Position.GK) drain *= 0.35;
                mp.Condition = Math.Max(15, mp.Condition - drain);
            }

            // Verletzungen ohne Gegnerkontakt (Muskel), wahrscheinlicher bei geringer Kondition.
            if (team.OnPitch.Count > 0)
            {
                var mp = team.OnPitch[_rng.Next(team.OnPitch.Count)];
                if (!mp.Injured && _rng.Chance(C.InjuryPerPhase * InjuryFactor(mp) * (mp.Condition < 75 ? 1.8 : 1)))
                    Injure(team, mp);
            }
        }
    }

    // ------------------------------------------------------------------ Aktionen

    private ActionType ChooseAction(MatchTeamState att, MatchPlayer carrier, int row, int col)
    {
        if (carrier.Position == Position.GK) return ActionType.Pass;
        var w = (double[])C.ActionWeights[Math.Clamp(row, 0, C.ActionWeights.Length - 1)].Clone();
        var ins = att.Tactic.Instructions;
        var m = EffMentality(att);
        var wide = col == 0 || col == Grid.Cols - 1;

        w[2] *= wide ? 1.0 : 0.15;
        w[2] *= ins.Width switch { Width.Wide => 1.3, Width.Narrow => 0.7, _ => 1.0 };
        w[1] *= 0.6 + Eff(carrier, Attr.Dribbling) / 20.0 * 0.8 + Eff(carrier, Attr.Flair) / 20.0 * 0.2;
        w[3] *= 1 + 0.1 * m;
        if (row == 4) w[3] *= 0.4 + carrier.Player.Attributes[Attr.LongShots] / 20.0 * 1.2;
        if (row >= 5) w[3] *= wide ? 0.45 : 1.2;
        if (_counter) w[1] *= 1.2;
        if (att.Shout == Shout.MoreForward) w[3] *= 1.15;
        if (att.Shout == Shout.WasteTime) { w[3] *= 0.6; w[1] *= 0.7; }
        return (ActionType)_rng.WeightedIndex(w);
    }

    private bool DoPass(MatchTeamState att, MatchTeamState def)
    {
        var carrier = _carrier!;
        var ins = att.Tactic.Instructions;
        var r = _rng.NextDouble();
        var adv = ins.PassingLength switch
        {
            PassingLength.Short => r < 0.25 ? 0 : 1,
            PassingLength.Long => r < 0.25 ? 1 : r < 0.85 ? 2 : 3,
            _ => r < 0.2 ? 0 : r < 0.8 ? 1 : 2,
        };
        if (_counter) adv++;
        if (att.Shout == Shout.WasteTime) adv = Math.Min(adv, 1);
        var targetRow = Math.Clamp(_row + adv, carrier.Position == Position.GK ? 1 : 0, Grid.Rows - 1);
        var targetCol = DriftCol(_col, ins);

        var receiver = ChooseAttacker(att, targetRow, targetCol, carrier, ActionType.Pass);
        var defender = ChooseDefender(def, targetRow, targetCol);

        if (targetRow >= 5 && adv >= 1)
        {
            var di = def.Tactic.Instructions;
            var line = di.Line switch { DefensiveLine.High => 1.8, DefensiveLine.Deep => 0.5, _ => 1.0 } * (di.OffsideTrap ? 1.6 : 1.0);
            var pOff = C.OffsideBase * line * (1.3 - Eff(receiver, Attr.OffTheBall) / 20.0 * 0.6);
            if (_rng.Chance(pOff))
            {
                att.Stats.Offsides++;
                Emit(MatchEventType.Offside, att.Side, receiver, row: targetRow, col: targetCol);
                _phaseDuration += C.RestartSeconds;
                Turnover(att, def, targetRow, targetCol, allowCounter: false, deadBall: true);
                return false;
            }
        }

        var a = 0.42 * Eff(carrier, Attr.Passing) + 0.18 * Eff(carrier, Attr.Decisions) + 0.12 * Eff(carrier, Attr.Technique)
                + 0.08 * Eff(carrier, Attr.Flair) + 0.1 * Eff(receiver, Attr.FirstTouch) + 0.1 * Eff(receiver, Attr.OffTheBall)
                + AttackBonus(att, targetRow);
        var d = 0.35 * Eff(defender, Attr.Anticipation) + 0.3 * Eff(defender, Attr.Positioning)
                + 0.2 * Eff(defender, Attr.Marking) + 0.15 * Eff(defender, Attr.Concentration)
                + DefenceBonus(def, targetRow);
        var baseP = C.PassBaseByRow[targetRow] - (adv >= 2 ? C.LongPassPenalty * (adv - 1) : 0) - WeatherPenalty();
        var p = Prob(att.Side, a, d, baseP);

        carrier.Passes++;
        att.Stats.Passes++;
        if (_rng.Chance(p))
        {
            carrier.PassesCompleted++;
            att.Stats.PassesCompleted++;
            carrier.RatingScore += targetRow >= 5 ? 0.03 : 0.004;
            _lastPasser = carrier;
            _carrier = receiver;
            _row = targetRow;
            _col = targetCol;
            return true;
        }
        defender.RatingScore += 0.025;
        carrier.RatingScore -= targetRow >= 5 ? 0.005 : 0.015;
        Turnover(att, def, targetRow, targetCol);
        return false;
    }

    private bool DoDribble(MatchTeamState att, MatchTeamState def)
    {
        var carrier = _carrier!;
        var defender = ChooseDefender(def, _row, _col);
        if (FoulCheck(att, def, carrier, defender)) return false;

        var a = 0.4 * Eff(carrier, Attr.Dribbling) + 0.15 * Eff(carrier, Attr.Agility) + 0.15 * Eff(carrier, Attr.Acceleration)
                + 0.1 * Eff(carrier, Attr.Balance) + 0.1 * Eff(carrier, Attr.Flair) + 0.1 * Eff(carrier, Attr.Technique)
                + AttackBonus(att, _row);
        var d = 0.4 * Eff(defender, Attr.Tackling) + 0.2 * Eff(defender, Attr.Positioning) + 0.1 * Eff(defender, Attr.Acceleration)
                + 0.1 * Eff(defender, Attr.Pace) + 0.1 * Eff(defender, Attr.Strength) + 0.1 * Eff(defender, Attr.Anticipation)
                + DefenceBonus(def, _row) + TacklingBonus(def);
        var won = _rng.Chance(Prob(att.Side, a, d, C.DribbleBase));

        if (won)
        {
            Duel(att, carrier, def, defender, 0.04);
            _row = Math.Min(Grid.Rows - 1, _row + 1);
            if (_row >= 4 && _rng.Chance(0.35))
                Emit(MatchEventType.Dribble, att.Side, carrier, defender, row: _row, col: _col);
            return true;
        }
        Duel(def, defender, att, carrier, 0.04);
        Turnover(att, def, _row, _col);
        return false;
    }

    private bool DoCross(MatchTeamState att, MatchTeamState def)
    {
        var carrier = _carrier!;
        var blocker = ChooseDefender(def, _row, _col);
        var a = 0.6 * Eff(carrier, Attr.Crossing) + 0.2 * Eff(carrier, Attr.Technique) + 0.2 * Eff(carrier, Attr.Decisions) + AttackBonus(att, _row);
        var d = 0.4 * Eff(blocker, Attr.Positioning) + 0.3 * Eff(blocker, Attr.Marking) + 0.3 * Eff(blocker, Attr.Anticipation) + DefenceBonus(def, _row);
        var target = ChooseAttacker(att, 6, 2, carrier, ActionType.Cross);
        Emit(MatchEventType.Cross, att.Side, carrier, target, row: _row, col: _col);

        if (!_rng.Chance(Prob(att.Side, a, d, C.CrossBase)))
        {
            carrier.RatingScore -= 0.01;
            if (_rng.Chance(C.CornerFromCross)) AwardCorner(att);
            else Turnover(att, def, 6, 2);
            return false;
        }

        var marker = ChooseDefender(def, 6, 2, aerial: true);
        if (AerialDuel(att, def, target, marker))
        {
            Emit(MatchEventType.AerialWon, att.Side, target, marker, row: 6, col: 2);
            ResolveShot(att, def, target, carrier, 6, 2, ShotKind.Header);
        }
        else if (_rng.Chance(0.25)) AwardCorner(att);
        else Turnover(att, def, 6, 2);
        return false;
    }

    private bool DoShot(MatchTeamState att, MatchTeamState def)
    {
        var kind = _row <= 4 ? ShotKind.LongShot : ShotKind.Normal;
        ResolveShot(att, def, _carrier!, _lastPasser, _row, _col, kind);
        return false;
    }

    private bool AerialDuel(MatchTeamState att, MatchTeamState def, MatchPlayer attacker, MatchPlayer defender)
    {
        var a = 0.45 * Eff(attacker, Attr.Heading) + 0.35 * Eff(attacker, Attr.JumpingReach)
                + 0.1 * Eff(attacker, Attr.Strength) + 0.1 * Eff(attacker, Attr.Bravery);
        var d = 0.35 * Eff(defender, Attr.Heading) + 0.3 * Eff(defender, Attr.JumpingReach)
                + 0.2 * Eff(defender, Attr.Positioning) + 0.15 * Eff(defender, Attr.Strength);
        var gk = Goalkeeper(def);
        var baseP = C.AerialBase - (gk is null ? -0.1 : (Eff(gk, Attr.CommandOfArea) - 10) * 0.01);
        var won = _rng.Chance(Prob(att.Side, a, d, Math.Clamp(baseP, 0.1, 0.9)));
        if (won) Duel(att, attacker, def, defender, 0.03);
        else Duel(def, defender, att, attacker, 0.03);
        return won;
    }

    private void Duel(MatchTeamState winnerTeam, MatchPlayer winner, MatchTeamState loserTeam, MatchPlayer loser, double weight)
    {
        winner.DuelsWon++;
        loser.DuelsLost++;
        winnerTeam.Stats.DuelsWon++;
        loserTeam.Stats.DuelsLost++;
        winner.RatingScore += weight;
        loser.RatingScore -= weight * 0.85;
    }

    // ------------------------------------------------------------------ Schüsse

    private void ResolveShot(MatchTeamState att, MatchTeamState def, MatchPlayer shooter, MatchPlayer? assister,
                             int row, int col, ShotKind kind, double? xgOverride = null)
    {
        var gk = Goalkeeper(def);
        var central = col is >= 1 and <= 3;
        var xg = xgOverride ?? kind switch
        {
            ShotKind.Penalty => C.PenaltyXg,
            ShotKind.FreeKick => C.FreeKickXg * (row >= 5 ? 1.2 : 0.7),
            ShotKind.Header => C.HeaderXg * (central ? 1.0 : 0.6),
            _ => central ? C.XgCentralByRow[row] : C.XgWideByRow[row],
        };
        if (kind == ShotKind.Normal && _counter && row == 6 && _rng.Chance(C.OneOnOneChance))
        {
            kind = ShotKind.OneOnOne;
            xg = C.OneOnOneXg;
        }
        if (kind is ShotKind.Normal or ShotKind.LongShot or ShotKind.Header)
        {
            var marker = ChooseDefender(def, row, col);
            var pressure = (Eff(marker, Attr.Positioning) + Eff(marker, Attr.Marking)) / 2
                           - (Eff(shooter, Attr.OffTheBall) + Eff(shooter, Attr.Composure)) / 2;
            xg *= Math.Exp(-C.PressureSlope * pressure);
        }
        if (gk is null) xg = Math.Max(xg, 0.6);
        xg = Math.Clamp(xg, 0.01, 0.95);

        var s = kind switch
        {
            ShotKind.Header => 0.7 * Eff(shooter, Attr.Heading) + 0.3 * Eff(shooter, Attr.Composure),
            ShotKind.LongShot => 0.7 * Eff(shooter, Attr.LongShots) + 0.3 * Eff(shooter, Attr.Technique),
            ShotKind.FreeKick => 0.8 * Eff(shooter, Attr.FreeKicks) + 0.2 * Eff(shooter, Attr.Technique),
            ShotKind.Penalty => 0.7 * Eff(shooter, Attr.Penalties) + 0.3 * Eff(shooter, Attr.Composure),
            ShotKind.OneOnOne => 0.5 * Eff(shooter, Attr.Finishing) + 0.5 * Eff(shooter, Attr.Composure),
            _ => 0.6 * Eff(shooter, Attr.Finishing) + 0.25 * Eff(shooter, Attr.Composure) + 0.15 * Eff(shooter, Attr.Technique),
        };
        var g = gk is null ? 1.0 : kind switch
        {
            ShotKind.Header => 0.4 * Eff(gk, Attr.Reflexes) + 0.3 * Eff(gk, Attr.Handling) + 0.3 * Eff(gk, Attr.CommandOfArea),
            ShotKind.OneOnOne => 0.6 * Eff(gk, Attr.OneOnOnes) + 0.2 * Eff(gk, Attr.RushingOut) + 0.2 * Eff(gk, Attr.Reflexes),
            ShotKind.Penalty => 0.6 * Eff(gk, Attr.Reflexes) + 0.4 * Eff(gk, Attr.OneOnOnes),
            _ => 0.5 * Eff(gk, Attr.Reflexes) + 0.25 * Eff(gk, Attr.Handling) + 0.25 * Eff(gk, Attr.Concentration),
        };

        shooter.Shots++;
        att.Stats.Shots++;
        att.Stats.Xg += xg;
        _counter = false;

        var pBlock = kind switch
        {
            ShotKind.Penalty or ShotKind.OneOnOne or ShotKind.Header => 0,
            ShotKind.FreeKick => C.WallBlockChance,
            _ => C.BlockChance * (row == 6 ? 0.8 : 1.2),
        };
        if (_rng.Chance(pBlock))
        {
            EmitShot(att, shooter, gk, row, col, xg, ShotOutcome.Blocked, kind);
            if (_rng.Chance(C.CornerFromBlock)) AwardCorner(att);
            else Turnover(att, def, row, col);
            return;
        }

        var pGoal = xg / (1 - pBlock) * Math.Exp((s - 11) * C.FinishingSlope) * Math.Exp(-(g - 11) * C.KeeperSlope);
        if (!Setup.NeutralGround) pGoal *= att.Side == Side.Home ? 1 + C.HomeAdvantage : 1 - C.HomeAdvantage;
        pGoal = Math.Clamp(pGoal, 0.005, 0.97);
        if (_rng.Chance(pGoal))
        {
            Goal(att, def, shooter, assister, gk, row, col, xg, kind);
            return;
        }

        if (xg >= 0.3) shooter.RatingScore -= 0.15;
        var pOn = Math.Max(pGoal + 0.05, C.OnTargetBase * Math.Exp((s - 11) * 0.03));
        var pSave = gk is null ? 0 : Math.Clamp((pOn - pGoal) / (1 - pGoal), 0, 0.95);
        var r = _rng.NextDouble();
        if (r < pSave)
        {
            shooter.ShotsOnTarget++;
            att.Stats.ShotsOnTarget++;
            shooter.RatingScore += 0.03;
            gk!.Saves++;
            gk.RatingScore += 0.1 + xg * 0.5;
            EmitShot(att, shooter, gk, row, col, xg, ShotOutcome.Saved, kind);
            if (_rng.Chance(C.CornerFromSave)) AwardCorner(att);
            else Turnover(att, def, 6, 2, allowCounter: def.Tactic.Instructions.GoalkeeperDistribution == Distribution.Fast);
        }
        else if (r < pSave + C.PostChance)
        {
            EmitShot(att, shooter, gk, row, col, xg, ShotOutcome.Post, kind);
            Turnover(att, def, 6, col);
        }
        else
        {
            EmitShot(att, shooter, gk, row, col, xg, ShotOutcome.Wide, kind);
            _ball = def.Side;
            _restart = Restart.GoalKick;
            Emit(MatchEventType.GoalKick, def.Side);
        }
    }

    private void EmitShot(MatchTeamState att, MatchPlayer shooter, MatchPlayer? gk, int row, int col, double xg, ShotOutcome outcome, ShotKind kind) =>
        Emit(MatchEventType.Shot, att.Side, shooter, gk, row, col, xg, outcome, kind,
             key: outcome is ShotOutcome.Saved or ShotOutcome.Post || xg >= 0.15 || kind is ShotKind.Penalty or ShotKind.FreeKick);

    private void Goal(MatchTeamState att, MatchTeamState def, MatchPlayer shooter, MatchPlayer? assister, MatchPlayer? gk,
                      int row, int col, double xg, ShotKind kind)
    {
        att.Goals++;
        shooter.Goals++;
        shooter.ShotsOnTarget++;
        att.Stats.ShotsOnTarget++;
        shooter.RatingScore += 0.85;
        var assist = assister is not null && assister != shooter && kind is not (ShotKind.Penalty or ShotKind.FreeKick) ? assister : null;
        if (assist is not null)
        {
            assist.Assists++;
            assist.RatingScore += 0.45;
        }
        if (gk is not null) gk.RatingScore -= Math.Max(0.1, 0.4 - xg * 0.4);
        foreach (var d in def.OnPitch.Where(p => PositionInfo.IsDefender(p.Position))) d.RatingScore -= 0.06;

        Emit(MatchEventType.Goal, att.Side, shooter, assist, row, col, xg, ShotOutcome.Goal, kind, key: true);
        _phaseDuration += C.GoalSeconds;
        _stoppageWeight += 0.35;
        _ball = def.Side;
        _restart = Restart.KickOff;
    }

    // ------------------------------------------------------------------ Standards und Fouls

    private void AwardCorner(MatchTeamState att)
    {
        att.Stats.Corners++;
        _ball = att.Side;
        _restart = Restart.Corner;
    }

    private void ResolveCorner(MatchTeamState att, MatchTeamState def)
    {
        var taker = SetPieceTaker(att, att.Tactic.CornerTakerId, Attr.Corners);
        Emit(MatchEventType.Corner, att.Side, taker, row: 6, col: _rng.Chance(0.5) ? 0 : 4);
        var target = ChooseAttacker(att, 6, 2, taker, ActionType.Cross);
        var marker = ChooseDefender(def, 6, 2, aerial: true);
        var quality = Eff(taker, Attr.Corners);
        if (_rng.Chance(C.CornerHeaderChance * (0.7 + quality / 20.0 * 0.6)) && AerialDuel(att, def, target, marker))
        {
            Emit(MatchEventType.AerialWon, att.Side, target, marker, row: 6, col: 2);
            ResolveShot(att, def, target, taker, 6, 2, ShotKind.Header, C.CornerHeaderXg);
        }
        else Turnover(att, def, 6, 2);
    }

    private bool ResolveFreeKick(MatchTeamState att, MatchTeamState def)
    {
        var row = _restartRow;
        var col = _restartCol;
        var taker = SetPieceTaker(att, att.Tactic.FreeKickTakerId, Attr.FreeKicks);
        var central = col is >= 1 and <= 3;
        var direct = (row >= 5 || (row == 4 && central))
                     && _rng.Chance(C.DirectFreeKickShare + (taker.Player.Attributes[Attr.FreeKicks] - 10) * 0.02);
        var meters = row >= 5 ? _rng.Next(17, 23) : _rng.Next(24, 32);
        Emit(MatchEventType.FreeKick, att.Side, taker, row: row, col: col, value: direct ? meters : 0);

        if (direct)
        {
            ResolveShot(att, def, taker, null, row, col, ShotKind.FreeKick);
            return true;
        }
        if (row >= 4)
        {
            var target = ChooseAttacker(att, 6, 2, taker, ActionType.Cross);
            var marker = ChooseDefender(def, 6, 2, aerial: true);
            if (_rng.Chance(0.4) && AerialDuel(att, def, target, marker))
            {
                Emit(MatchEventType.AerialWon, att.Side, target, marker, row: 6, col: 2);
                ResolveShot(att, def, target, taker, 6, 2, ShotKind.Header, C.CornerHeaderXg);
            }
            else Turnover(att, def, 6, 2);
            return true;
        }
        _row = row;
        _col = col;
        _carrier = taker;
        return false;
    }

    private void ResolvePenalty(MatchTeamState att, MatchTeamState def)
    {
        var taker = SetPieceTaker(att, att.Tactic.PenaltyTakerId, Attr.Penalties);
        ResolveShot(att, def, taker, null, 6, 2, ShotKind.Penalty);
    }

    private MatchPlayer SetPieceTaker(MatchTeamState team, int? preferredId, Attr attr)
    {
        var preferred = team.OnPitch.FirstOrDefault(p => p.Player.Id == preferredId && !p.Injured);
        return preferred ?? team.OnPitch.Where(p => p.Position != Position.GK).DefaultIfEmpty(team.OnPitch[0])
            .MaxBy(p => p.Player.Attributes[attr])!;
    }

    private bool FoulCheck(MatchTeamState att, MatchTeamState def, MatchPlayer victim, MatchPlayer fouler)
    {
        var di = def.Tactic.Instructions;
        var p = C.FoulPerDuel
                * (0.55 + Eff(fouler, Attr.Aggression) / 20.0 * 0.9)
                * di.Tackling switch { TacklingStyle.Hard => 1.45, TacklingStyle.Fair => 0.7, _ => 1.0 }
                * (1.25 - fouler.Player.Hidden.Sportsmanship / 20.0 * 0.5)
                * (def.Shout == Shout.CalmDown ? 0.7 : 1.0)
                * (_counter ? 1.4 : 1.0)
                * (fouler.Yellow ? 0.5 : 1.0)
                * (0.8 + Eff(victim, Attr.Dribbling) / 20.0 * 0.4);
        if (!_rng.Chance(p)) return false;
        HandleFoul(att, def, victim, fouler);
        return true;
    }

    private void HandleFoul(MatchTeamState att, MatchTeamState def, MatchPlayer victim, MatchPlayer fouler)
    {
        fouler.Fouls++;
        def.Stats.Fouls++;
        fouler.RatingScore -= 0.03;
        _phaseDuration += C.FoulSeconds;
        var row = _row;
        var col = _col;
        var meters = row >= 4 ? (row >= 5 ? _rng.Next(16, 23) : _rng.Next(23, 32)) : 0;
        Emit(MatchEventType.Foul, def.Side, fouler, victim, row, col, value: meters);

        var di = def.Tactic.Instructions;
        var strict = 0.75 + Setup.RefereeStrictness / 20.0 * 0.5;
        var hard = di.Tackling switch { TacklingStyle.Hard => 1.3, TacklingStyle.Fair => 0.8, _ => 1.0 };
        var tactical = _counter || row >= 4 ? 1.25 : 1.0;
        var pRed = C.RedPerFoul * strict * hard * (0.6 + Eff(fouler, Attr.Aggression) / 20.0 * 0.8);
        var pYellow = C.YellowPerFoul * strict * hard * tactical * (0.7 + fouler.Player.Hidden.Temperament / 20.0 * 0.6)
                      * (fouler.Yellow ? 0.6 : 1.0);
        var r = _rng.NextDouble();
        if (r < pRed) SendOff(def, fouler, secondYellow: false);
        else if (r < pRed + pYellow)
        {
            if (fouler.Yellow) SendOff(def, fouler, secondYellow: true);
            else
            {
                fouler.Yellow = true;
                def.Stats.YellowCards++;
                fouler.RatingScore -= 0.15;
                _stoppageWeight += 0.1;
                Emit(MatchEventType.YellowCard, def.Side, fouler, key: true);
            }
        }

        if (!victim.Injured && _rng.Chance(C.InjuryPerFoul * InjuryFactor(victim)))
            Injure(att, victim);

        _counter = false;
        var boxShare = row == 6 ? C.BoxFoulShare : row == 5 ? C.EdgeFoulShare : 0;
        if (col is >= 1 and <= 3 && _rng.Chance(boxShare))
        {
            _restart = Restart.Penalty;
            Emit(MatchEventType.Penalty, att.Side, victim, fouler, 6, 2, key: true);
        }
        else if (row >= 4)
        {
            _restart = Restart.FreeKick;
            _restartRow = Math.Min(row, 5);
            _restartCol = col;
        }
        // In der eigenen Hälfte: Freistoß wird schnell ausgeführt, Angriff läuft in der nächsten Phase weiter.
        _ball = att.Side;
    }

    private void SendOff(MatchTeamState team, MatchPlayer mp, bool secondYellow)
    {
        mp.Red = true;
        team.Stats.RedCards++;
        mp.RatingScore -= 1.2;
        _stoppageWeight += 0.4;
        Emit(secondYellow ? MatchEventType.SecondYellow : MatchEventType.RedCard, team.Side, mp, key: true);
        RemoveFromPitch(team, mp);
        if (mp.Position == Position.GK) EnsureGoalkeeper(team);
    }

    private void RemoveFromPitch(MatchTeamState team, MatchPlayer mp)
    {
        team.OnPitch.Remove(mp);
        mp.MinuteOff = Minute;
        team.Off.Add(mp);
        if (_carrier == mp) _carrier = null;
    }

    /// <summary>Ohne Torwart auf dem Platz: Ersatztorwart einwechseln oder einen Feldspieler ins Tor stellen.</summary>
    private void EnsureGoalkeeper(MatchTeamState team)
    {
        if (team.OnPitch.Any(p => p.Position == Position.GK) || team.OnPitch.Count == 0) return;
        var gkSlot = team.Formation.Slots.FindIndex(s => s.Position == Position.GK);
        var benchGk = team.Bench.FirstOrDefault(p => p.Player.MainPosition == Position.GK);
        if (benchGk is not null && team.SubstitutionsUsed < Setup.Substitutions)
        {
            var outfield = team.OnPitch.MinBy(p => p.RatingScore)!;
            DoSubstitute(team, outfield, benchGk);
            benchGk.SlotIndex = Math.Max(0, gkSlot);
            benchGk.Position = Position.GK;
            return;
        }
        var stand_in = team.OnPitch.MaxBy(p => p.Player.Attributes[Attr.Reflexes] + p.Player.Attributes[Attr.Handling])!;
        stand_in.SlotIndex = Math.Max(0, gkSlot);
        stand_in.Position = Position.GK;
    }

    private void Injure(MatchTeamState team, MatchPlayer mp)
    {
        mp.Injured = true;
        team.Stats.Injuries++;
        var types = Setup.InjuryTypes;
        if (types.Count > 0)
        {
            var t = types[_rng.WeightedIndex(types.Select(x => x.Weight).ToList())];
            mp.InjuryType = t.Key;
            mp.InjuryDays = _rng.Next(t.MinDays, Math.Max(t.MinDays, t.MaxDays));
        }
        else
        {
            mp.InjuryType = "strain";
            mp.InjuryDays = _rng.Next(3, 10);
        }
        _stoppageWeight += 0.6;
        Emit(MatchEventType.Injury, team.Side, mp, key: true);
        if (!team.HumanControlled) AutoReplace(team, mp);
    }

    // ------------------------------------------------------------------ Ballverlust

    private void Turnover(MatchTeamState att, MatchTeamState def, int rowAtt, int colAtt, bool allowCounter = true, bool deadBall = false)
    {
        if (!deadBall && att.Tactic.Instructions.Gegenpressing && rowAtt >= 3 && _rng.Chance(C.GegenpressingRegain))
        {
            // Gegenpressing: Ball sofort zurückerobert, Angriff wird neu aufgebaut.
            _row = Math.Max(2, rowAtt - 1);
            _col = colAtt;
            _counter = false;
            foreach (var mp in att.OnPitch) mp.Condition = Math.Max(15, mp.Condition - 0.25);
            return;
        }
        _ball = def.Side;
        _row = Math.Clamp(Grid.Rows - 1 - rowAtt, 0, 5);
        _col = Grid.Cols - 1 - colAtt;
        var counterChance = def.Tactic.Instructions.Counter ? C.CounterChance : 0.1;
        _counter = allowCounter && !deadBall && rowAtt >= 3 && _rng.Chance(counterChance);
    }

    // ------------------------------------------------------------------ Spielerwahl

    private (double col, double row) AttackPos(MatchTeamState team, MatchPlayer mp)
    {
        if (mp.Position == Position.GK || mp.SlotIndex < 0) return (2, 0);
        var slot = team.Formation.Slots[mp.SlotIndex];
        var push = slot.Duty switch { Duty.Defend => 0.2, Duty.Attack => 1.4, _ => 0.8 };
        return (slot.Col, slot.Row + push + 0.15 * EffMentality(team));
    }

    private (double col, double row) DefendPos(MatchTeamState team, MatchPlayer mp)
    {
        if (mp.Position == Position.GK || mp.SlotIndex < 0) return (2, 6.5);
        var slot = team.Formation.Slots[mp.SlotIndex];
        var press = team.Tactic.Instructions.Pressing switch { PressingHeight.High => 0.6, PressingHeight.Low => -0.5, _ => 0 };
        var ownRow = slot.Row * 0.8 + press;
        return (Grid.Cols - 1 - slot.Col, Grid.Rows - 1 - ownRow);
    }

    private MatchPlayer ChooseAttacker(MatchTeamState team, int row, int col, MatchPlayer? exclude, ActionType action)
    {
        var candidates = team.OnPitch.Where(p => p != exclude && (p.Position != Position.GK || row <= 0)).ToList();
        if (candidates.Count == 0) candidates = team.OnPitch.Where(p => p != exclude).ToList();
        if (candidates.Count == 0) return exclude ?? team.OnPitch[0];
        var weights = new double[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var mp = candidates[i];
            var (pc, pr) = AttackPos(team, mp);
            var dc = pc - col;
            var dr = pr - row;
            var w = Math.Exp(-(dc * dc * 0.45 + dr * dr * 0.7));
            if (action == ActionType.Cross)
                w *= (mp.Player.Attributes[Attr.Heading] + mp.Player.Attributes[Attr.JumpingReach]) / 20.0;
            if (mp.Injured) w *= 0.5;
            weights[i] = w + 1e-6;
        }
        return candidates[_rng.WeightedIndex(weights)];
    }

    private MatchPlayer ChooseDefender(MatchTeamState team, int row, int col, bool aerial = false)
    {
        var candidates = team.OnPitch.Where(p => p.Position != Position.GK).ToList();
        if (candidates.Count == 0) candidates = team.OnPitch.ToList();
        var weights = new double[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var mp = candidates[i];
            var (pc, pr) = DefendPos(team, mp);
            var dc = pc - col;
            var dr = pr - row;
            var w = Math.Exp(-(dc * dc * 0.45 + dr * dr * 0.7));
            if (aerial) w *= (mp.Player.Attributes[Attr.Heading] + mp.Player.Attributes[Attr.JumpingReach]) / 20.0;
            weights[i] = w + 1e-6;
        }
        return candidates[_rng.WeightedIndex(weights)];
    }

    private static MatchPlayer? Goalkeeper(MatchTeamState team) => team.OnPitch.FirstOrDefault(p => p.Position == Position.GK);

    private int DriftCol(int col, TeamInstructions ins)
    {
        var shift = _rng.Next(-1, 1);
        var target = col + shift;
        if (ins.Focus == AttackFocus.Wings && _rng.Chance(0.3)) target = col < 2 ? 0 : col > 2 ? 4 : _rng.Chance(0.5) ? 0 : 4;
        if (ins.Focus == AttackFocus.Centre && _rng.Chance(0.3)) target = 2;
        if (ins.Width == Width.Wide && _rng.Chance(0.15)) target = target < 2 ? 0 : 4;
        if (ins.Width == Width.Narrow && _rng.Chance(0.15)) target = Math.Clamp(target, 1, 3);
        return Math.Clamp(target, 0, Grid.Cols - 1);
    }

    // ------------------------------------------------------------------ Werte und Modifikatoren

    /// <summary>Effektiver Attributwert inkl. Moral, Kondition, Tagesform, Spielpraxis und Positionseignung.</summary>
    private double Eff(MatchPlayer mp, Attr a)
    {
        double v = mp.Player.Attributes[a];
        var group = AttrInfo.GroupOf(a);
        if (group == AttrGroup.Mental) v += ((int)mp.Morale - 3) * 0.67;
        var team = _teamOf[mp];
        if (a == Attr.Concentration && team.Shout == Shout.Concentrate) v += 2;
        if (a == Attr.Aggression && team.Shout == Shout.CalmDown) v -= 3;
        v += mp.DayForm;

        var cond = mp.Condition / 100.0;
        v *= 0.8 + 0.2 * cond;
        if (group == AttrGroup.Physical) v *= 0.9 + 0.1 * cond;
        v *= 0.95 + 0.05 * mp.Player.State.MatchPractice / 100.0;
        if (group != AttrGroup.Goalkeeping)
            v *= 0.8 + 0.2 * PositionInfo.SuitabilityFactor(mp.Player.SuitabilityFor(mp.Position));
        if (mp.Injured) v *= 0.75;
        return Math.Max(1, v);
    }

    private double Prob(Side attacking, double a, double d, double baseP)
    {
        if (!Setup.NeutralGround)
        {
            if (attacking == Side.Home) a *= 1 + C.HomeAdvantage;
            else d *= 1 + C.HomeAdvantage;
        }
        baseP = Math.Clamp(baseP, 0.02, 0.98);
        var logit = Math.Log(baseP / (1 - baseP)) + C.DuelSlope * (a - d);
        return 1 / (1 + Math.Exp(-logit));
    }

    private int EffMentality(MatchTeamState team)
    {
        var m = (int)team.Tactic.Mentality + team.Shout switch { Shout.MoreForward => 1, Shout.WasteTime => -1, _ => 0 };
        return Math.Clamp(m, -3, 3);
    }

    private double AttackBonus(MatchTeamState att, int row)
    {
        var bonus = 0.12 * EffMentality(att);
        if (_counter) bonus += C.CounterBonus;
        bonus += att.Tactic.Instructions.Tempo switch { Tempo.High => -0.3, Tempo.Low => 0.15, _ => 0 };
        return bonus;
    }

    private double DefenceBonus(MatchTeamState def, int attRow)
    {
        var ins = def.Tactic.Instructions;
        var bonus = -0.1 * EffMentality(def);
        if (ins.Pressing == PressingHeight.High && attRow <= 2) bonus += 1.0;
        if (ins.Pressing == PressingHeight.Low) bonus += attRow >= 5 ? 0.5 : attRow <= 2 ? -0.5 : 0;
        if (ins.Line == DefensiveLine.High && attRow >= 5) bonus -= 0.6;
        if (ins.Line == DefensiveLine.Deep) bonus += attRow >= 5 ? 0.4 : attRow == 4 ? -0.3 : 0;
        if (def.Shout == Shout.Concentrate) bonus += 0.3;
        return bonus;
    }

    private static double TacklingBonus(MatchTeamState def) =>
        def.Tactic.Instructions.Tackling switch { TacklingStyle.Hard => 0.5, TacklingStyle.Fair => -0.4, _ => 0 };

    private double WeatherPenalty() => Setup.Weather switch
    {
        Weather.Rain => 0.02,
        Weather.Snow => 0.04,
        Weather.Wind => 0.015,
        _ => 0,
    };

    private double StepSeconds(MatchTeamState att)
    {
        var s = C.StepSeconds * att.Tactic.Instructions.Tempo switch { Tempo.Low => 1.2, Tempo.High => 0.85, _ => 1.0 };
        return att.Shout == Shout.WasteTime ? s * 1.35 : s;
    }

    private static double StaminaFactor(MatchPlayer mp) => 1.45 - mp.Player.Attributes[Attr.Stamina] / 20.0 * 0.8;

    private static double Intensity(MatchTeamState team)
    {
        var ins = team.Tactic.Instructions;
        var f = ins.Pressing switch { PressingHeight.High => 1.12, PressingHeight.Low => 0.92, _ => 1.0 };
        if (ins.Tempo == Tempo.High) f *= 1.06;
        if (ins.Gegenpressing) f *= 1.05;
        return f;
    }

    private double InjuryFactor(MatchPlayer mp) =>
        Setup.InjuryFrequency * (0.5 + mp.Player.Hidden.InjuryProneness / 20.0) * (mp.Condition < 70 ? 1.4 : 1.0);

    private void DrainAction(MatchPlayer mp) =>
        mp.Condition = Math.Max(15, mp.Condition - C.ConditionPerAction * StaminaFactor(mp));

    // ------------------------------------------------------------------ KI und Eingriffe

    private void AiManage(Side side)
    {
        var team = Team(side);
        if (team.HumanControlled || _finished) return;
        var minute = Minute;
        var other = Team(side.Other());

        foreach (var injured in team.OnPitch.Where(p => p.Injured).ToList())
            AutoReplace(team, injured);

        if (minute >= C.AiEarliestSubMinute && team.SubstitutionsUsed < Setup.Substitutions
            && minute - _lastAiSubMinute[(int)side] >= 4 && team.Bench.Count > 0)
        {
            var outfield = team.OnPitch.Where(p => p.Position != Position.GK).ToList();
            if (outfield.Count > 0)
            {
                var candidate = outfield.MinBy(p => p.Condition)!;
                var tired = candidate.Condition < C.AiSubCondition;
                var late = minute >= 70 && team.SubstitutionsUsed < Math.Min(3, Setup.Substitutions);
                if (!tired && late) candidate = outfield.MinBy(p => p.RatingScore + p.Condition / 100.0)!;
                if ((tired || late) && AutoReplace(team, candidate)) _lastAiSubMinute[(int)side] = minute;
            }
        }

        var diff = team.Goals - other.Goals;
        var stage = _aiMentalityStage[(int)side];
        if (stage == 0 && minute >= 70 && diff < 0) { Shift(team, +1); _aiMentalityStage[(int)side] = 1; }
        else if (stage == 1 && minute >= 82 && diff == -1) { Shift(team, +1); _aiMentalityStage[(int)side] = 2; }
        else if (stage == 0 && minute >= 80 && diff == 1) { Shift(team, -1); _aiMentalityStage[(int)side] = 3; }
    }

    private static void Shift(MatchTeamState team, int delta) =>
        team.Tactic.Mentality = (Mentality)Math.Clamp((int)team.Tactic.Mentality + delta, -3, 3);

    private bool AutoReplace(MatchTeamState team, MatchPlayer outMp)
    {
        if (team.SubstitutionsUsed >= Setup.Substitutions || !team.OnPitch.Contains(outMp)) return false;
        var pos = outMp.Position;
        var best = team.Bench
            .Where(p => pos == Position.GK ? p.Player.MainPosition == Position.GK : p.Player.MainPosition != Position.GK)
            .MaxBy(p => Ratings.EffectiveRating(p.Player, pos, Setup.Profiles) * (0.75 + 0.25 * p.Condition / 100));
        if (best is null) return false;
        DoSubstitute(team, outMp, best);
        return true;
    }

    private void DoSubstitute(MatchTeamState team, MatchPlayer outMp, MatchPlayer inMp)
    {
        var idx = team.OnPitch.IndexOf(outMp);
        inMp.SlotIndex = outMp.SlotIndex;
        inMp.Position = outMp.Position;
        inMp.MinuteOn = Minute;
        outMp.MinuteOff = Minute;
        team.OnPitch[idx] = inMp;
        team.Bench.Remove(inMp);
        team.Off.Add(outMp);
        team.SubstitutionsUsed++;
        if (_carrier == outMp) _carrier = inMp;
        _stoppageWeight += 0.25;
        Emit(MatchEventType.Substitution, team.Side, inMp, outMp, key: true);
    }

    public bool Substitute(Side side, int outPlayerId, int inPlayerId, out string? errorKey)
    {
        var team = Team(side);
        errorKey = null;
        if (_finished) errorKey = "sub.finished";
        else if (team.SubstitutionsUsed >= Setup.Substitutions) errorKey = "sub.noneLeft";
        var outMp = team.OnPitch.FirstOrDefault(p => p.Player.Id == outPlayerId);
        var inMp = team.Bench.FirstOrDefault(p => p.Player.Id == inPlayerId);
        if (errorKey is null && outMp is null) errorKey = "sub.notOnPitch";
        if (errorKey is null && inMp is null) errorKey = "sub.notOnBench";
        if (errorKey is not null) return false;
        DoSubstitute(team, outMp!, inMp!);
        return true;
    }

    public void ChangeMentality(Side side, Mentality mentality)
    {
        Team(side).Tactic.Mentality = mentality;
        Emit(MatchEventType.TacticChange, side);
    }

    public void ChangeInstructions(Side side, TeamInstructions instructions)
    {
        var tactic = Team(side).Tactic;
        var ins = instructions.Clone();
        tactic.Instructions.PassingLength = ins.PassingLength;
        tactic.Instructions.Tempo = ins.Tempo;
        tactic.Instructions.Width = ins.Width;
        tactic.Instructions.Focus = ins.Focus;
        tactic.Instructions.Counter = ins.Counter;
        tactic.Instructions.Pressing = ins.Pressing;
        tactic.Instructions.Line = ins.Line;
        tactic.Instructions.OffsideTrap = ins.OffsideTrap;
        tactic.Instructions.Tackling = ins.Tackling;
        tactic.Instructions.Gegenpressing = ins.Gegenpressing;
        tactic.Instructions.GoalkeeperDistribution = ins.GoalkeeperDistribution;
        Emit(MatchEventType.TacticChange, side);
    }

    public void ChangeFormation(Side side, Formation formation)
    {
        var team = Team(side);
        var players = team.OnPitch.Select(p => p.Player).ToList();
        var assignment = LineupSelector.Assign(formation, players, Setup.Profiles);
        team.Formation = formation;
        team.Tactic.FormationId = formation.Id;
        for (var slot = 0; slot < assignment.Count; slot++)
        {
            if (assignment[slot] is not { } id) continue;
            var mp = team.OnPitch.First(p => p.Player.Id == id);
            mp.SlotIndex = slot;
            mp.Position = formation.Slots[slot].Position;
        }
        Emit(MatchEventType.TacticChange, side);
    }

    public void SetShout(Side side, Shout shout) => Team(side).Shout = shout;

    public IReadOnlyList<TeamTalkReaction> TeamTalk(Side side, Tone tone, int? playerId = null)
    {
        var team = Team(side);
        var diff = team.Goals - Team(side.Other()).Goals;
        var targets = playerId is { } pid
            ? team.OnPitch.Concat(team.Bench).Where(p => p.Player.Id == pid).ToList()
            : team.OnPitch.Concat(team.Bench).ToList();
        var reactions = new List<TeamTalkReaction>();
        foreach (var mp in targets)
        {
            var score = tone switch
            {
                Tone.Calm => 0.3 + (diff > 0 ? 0.15 : 0),
                Tone.Demanding => diff is 0 or 1 ? 0.6 : diff < 0 ? 0.25 : -0.2,
                Tone.Praising => diff > 0 ? 0.7 : diff == 0 ? 0.05 : -0.45,
                Tone.Angry => diff < 0 ? 0.45 : diff == 0 ? -0.1 : -0.8,
                Tone.Disappointed => diff < 0 ? 0.5 : -0.5,
                _ => !_started || diff <= 0 ? 0.6 : 0.1,
            };
            if (!_started)
            {
                if (tone is Tone.Angry or Tone.Disappointed) score -= 0.8;
                if (tone == Tone.Praising) score = 0;
            }
            var h = mp.Player.Hidden;
            if (tone is Tone.Angry or Tone.Demanding) score += (h.Professionalism - 10) * 0.03 - (h.Temperament - 10) * 0.02;
            if (tone == Tone.Motivating) score += (h.Ambition - 10) * 0.02;
            score += _rng.Normal(0, 0.35) * (1.2 - h.Pressure / 20.0 * 0.6);

            var delta = score > 0.55 ? 1 : score < -0.35 ? -1 : 0;
            mp.Morale = (Morale)Math.Clamp((int)mp.Morale + delta, 0, 6);
            reactions.Add(new TeamTalkReaction(mp.Player.Id, delta));
        }
        return reactions;
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
