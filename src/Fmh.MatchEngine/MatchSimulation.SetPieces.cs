using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Standards: Ecke, Freistoß, Elfmeter und die Wahl des Schützen.</summary>
internal sealed partial class MatchSimulation
{
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
        var routine = att.Tactic.Corners;
        var headerChance = C.CornerHeaderChance * (0.7 + quality / 20.0 * 0.6) * routine switch
        {
            CornerRoutine.Short => 0.5,
            CornerRoutine.TallPlayersForward => 1.2,
            _ => 1.0,
        };
        var xg = C.CornerHeaderXg * (routine == CornerRoutine.FarPost ? 1.15 : 1.0);
        if (_rng.Chance(headerChance) && AerialDuel(att, def, target, marker))
        {
            Emit(MatchEventType.AerialWon, att.Side, target, marker, row: 6, col: 2);
            ResolveShot(att, def, target, taker, 6, 2, ShotKind.Header, xg);
        }
        else if (routine == CornerRoutine.Short && _rng.Chance(0.55))
        {
            // Kurz ausgeführt: Ballbesitz bleibt, Angriff wird von der Seite neu aufgebaut.
            _row = 5;
            _col = _rng.Chance(0.5) ? 0 : 4;
            _carrier = taker;
        }
        else
        {
            Turnover(att, def, 6, 2);
            if (routine == CornerRoutine.TallPlayersForward && !_counter && _rng.Chance(0.2)) _counter = true;
        }
    }

    private bool ResolveFreeKick(MatchTeamState att, MatchTeamState def)
    {
        var row = _restartRow;
        var col = _restartCol;
        var taker = SetPieceTaker(att, att.Tactic.FreeKickTakerId, Attr.FreeKicks);
        var central = col is >= 1 and <= 3;
        var share = att.Tactic.FreeKicks switch
        {
            FreeKickRoutine.Direct => C.DirectFreeKickShare + 0.25,
            FreeKickRoutine.Cross => 0,
            _ => C.DirectFreeKickShare,
        };
        var direct = (row >= 5 || (row == 4 && central))
                     && _rng.Chance(share + (taker.Player.Attributes[Attr.FreeKicks] - 10) * 0.02);
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
}
