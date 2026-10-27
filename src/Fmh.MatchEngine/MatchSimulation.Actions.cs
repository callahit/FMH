using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Aktionen im Ballbesitz: Pass, Dribbling, Flanke, Schuss, Kopfball und Zweikämpfe.</summary>
internal sealed partial class MatchSimulation
{
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
        if (OrderOf(carrier) is { } order)
        {
            if (order.Role is { } role)
            {
                w[0] *= role.Pass;
                w[1] *= role.Dribble;
                w[2] *= role.Cross;
                w[3] *= role.Shot;
            }
            if (order.MoreShots) w[3] *= 1.5;
            if (order.CrossEarly && row is 3 or 4) w[2] *= 1.6;
        }
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
        if (OrderOf(carrier)?.Role is { LongPass: > 1 } longRole && _rng.Chance((longRole.LongPass - 1) * 0.6)) adv++;
        if (att.Shout == Shout.WasteTime) adv = Math.Min(adv, 1);
        var targetRow = Math.Clamp(_row + adv, carrier.Position == Position.GK ? 1 : 0, Grid.Rows - 1);
        var targetCol = DriftCol(_col, ins);

        var receiver = ChooseAttacker(att, targetRow, targetCol, carrier, ActionType.Pass);
        var defender = ChooseDefender(def, targetRow, targetCol);

        if (targetRow >= 5 && adv >= 1 && Offside(att, def, receiver, targetRow, targetCol)) return false;

        var a = 0.42 * Eff(carrier, Attr.Passing) + 0.18 * Eff(carrier, Attr.Decisions) + 0.12 * Eff(carrier, Attr.Technique)
                + 0.08 * Eff(carrier, Attr.Flair) + 0.1 * Eff(receiver, Attr.FirstTouch) + 0.1 * Eff(receiver, Attr.OffTheBall)
                + AttackBonus(att, targetRow) + 0.5 * (OrderOf(carrier)?.Role?.Playmaking ?? 0);
        var d = 0.35 * Eff(defender, Attr.Anticipation) + 0.3 * Eff(defender, Attr.Positioning)
                + 0.2 * Eff(defender, Attr.Marking) + 0.15 * Eff(defender, Attr.Concentration)
                + DefenceBonus(def, targetRow) + 0.5 * MarkingBonus(defender);
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

    /// <summary>Abseits beim Pass in die beiden letzten Reihen: hängt von Abwehrlinie, Abseitsfalle und Laufwegen ab.</summary>
    private bool Offside(MatchTeamState att, MatchTeamState def, MatchPlayer receiver, int targetRow, int targetCol)
    {
        var di = def.Tactic.Instructions;
        var line = di.Line switch { DefensiveLine.High => 1.8, DefensiveLine.Deep => 0.5, _ => 1.0 } * (di.OffsideTrap ? 1.6 : 1.0);
        var pOff = C.OffsideBase * line * (1.3 - Eff(receiver, Attr.OffTheBall) / 20.0 * 0.6);
        if (!_rng.Chance(pOff)) return false;
        att.Stats.Offsides++;
        Emit(MatchEventType.Offside, att.Side, receiver, row: targetRow, col: targetCol);
        _phaseDuration += C.RestartSeconds;
        Turnover(att, def, targetRow, targetCol, allowCounter: false, deadBall: true);
        return true;
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
                + DefenceBonus(def, _row) + TacklingBonus(def) + MarkingBonus(defender);
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
        var xg = ShotXg(def, shooter, gk, row, col, ref kind, xgOverride);
        var (s, g) = ShotSkill(kind, shooter, gk);

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

        MissedShot(att, def, shooter, gk, row, col, xg, kind, s, pGoal);
    }

    /// <summary>Torwahrscheinlichkeit der Chance aus Art, Position, Konter und Bedrängnis.</summary>
    private double ShotXg(MatchTeamState def, MatchPlayer shooter, MatchPlayer? gk, int row, int col, ref ShotKind kind, double? xgOverride)
    {
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

        return xg;
    }

    /// <summary>Abschlussstärke des Schützen und Stärke des Torwarts für die Schussart.</summary>
    private (double s, double g) ShotSkill(ShotKind kind, MatchPlayer shooter, MatchPlayer? gk)
    {
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

        return (s, g);
    }

    /// <summary>Kein Tor: gehalten, Pfosten oder vorbei.</summary>
    private void MissedShot(MatchTeamState att, MatchTeamState def, MatchPlayer shooter, MatchPlayer? gk, int row, int col,
                            double xg, ShotKind kind, double s, double pGoal)
    {
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
}
