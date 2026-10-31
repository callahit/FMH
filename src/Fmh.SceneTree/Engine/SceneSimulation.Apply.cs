using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

public sealed partial class SceneSimulation
{
    private void Visit(SceneNode node, RoleBook att, RoleBook def)
    {
        if (_shootout && node.Result.Scores) _kickScored = true;

        MatchPlayer? scorer = null;
        MatchPlayer? assist = null;
        var ownGoal = false;
        double xg = 0;
        if (!_shootout) ApplyStats(node, att, def, ref scorer, ref assist, ref ownGoal, ref xg);

        var type = EventType(node);
        var player = type is MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard ? _cardPlayer
            : type == MatchEventType.Injury ? _injuryPlayer
            : scorer ?? Actor(node, att, def);
        var text = Narrative(node, att, def);
        var zoneCol = node.Zone switch { 1 => 4, 2 => 0, _ => 2 };
        var outcome = OutcomeOf(node);
        var kind = KindOf(node);
        if (type is not (MatchEventType.Attack or MatchEventType.Counter) || !string.IsNullOrEmpty(text))
            Emit(type, _attack, player, assist, xg, outcome, kind, ownGoal ? 1 : 0,
                key: type is MatchEventType.Goal or MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard or MatchEventType.Injury or MatchEventType.ShootoutKick,
                narrative: string.IsNullOrEmpty(text) ? null : text, row: RowOf(node), col: zoneCol);

        if (type is MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard)
            _pendingCard = CardPending.None;

        if (!_shootout && node.IsLeaf)
        {
            MaybeInjury(node, att, def);
            MaybeCard(node, att, def);
            if (node.FollowUp is { } next)
            {
                if (next is >= 1 and <= 9 && Offender(node, att, def) is { } fouler)
                    _restartFor = _teamOf[fouler].Side.Other();
                _follow.Enqueue(next);
            }
        }
    }

    private void ApplyStats(SceneNode node, RoleBook att, RoleBook def, ref MatchPlayer? scorer, ref MatchPlayer? assist, ref bool ownGoal, ref double xg)
    {
        var attack = Team(_attack);
        var defence = Team(_attack.Other());
        var touched = !node.Actor.IsEmpty && node.Actor.Attacking ? att.Find(node.Actor, 0) : null;
        Rate(node, att, def);

        if (node.IsLeaf && node.Result.Scores)
        {
            ownGoal = node.Result.OwnGoal || node.GoalType == SceneGoalType.OwnGoal;
            attack.Goals++;
            xg = Chance(node);
            attack.Stats.Shots++;
            attack.Stats.ShotsOnTarget++;
            attack.Stats.Xg += xg;
            Heat(attack, node);
            if (ownGoal)
                scorer = (!node.Actor.IsEmpty && !node.Actor.Attacking ? def.Find(node.Actor, 0) : null)
                         ?? def.Find((int)FmRole.Cd, 0) ?? defence.OnPitch.FirstOrDefault();
            else
            {
                scorer = touched ?? _lastAttacker ?? att.Find((int)FmRole.St, 0) ?? attack.OnPitch.FirstOrDefault(p => p.Position != Position.GK);
                if (scorer is not null)
                {
                    scorer.Goals++;
                    scorer.Shots++;
                    scorer.ShotsOnTarget++;
                    scorer.RatingScore += 0.85;
                }
                if (touched is not null && _lastAttacker is not null && _lastAttacker != touched)
                {
                    assist = _lastAttacker;
                    assist.Assists++;
                    assist.RatingScore += 0.4;
                }
            }
        }
        else if (node.IsLeaf && node.Result.ShotLike)
        {
            var onTarget = node.Result.OnTarget || node.Result.Post || node.Result.Bar || node.Result.KeeperBall;
            xg = Chance(node);
            attack.Stats.Shots++;
            if (onTarget) attack.Stats.ShotsOnTarget++;
            attack.Stats.Xg += xg;
            Heat(attack, node);
            var shooter = touched ?? _lastAttacker;
            if (shooter is not null)
            {
                shooter.Shots++;
                if (onTarget) shooter.ShotsOnTarget++;
            }
            if (node.Result.KeeperBall && def.Find((int)FmRole.Gk, 0) is { } gk)
            {
                gk.Saves++;
                gk.RatingScore += 0.08;
            }
        }

        if (touched is not null) _lastAttacker = touched;

        if (node.Result.Foul && !_foulCounted)
        {
            _foulCounted = true;
            var offender = Offender(node, att, def);
            var team = offender is null ? defence : _teamOf[offender];
            team.Stats.Fouls++;
            if (offender is not null) offender.Fouls++;
        }
        if (node.Result.Corner) attack.Stats.Corners++;
        if ((node.Pass & PassKind.Offside) != 0 && !_offsideCounted)
        {
            _offsideCounted = true;
            attack.Stats.Offsides++;
        }
        if ((node.Action & SceneAction.Pass) != 0)
        {
            attack.Stats.Passes++;
            if ((node.Pass & PassKind.Offside) == 0 && !node.Result.Foul) attack.Stats.PassesCompleted++;
        }
        if ((node.Action & SceneAction.Dribble) != 0)
        {
            attack.Stats.DuelsWon++;
            defence.Stats.DuelsLost++;
        }
        if ((node.Action & SceneAction.Tackle) != 0)
        {
            defence.Stats.DuelsWon++;
            attack.Stats.DuelsLost++;
        }
    }

