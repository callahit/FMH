using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

public sealed partial class SceneSimulation
{
    private void AiManage(Side side)
    {
        var team = Team(side);
        if (team.HumanControlled || _finished) return;
        foreach (var injured in team.OnPitch.Where(p => p.Injured).ToList())
            AutoReplace(team, injured);
        if (Minute < Setup.Config.AiEarliestSubMinute || team.SubstitutionsUsed >= MaxSubstitutions || team.Bench.Count == 0) return;
        if (Minute - _lastAiSub[(int)side] < 4) return;
        var outfield = team.OnPitch.Where(p => p.Position != Position.GK).ToList();
        if (outfield.Count == 0) return;
        var tired = outfield.MinBy(p => p.Condition)!;
        if (tired.Condition >= Setup.Config.AiSubCondition && Minute < 70) return;
        if (AutoReplace(team, tired)) _lastAiSub[(int)side] = Minute;
    }

    private bool AutoReplace(MatchTeamState team, MatchPlayer outgoing)
    {
        if (team.SubstitutionsUsed >= MaxSubstitutions || !team.OnPitch.Contains(outgoing)) return false;
        var pos = outgoing.Position;
        var best = team.Bench
            .Where(p => pos == Position.GK ? p.Player.MainPosition == Position.GK : p.Player.MainPosition != Position.GK)
            .MaxBy(p => Ratings.EffectiveRating(p.Player, pos, Setup.Profiles) * (0.75 + 0.25 * p.Condition / 100));
        if (best is null) return false;
        DoSubstitute(team, outgoing, best);
        return true;
    }

    private void DoSubstitute(MatchTeamState team, MatchPlayer outgoing, MatchPlayer incoming)
    {
        var index = team.OnPitch.IndexOf(outgoing);
        incoming.SlotIndex = outgoing.SlotIndex;
        incoming.Position = outgoing.Position;
        incoming.MinuteOn = Minute;
        outgoing.MinuteOff = Minute;
        team.OnPitch[index] = incoming;
        team.Bench.Remove(incoming);
        team.Off.Add(outgoing);
        team.SubstitutionsUsed++;
        Emit(MatchEventType.Substitution, team.Side, incoming, outgoing, key: true);
    }

    private void Remove(MatchPlayer player)
    {
        var team = _teamOf[player];
        if (!team.OnPitch.Remove(player)) return;
        player.MinuteOff = Minute;
        team.Off.Add(player);
    }

    private void EnsureGoalkeeper(MatchTeamState team)
    {
        if (team.OnPitch.Any(p => p.Position == Position.GK) || team.OnPitch.Count == 0) return;
        var slot = team.Formation.Slots.FindIndex(s => s.Position == Position.GK);
        var bench = team.Bench.FirstOrDefault(p => p.Player.MainPosition == Position.GK);
        if (bench is not null && team.SubstitutionsUsed < MaxSubstitutions)
        {
            var outgoing = team.OnPitch.MinBy(p => p.RatingScore)!;
            DoSubstitute(team, outgoing, bench);
            bench.SlotIndex = Math.Max(0, slot);
            bench.Position = Position.GK;
            return;
        }
        var standIn = team.OnPitch.MaxBy(p => p.Player.Attributes[Attr.Reflexes] + p.Player.Attributes[Attr.Handling])!;
        standIn.SlotIndex = Math.Max(0, slot);
        standIn.Position = Position.GK;
    }

    public bool Substitute(Side side, int outPlayerId, int inPlayerId, out string? errorKey)
    {
        var team = Team(side);
        errorKey = null;
        if (_finished) errorKey = "sub.finished";
        else if (team.SubstitutionsUsed >= MaxSubstitutions) errorKey = "sub.noneLeft";
        var outgoing = team.OnPitch.FirstOrDefault(p => p.Player.Id == outPlayerId);
        var incoming = team.Bench.FirstOrDefault(p => p.Player.Id == inPlayerId);
        if (errorKey is null && outgoing is null) errorKey = "sub.notOnPitch";
        if (errorKey is null && incoming is null) errorKey = "sub.notOnBench";
        if (errorKey is not null) return false;
        DoSubstitute(team, outgoing!, incoming!);
        return true;
    }

    public void ChangeMentality(Side side, Mentality mentality)
    {
        Team(side).Tactic.Mentality = mentality;
        Emit(MatchEventType.TacticChange, side);
    }

    public void ChangeInstructions(Side side, TeamInstructions instructions)
    {
        Team(side).Tactic.Instructions = instructions.Clone();
        Emit(MatchEventType.TacticChange, side);
    }

    public void ChangeFormation(Side side, Formation formation)
    {
        var team = Team(side);
        var assignment = LineupSelector.Assign(formation, team.OnPitch.Select(p => p.Player).ToList(), Setup.Profiles);
        team.Formation = formation;
        team.Tactic.FormationId = formation.Id;
        team.Tactic.Orders.Clear();
        team.Orders = formation.Slots.Select(slot =>
            new ResolvedOrder(Setup.Roles.FirstOrDefault(r => r.Positions.Contains(slot.Position)), false, false, false, false)).ToList();
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
        var targets = playerId is { } id
            ? team.OnPitch.Concat(team.Bench).Where(p => p.Player.Id == id).ToList()
            : team.OnPitch.Concat(team.Bench).ToList();
        var reactions = new List<TeamTalkReaction>(targets.Count);
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
            score += _rng.Normal(0, 0.25);
            var delta = score > 0.55 ? 1 : score < -0.35 ? -1 : 0;
            mp.Morale = (Morale)Math.Clamp((int)mp.Morale + delta, 0, 6);
            reactions.Add(new TeamTalkReaction(mp.Player.Id, delta));
        }
        return reactions;
    }
}
