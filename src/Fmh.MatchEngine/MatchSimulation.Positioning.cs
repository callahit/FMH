using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Ballverlust und Spielerwahl: Positionen auf dem Raster, Angreifer und Verteidiger.</summary>
internal sealed partial class MatchSimulation
{
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
        if (OrderOf(mp) is { } order)
        {
            push += order.Role?.Push ?? 0;
            if (order.HoldPosition) push -= 0.4;
        }
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
                w *= (mp.Player.Attributes[Attr.Heading] + mp.Player.Attributes[Attr.JumpingReach]) / 20.0 * (OrderOf(mp)?.Role?.Aerial ?? 1);
            w *= OrderOf(mp)?.Role?.Target ?? 1;
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
}
