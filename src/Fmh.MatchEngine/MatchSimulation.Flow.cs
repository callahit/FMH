using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Phasensteuerung: Ablauf der Spielzeit, Halbzeiten, Verlängerung, Elfmeterschießen, Abpfiff und die einzelne Spielphase.</summary>
internal sealed partial class MatchSimulation
{
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
            StartPeriod(_half + 1);
            Emit(_half == 2 ? MatchEventType.SecondHalfKickOff : MatchEventType.ExtraTimeStart, _ball, key: true);
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

    private void StartPeriod(int period)
    {
        _half = period;
        _clock = 0;
        _stoppageMinutes = -1;
        _stoppageWeight = 0;
        _ball = period % 2 == 0 ? Side.Away : Side.Home;
        _restart = Restart.KickOff;
        _counter = false;
    }

    private void CheckClock()
    {
        var length = PeriodSeconds(_half);
        if (_clock >= length && _stoppageMinutes < 0)
        {
            var max = _half switch { 1 => 4, 2 => 6, 3 => 2, _ => 3 };
            var min = _half >= 3 ? 0 : 1;
            _stoppageMinutes = (int)Math.Clamp(Math.Round(0.6 + _stoppageWeight + _rng.Range(0, 1.4)) - (_half >= 3 ? 1 : 0), min, max);
            if (_stoppageMinutes > 0) Emit(MatchEventType.Stoppage, _ball, value: _stoppageMinutes, key: true);
        }
        if (_stoppageMinutes < 0 || _clock < length + _stoppageMinutes * 60) return;

        switch (_half)
        {
            case 1:
                _halfTime = true;
                Emit(MatchEventType.HalfTime, _ball, key: true);
                break;
            case 2 when Setup.Knockout is not null && TieLevel():
                // Pause vor der Verlängerung; ein zusätzlicher Wechsel ist erlaubt.
                _halfTime = true;
                Emit(MatchEventType.EndOfRegularTime, _ball, key: true);
                break;
            case 3:
                StartPeriod(4);
                Emit(MatchEventType.ExtraTimeSecondHalf, _ball);
                break;
            case 4 when TieLevel():
                Shootout();
                Finish();
                break;
            default:
                Finish();
                break;
        }
    }

    /// <summary>Ist das Duell nach Toren (und ggf. Auswärtstoren) ausgeglichen?</summary>
    private bool TieLevel()
    {
        var k = Setup.Knockout!;
        var homeAgg = Home.Goals + k.FirstLegHome;
        var awayAgg = Away.Goals + k.FirstLegAway;
        if (homeAgg != awayAgg) return false;
        if (k.AwayGoals && k.TwoLegged && k.FirstLegHome != Away.Goals) return false;
        return true;
    }

    public int MaxSubstitutions => Setup.Substitutions + (_half >= 3 || (_halfTime && _half == 2) ? 1 : 0);

    /// <summary>Elfmeterschießen: fünf Schützen je Team, danach Sudden Death.</summary>
    private void Shootout()
    {
        _penHome = 0;
        _penAway = 0;
        Emit(MatchEventType.ShootoutStart, Side.Home, key: true);
        List<MatchPlayer> Order(MatchTeamState t) => t.OnPitch
            .OrderBy(p => p.Position == Position.GK ? 1 : 0)
            .ThenByDescending(p => p.Player.Attributes[Attr.Penalties] * 2 + p.Player.Attributes[Attr.Composure])
            .ToList();
        var takers = new[] { Order(Home), Order(Away) };
        for (var round = 0; round < 30; round++)
        {
            foreach (var side in new[] { Side.Home, Side.Away })
            {
                var team = Team(side);
                var list = takers[(int)side];
                if (list.Count == 0) continue;
                var shooter = list[round % list.Count];
                var gk = Goalkeeper(Team(side.Other()));
                var s = 0.6 * Eff(shooter, Attr.Penalties) + 0.4 * Eff(shooter, Attr.Composure);
                var g = gk is null ? 1.0 : 0.6 * Eff(gk, Attr.Reflexes) + 0.4 * Eff(gk, Attr.OneOnOnes);
                var p = Math.Clamp(C.PenaltyXg * Math.Exp((s - 11) * C.FinishingSlope) * Math.Exp(-(g - 11) * C.KeeperSlope) * (round >= 5 ? 0.97 : 1), 0.4, 0.95);
                var scored = _rng.Chance(p);
                ShotOutcome outcome;
                if (scored)
                {
                    outcome = ShotOutcome.Goal;
                    if (side == Side.Home) _penHome++; else _penAway++;
                    shooter.RatingScore += 0.05;
                }
                else
                {
                    outcome = _rng.Chance(0.6) && gk is not null ? ShotOutcome.Saved : _rng.Chance(0.2) ? ShotOutcome.Post : ShotOutcome.Wide;
                    shooter.RatingScore -= 0.25;
                    if (outcome == ShotOutcome.Saved) gk!.RatingScore += 0.3;
                }
                Emit(MatchEventType.ShootoutKick, side, shooter, gk, outcome: outcome, kind: ShotKind.Penalty,
                     value: _penHome!.Value * 100 + _penAway!.Value, key: true);
                if (round < 5 && Decided(round, side)) goto done;
            }
            if (round >= 4 && _penHome != _penAway) break;
        }
        done:
        Emit(MatchEventType.ShootoutEnd, _penHome > _penAway ? Side.Home : Side.Away, value: _penHome!.Value * 100 + _penAway!.Value, key: true);

        bool Decided(int round, Side justKicked)
        {
            var homeLeft = 5 - (round + 1);
            var awayLeft = 5 - (justKicked == Side.Away ? round + 1 : round);
            return _penHome + homeLeft < _penAway || _penAway + awayLeft < _penHome;
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
        var minute = Minute;
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

        if (ApplyRestart(restart, att, def)) return;

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

    /// <summary>Spielfortsetzung zu Beginn der Phase (Anstoß, Abstoß, Standards). true, wenn die Phase damit endet.</summary>
    private bool ApplyRestart(Restart restart, MatchTeamState att, MatchTeamState def)
    {
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
                        return true;
                    }
                    _row = 3;
                }
                else _row = 1;
                break;
            case Restart.Corner:
                _phaseDuration += C.SetPieceSeconds;
                ResolveCorner(att, def);
                return true;
            case Restart.Penalty:
                _phaseDuration += C.SetPieceSeconds;
                ResolvePenalty(att, def);
                return true;
            case Restart.FreeKick:
                _phaseDuration += C.SetPieceSeconds;
                if (ResolveFreeKick(att, def)) return true;
                break;
        }
        return false;
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
}