    private void MaybeCard(SceneNode node, RoleBook att, RoleBook def)
    {
        if (_cardRolled || node.CategoryNumber is >= 22 and <= 28) return;
        if (node.OffenderPlay.IsEmpty && node.OffenderConduct.IsEmpty) return;
        if (!node.Result.Foul && node.Referee == RefDecision.None && (node.Action & SceneAction.FoulWhistle) == 0) return;
        _cardRolled = true;
        var conduct = CardCatalog.Conduct(node.Reason) || (node.OffenderPlay.IsEmpty && !node.OffenderConduct.IsEmpty);
        var role = conduct && !node.OffenderConduct.IsEmpty ? node.OffenderConduct : node.OffenderPlay;
        if (role.IsEmpty) role = node.OffenderConduct;
        var player = (role.Attacking ? att : def).Find(role, 0);
        if (player is null || player.Red) return;

        var strict = Math.Clamp(Setup.RefereeStrictness / 10.0, 0.55, 1.8);
        var hard = _teamOf[player].Tactic.Instructions.Tackling == TacklingStyle.Hard ? 1.25 : _teamOf[player].Tactic.Instructions.Tackling == TacklingStyle.Fair ? 0.8 : 1;
        var pRed = (CardCatalog.Serious(node.Reason) ? _opt.SeriousRedChance : _opt.RedChance) * strict * hard;
        var pYellow = _opt.CardChance * strict * hard * (player.Yellow ? 0.7 : 1);
        var roll = _rng.NextDouble();
        if (roll >= pRed + pYellow) return;

        var team = _teamOf[player];
        if (roll < pRed || player.Yellow)
        {
            var second = player.Yellow && roll >= pRed;
            player.Red = true;
            team.Stats.RedCards++;
            player.RatingScore -= 1.2;
            _pendingCard = second ? CardPending.Second : CardPending.Red;
            Remove(player);
            if (player.Position == Position.GK || team.OnPitch.All(p => p.Position != Position.GK)) EnsureGoalkeeper(team);
        }
        else
        {
            player.Yellow = true;
            team.Stats.YellowCards++;
            player.RatingScore -= 0.15;
            _pendingCard = CardPending.Yellow;
        }
        _cardPlayer = player;
        _reason = node.Reason;
        _victim ??= Victim(node, att, def);
        _follow.Enqueue(CardCategory(conduct, _pendingCard));
    }

    private static int CardCategory(bool conduct, CardPending kind) => kind switch
    {
        CardPending.Second => conduct ? 26 : 25,
        CardPending.Red => conduct ? 28 : 27,
        _ => conduct ? 24 : 23,
    };

    private void MaybeInjury(SceneNode node, RoleBook att, RoleBook def)
    {
        if (_injuryRolled || node.CategoryNumber == 22 || node.Injured.IsEmpty) return;
        _injuryRolled = true;
        if (!_rng.Chance(_opt.InjuryChance * Math.Max(0.1, Setup.InjuryFrequency))) return;
        var player = (node.Injured.Attacking ? att : def).Find(node.Injured, 0);
        if (player is null || player.Injured) return;
        var (key, days, label) = CardCatalog.Injury(node.InjuryTypeCode);
        player.Injured = true;
        player.InjuryType = key;
        player.InjuryDays = Math.Max(1, (int)Math.Round(days * (0.85 + _rng.NextDouble() * 0.3)));
        _teamOf[player].Stats.Injuries++;
        player.RatingScore -= 0.2;
        _injuryPlayer = player;
        _injuryLabel = label;
        _victim = player;
        _follow.Enqueue(22);
        if (!_teamOf[player].HumanControlled) AutoReplace(_teamOf[player], player);
    }

    private MatchEventType EventType(SceneNode node)
    {
        if (_shootout && (node.IsLeaf || node.Result.ShotLike)) return MatchEventType.ShootoutKick;
        if (node.CategoryNumber is >= 23 and <= 28 && node.IsLeaf && _pendingCard != CardPending.None)
            return _pendingCard switch
            {
                CardPending.Second => MatchEventType.SecondYellow,
                CardPending.Red => MatchEventType.RedCard,
                _ => MatchEventType.YellowCard,
            };
        if (node.CategoryNumber == 22 && node.IsLeaf && _injuryPlayer is not null) return MatchEventType.Injury;
        if (node.IsLeaf && node.Result.Scores) return MatchEventType.Goal;
        if (node.IsLeaf && node.Result.ShotLike) return MatchEventType.Shot;
        if (node.Result.Foul) return MatchEventType.Foul;
        if (node.IsLeaf && node.Result.Corner) return MatchEventType.Corner;
        if (node.IsLeaf && (node.Pass & PassKind.Offside) != 0) return MatchEventType.Offside;
        if (node.CategoryNumber == 10 && node.IsLeaf) return MatchEventType.Counter;
        return MatchEventType.Attack;
    }

