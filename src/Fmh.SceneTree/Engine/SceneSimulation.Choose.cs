using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

public sealed partial class SceneSimulation
{
    private void Play()
    {
        if (_shootout && _parent < 0 && ShootoutOver())
        {
            EndShootout();
            return;
        }
        if (_parent < 0 && !_shootout && _clock > 0 && ClockDone())
        {
            EndPeriod();
            return;
        }
        if (_parent < 0) OpenParent();
        var parent = _tree.Nodes[_parent];
        if (parent.Children.Count == 0)
        {
            Close();
            return;
        }
        var (att, def) = Books();
        var child = _tree.Nodes[Choose(parent, att, def)];
        Visit(child, att, def);
        if (_shootout ? EndsPathShootout(child) : child.IsLeaf) Close();
        else _parent = child.Index;
    }

    private void OpenParent()
    {
        if (_shootout)
        {
            _attack = _soSide;
            _kickScored = false;
            _parent = _tree.Category(1).NodeIndex;
            return;
        }
        _attack = PickAttack();
        var category = PickCategory();
        _parent = _tree.Category(category).NodeIndex;
        _sceneMinutes = Minutes(category) * (0.85 + _rng.NextDouble() * 0.3);
        _follow.Clear();
        _followChain = 0;
        _foulCounted = _offsideCounted = _cardRolled = _injuryRolled = false;
        _pendingCard = CardPending.None;
        _lastAttacker = _cardPlayer = _victim = _injuryPlayer = null;
        _restartFor = null;
        _reason = 0;
        _injuryLabel = "Verletzung";
    }

    private void Close()
    {
        if (_shootout)
        {
            EndKick();
            return;
        }
        if (_follow.Count > 0 && _followChain < 8)
        {
            _followChain++;
            var next = _follow.Dequeue();
            if (next >= 1 && next <= _tree.Categories.Count)
            {
                if (_restartFor is { } side && next is >= 1 and <= 9)
                {
                    _attack = side;
                    _restartFor = null;
                }
                _parent = _tree.Category(next).NodeIndex;
                return;
            }
        }
        Advance(_sceneMinutes);
        Drain(_sceneMinutes);
        _parent = -1;
        AiManage(Side.Home);
        AiManage(Side.Away);
        if (ClockDone()) EndPeriod();
    }

    private void EndKick()
    {
        if (_kickScored)
        {
            if (_soSide == Side.Home) _penHome++;
            else _penAway++;
        }
        if (_soSide == Side.Home) _soHome++;
        else _soAway++;
        _soSide = _soSide.Other();
        _parent = -1;
        _follow.Clear();
        if (ShootoutOver()) EndShootout();
    }

    private void EndShootout()
    {
        var home = _penHome ?? 0;
        var away = _penAway ?? 0;
        Emit(MatchEventType.ShootoutEnd, home >= away ? Side.Home : Side.Away, value: home * 100 + away, key: true);
        Finish();
    }

    private bool ShootoutOver()
    {
        if (_soHome + _soAway > 48) return true;
        var home = _penHome ?? 0;
        var away = _penAway ?? 0;
        if (_soHome < 5 || _soAway < 5)
            return home + (5 - _soHome) < away || away + (5 - _soAway) < home;
        return _soHome == _soAway && home != away;
    }

    private static bool EndsPathShootout(SceneNode node) =>
        node.IsLeaf || node.Result.Scores || node.Result.Wide || node.Result.Post || node.Result.Bar || node.Result.KeeperBall;

    private (RoleBook Attack, RoleBook Defence) Books() =>
        (RoleBook.Build(Team(_attack)), RoleBook.Build(Team(_attack.Other())));

    private int Choose(SceneNode parent, RoleBook att, RoleBook def)
    {
        var gate = Gate(att, def);
        var kids = parent.Children;
        int[] Pool(Func<SceneNode, bool> ok)
        {
            var list = new List<int>(kids.Count);
            foreach (var id in kids)
                if (ok(_tree.Nodes[id])) list.Add(id);
            return list.ToArray();
        }
        var pool = Pool(n => SceneFilters.Allows(n, gate));
        if (pool.Length == 0) pool = Pool(n => SceneFilters.AllowsCore(n, gate));
        if (pool.Length == 0) pool = Pool(n => SceneFilters.RolesFit(n, gate.Occupied));
        if (pool.Length == 0) pool = kids.ToArray();
        var mood = Mood();
        var weights = new double[pool.Length];
        for (var i = 0; i < pool.Length; i++)
            weights[i] = SceneWeights.Compute(_tree.Nodes[pool[i]], _opt, (role, index) => Attribute(role, index, att, def), mood);
        return pool[_rng.WeightedIndex(weights)];
    }

