using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Disziplin: Fouls, Karten, Platzverweise, Verletzungen.</summary>
internal sealed partial class MatchSimulation
{
    private bool FoulCheck(MatchTeamState att, MatchTeamState def, MatchPlayer victim, MatchPlayer fouler)
    {
        var di = def.Tactic.Instructions;
        var p = C.FoulPerDuel
                * (0.55 + RelEff(fouler, Attr.Aggression) / 20.0 * 0.9)
                * di.Tackling switch { TacklingStyle.Hard => 1.45, TacklingStyle.Fair => 0.7, _ => 1.0 }
                * (1.25 - fouler.Player.Hidden.Sportsmanship / 20.0 * 0.5)
                * (def.Shout == Shout.CalmDown ? 0.7 : 1.0)
                * (_counter ? 1.4 : 1.0)
                * (fouler.Yellow ? 0.5 : 1.0)
                * (0.8 + RelEff(victim, Attr.Dribbling) / 20.0 * 0.4);
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
        var pRed = C.RedPerFoul * strict * hard * (0.6 + RelEff(fouler, Attr.Aggression) / 20.0 * 0.8);
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
        if (benchGk is not null && team.SubstitutionsUsed < MaxSubstitutions)
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
}
