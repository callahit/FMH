using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Wechsel-KI und Eingriffe des Managers: Auswechslungen, Mentalität, Anweisungen, Formation, Ansprachen.</summary>
internal sealed partial class MatchSimulation
{
    // ------------------------------------------------------------------ KI und Eingriffe

    private void AiManage(Side side)
    {
        var team = Team(side);
        if (team.HumanControlled || _finished) return;
        var minute = Minute;
        var other = Team(side.Other());

        foreach (var injured in team.OnPitch.Where(p => p.Injured).ToList())
            AutoReplace(team, injured);

        if (minute >= C.AiEarliestSubMinute && team.SubstitutionsUsed < MaxSubstitutions
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
        if (team.SubstitutionsUsed >= MaxSubstitutions || !team.OnPitch.Contains(outMp)) return false;
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
        else if (team.SubstitutionsUsed >= MaxSubstitutions) errorKey = "sub.noneLeft";
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
        // Neue Formation: Standardrollen der Positionen, individuelle Anweisungen verfallen.
        team.Tactic.Orders.Clear();
        team.Orders = formation.Slots.Select(sl => new ResolvedOrder(Setup.Roles.FirstOrDefault(r => r.Positions.Contains(sl.Position)), false, false, false, false)).ToList();
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
}