    private SceneGate Gate(RoleBook att, RoleBook def) => new()
    {
        Weather = Setup.Weather,
        Floodlit = Setup.Floodlit,
        Shootout = _shootout,
        AttackLead = Team(_attack).Goals - Team(_attack.Other()).Goals,
        DefenceOffsideTrap = Team(_attack.Other()).Tactic.Instructions.OffsideTrap,
        EasterEgg = _easter,
        Occupied = role => (role.Attacking ? att : def).Occupied(role.Code),
    };

    private SceneMood Mood() => new(MoodOf(Team(_attack)), MoodOf(Team(_attack.Other())),
        Team(_attack).Tactic.Instructions.PassingLength, Setup.RefereeStrictness);

    private static double MoodOf(MatchTeamState team)
    {
        if (team.OnPitch.Count == 0) return 5;
        var mood = team.OnPitch.Average(p => (int)p.Morale / 6.0 * 5 + p.Player.State.Form / 2.0);
        if (team.Shout == Shout.MoreForward) mood += 0.6;
        if (team.Shout == Shout.CalmDown) mood -= 0.4;
        return Math.Clamp(mood, 1, 10);
    }

    private static double? Attribute(RoleRef role, int index, RoleBook att, RoleBook def)
    {
        var player = (role.Attacking ? att : def).Find(role, 0);
        return player is null ? null : AttributeMap.Value(player, index);
    }

    private Side PickAttack()
    {
        double Score(MatchTeamState team)
        {
            var other = Team(team.Side.Other());
            var skill = team.OnPitch.Count == 0 ? 10 : team.OnPitch.Average(p =>
                (p.Player.Attributes[Attr.Passing] + p.Player.Attributes[Attr.Decisions] + p.Player.Attributes[Attr.OffTheBall]) / 3.0);
            skill += (int)team.Tactic.Mentality * 0.45;
            var diff = team.Goals - other.Goals;
            if (diff < 0) skill += 0.35 * Math.Min(3, -diff);
            if (diff > 0) skill -= 0.12 * Math.Min(3, diff);
            if (team.Side == Side.Home && !Setup.NeutralGround) skill += 0.45;
            return Math.Max(0.2, skill);
        }
        return (Side)_rng.WeightedIndex(new[] { Score(Home), Score(Away) });
    }

    private int PickCategory()
    {
        var team = Team(_attack);
        var ins = team.Tactic.Instructions;
        var ment = (int)team.Tactic.Mentality;
        double left = 4, centre = 4.2, right = 4;
        var slots = team.Formation.Slots;
        foreach (var p in team.OnPitch)
        {
            if (p.SlotIndex < 0 || p.SlotIndex >= slots.Count) continue;
            if (slots[p.SlotIndex].Col <= 1) left += 0.15;
            if (slots[p.SlotIndex].Col >= 3) right += 0.15;
        }
        if (ins.Focus == AttackFocus.Wings) { left += 1.6; right += 1.6; }
        if (ins.Focus == AttackFocus.Centre) centre += 2.2;
        if (ins.Width == Width.Narrow) centre += 1.4;
        if (ins.Width == Width.Wide) { left += 1.1; right += 1.1; }
        var counter = (ins.Counter ? 2.4 : 0.7) + (ment < 0 ? 0.8 : 0);
        var through = 0.55 + (ment > 0 ? 0.45 : 0) + (ins.PassingLength == PassingLength.Short ? 0.25 : 0);
        var build = ins.PassingLength == PassingLength.Long ? 1.3 : 3.2;
        var longBall = (ins.PassingLength == PassingLength.Long ? 2.4 : 0.9)
                       + (ins.GoalkeeperDistribution == Distribution.Fast ? 0.6 : 0) + (ment < 0 ? 0.7 : 0);
        var cats = new[] { 10, 11, 12, 15, 16, 17, 18, 19, 20, 21, 29 };
        var weights = new[] { counter, through, 0.35, 0.28, 0.28, left, centre, right, build, longBall, _easter ? 0.15 : 0.04 };
        return cats[_rng.WeightedIndex(weights)];
    }

    private double Minutes(int category) => category switch
    {
        15 or 16 => _opt.MinutesPerScene * 0.55,
        29 => 0.45,
        21 => _opt.MinutesPerScene * 1.1,
        _ => _opt.MinutesPerScene,
    };

    private void Drain(double minutes)
    {
        foreach (var team in _teams)
            foreach (var mp in team.OnPitch)
            {
                var loss = minutes * (0.18 + (20 - mp.Player.Attributes[Attr.Stamina]) * 0.01);
                if (team.Tactic.Instructions.Pressing == PressingHeight.High) loss *= 1.1;
                if (team.Tactic.Instructions.Tempo == Tempo.High) loss *= 1.06;
                mp.Condition = Math.Max(40, mp.Condition - loss);
            }
    }
}