    private string? Narrative(SceneNode node, RoleBook att, RoleBook def)
    {
        if (!_opt.EmitNarrative || node.LineRefs.Count == 0) return null;
        var line = _tree.Line(node.LineRefs[_rng.Next(node.LineRefs.Count)]);
        var name = new SceneNarrator
        {
            Find = (attack, code, index) => (attack ? att : def).Find(code, index),
            Referee = string.IsNullOrWhiteSpace(Setup.RefereeName) ? "der Schiedsrichter" : Setup.RefereeName,
            Offender = _cardPlayer?.Player.LastName ?? "der Spieler",
            Victim = _victim?.Player.LastName ?? "der Spieler",
            Reason = CardCatalog.Reason(_reason),
            Injury = _injuryLabel,
        };
        return SceneNarrative.Apply(line, name);
    }

    private static void Rate(SceneNode node, RoleBook att, RoleBook def)
    {
        Apply(node.RatingARole, node.RatingA);
        Apply(node.RatingBRole, node.RatingB);
        void Apply(RoleRef role, byte value)
        {
            if (role.IsEmpty || value == 30) return;
            var player = (role.Attacking ? att : def).Find(role, 0);
            if (player is not null) player.RatingScore += (value - 30) / 25.0;
        }
    }

    private static MatchPlayer? Actor(SceneNode node, RoleBook att, RoleBook def) =>
        node.Actor.IsEmpty ? null : (node.Actor.Attacking ? att : def).Find(node.Actor, 0);

    private static MatchPlayer? Offender(SceneNode node, RoleBook att, RoleBook def)
    {
        var role = !node.OffenderPlay.IsEmpty ? node.OffenderPlay : node.OffenderConduct;
        return role.IsEmpty ? null : (role.Attacking ? att : def).Find(role, 0);
    }

    private static MatchPlayer? Victim(SceneNode node, RoleBook att, RoleBook def)
    {
        var role = !node.Victim.IsEmpty ? node.Victim : node.Injured;
        return role.IsEmpty ? null : (role.Attacking ? att : def).Find(role, 0);
    }

    private static ShotOutcome? OutcomeOf(SceneNode node)
    {
        var r = node.Result;
        if (r.Scores) return ShotOutcome.Goal;
        if (r.Wide) return ShotOutcome.Wide;
        if (r.Post || r.Bar) return ShotOutcome.Post;
        if (r.KeeperBall || r.OnTarget) return ShotOutcome.Saved;
        if (r.Shot) return ShotOutcome.Blocked;
        return null;
    }

    private static ShotKind? KindOf(SceneNode node) => node.GoalType switch
    {
        SceneGoalType.Header => ShotKind.Header,
        SceneGoalType.LongRange => ShotKind.LongShot,
        SceneGoalType.DirectFreeKick => ShotKind.FreeKick,
        SceneGoalType.Penalty => ShotKind.Penalty,
        _ when (node.SetPiece & SetPieceKind.Penalty) != 0 => ShotKind.Penalty,
        _ when node.Result.ShotLike || node.Result.Scores => ShotKind.Normal,
        _ => null,
    };

    private double Chance(SceneNode node)
    {
        if (node.GoalType == SceneGoalType.Penalty || (node.SetPiece & SetPieceKind.Penalty) != 0)
            return Setup.Config.PenaltyXg;
        if (node.GoalType == SceneGoalType.DirectFreeKick) return Setup.Config.FreeKickXg;
        // Ohne Distanzbyte: Tore eher aus der Nähe, sonst ein Standardschuss um die 16 Meter.
        var distance = node.ShotDistance is { } meters and < 80 ? meters : node.Result.Scores ? 10 : 16;
        var xg = Math.Exp(-Math.Max(0, distance - 3) / 7.4);
        if (node.GoalType == SceneGoalType.Header) xg *= 0.82;
        if (node.GoalType == SceneGoalType.LongRange) xg *= 0.9;
        // Viele Abschlüsse haben kein Distanzbyte. Der Faktor hebt die Summe in die Nähe der Tore.
        return Math.Clamp(xg * 1.6, 0.03, 0.86);
    }

    private static void Heat(MatchTeamState team, SceneNode node)
    {
        var col = node.Zone switch { 1 => 4, 2 => 0, _ => node.CategoryNumber is 17 or 15 ? 0 : node.CategoryNumber is 19 or 16 ? 4 : 2 };
        var distance = node.ShotDistance ?? 18;
        var row = distance < 8 ? 6 : distance < 16 ? 5 : distance < 25 ? 4 : 3;
        team.Stats.Heat[row * 5 + col]++;
    }

    private static int RowOf(SceneNode node)
    {
        var distance = node.ShotDistance ?? 24;
        return distance < 8 ? 6 : distance < 16 ? 5 : distance < 25 ? 4 : 3;
    }
}
