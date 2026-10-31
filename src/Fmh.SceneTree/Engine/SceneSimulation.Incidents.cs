using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

public sealed partial class SceneSimulation
{
    private enum IncidentKind { Foul, Offside, Corner, ThrowIn, FreeKick, Penalty, Shot, Card, Injury, Rare }

    private sealed class PlannedIncident
    {
        public double LocalMinute;
        public IncidentKind Kind;
        public Side Attack;
        public Side Fouler;
        public int Category;
        public int Node = -1;
        public bool Full;
        public bool Done;
        public bool OnTarget;
        public string? RareTag;
        public double Stoppage;
    }

    /// <summary>1 = dieser Schritt ist fertig, 2 = voller Baum steht bereit, 0 = nichts fällig.</summary>
    private int BeginIncident()
    {
        var now = _clock / 60.0;
        PlannedIncident? due = null;
        foreach (var item in _plan)
        {
            if (item.Done || item.LocalMinute > now) continue;
            due = item;
            break;
        }
        if (due is null) return 0;
        due.Done = true;
        if (due.Full)
            return ArmFull(due) ? 2 : 1;
        EmitExcerpt(due);
        return 1;
    }

    private void PlanPeriod()
    {
        _plan.Clear();
        var scale = (_period <= 2 ? 45.0 : 15.0) / 90.0;
        var length = _period <= 2 ? 44.0 : 14.0;
        var lib = SceneLibrary.For(_tree);

        var fouls = Poisson(scale * ExpectFouls());
        for (var i = 0; i < fouls; i++)
        {
            var fouler = PickSide(FoulFactor);
            var attack = fouler.Other();
            var minute = _rng.Range(1, length);
            Add(minute, IncidentKind.Foul, attack, fouler, node: Pick(lib.Fouls));
            if (_rng.Chance(_opt.PenaltyShare * BoxBias(attack)))
                Add(minute + 0.05, IncidentKind.Penalty, attack, fouler, category: 1, full: true);
            else if (_rng.Chance(_opt.ShootingFkShare))
                Add(minute + 0.05, IncidentKind.FreeKick, attack, fouler, category: FreeKickCategory(), full: true);
            if (_rng.Chance(CardChance(fouler, red: true)))
                Add(minute + 0.08, IncidentKind.Card, attack, fouler, category: 27, full: true);
            else if (_rng.Chance(CardChance(fouler, red: false)))
                Add(minute + 0.08, IncidentKind.Card, attack, fouler, category: 23, full: true);
            if (_rng.Chance(InjuryChance(fouler)))
                Add(minute + 0.1, IncidentKind.Injury, attack, fouler, category: 22, full: true);
        }
        for (var i = 0; i < Poisson(scale * ExpectCorners()); i++)
        {
            var attack = PickSide(CornerFactor);
            var full = _rng.Chance(_opt.CornerFullShare);
            Add(_rng.Range(1, length), IncidentKind.Corner, attack, attack.Other(), category: _rng.Chance(0.5) ? 13 : 14, full: full, node: full ? -1 : Pick(lib.CornerShort));
        }
        for (var i = 0; i < Poisson(scale * ExpectOffsides()); i++)
            Add(_rng.Range(1, length), IncidentKind.Offside, PickSide(OffsideFactor), Side.Home, node: Pick(lib.Offsides));
        for (var i = 0; i < Poisson(scale * _opt.ThrowInsPerMatch); i++)
        {
            var attack = PickSide(_ => 1);
            Add(_rng.Range(1, length), IncidentKind.ThrowIn, attack, attack.Other(), category: _rng.Chance(0.5) ? 15 : 16, node: Pick(lib.ThrowIns));
        }
        for (var i = 0; i < Poisson(scale * _opt.ShortShotsPerMatch * ShotFactor()); i++)
        {
            var attack = PickSide(FinishFactor);
            var on = _rng.Chance(_opt.ShortOnTarget * FinishFactor(attack) / Math.Max(0.7, KeeperFactor(attack.Other())));
            var pool = on ? lib.OnTarget : lib.Wide;
            Add(_rng.Range(1, length), IncidentKind.Shot, attack, attack.Other(), node: Pick(pool), onTarget: on);
        }
        for (var i = 0; i < Poisson(scale * _opt.RarePerMatch); i++)
        {
            if (PickRare() is { } spot)
                Add(_rng.Range(1, length), IncidentKind.Rare, Side.Home, Side.Away, node: spot.Node, tag: spot.Tag, stoppage: spot.Stoppage);
        }
        _plan.Sort((a, b) => a.LocalMinute.CompareTo(b.LocalMinute));
    }

    private void Add(double minute, IncidentKind kind, Side attack, Side fouler, int category = 0, int node = -1, bool full = false, bool onTarget = false, string? tag = null, double stoppage = 0) =>
        _plan.Add(new PlannedIncident
        {
            LocalMinute = minute, Kind = kind, Attack = attack, Fouler = fouler, Category = category, Node = node, Full = full, OnTarget = onTarget, RareTag = tag, Stoppage = stoppage,
        });

    private bool ArmFull(PlannedIncident item)
    {
        _attack = item.Attack;
        if (item.Kind == IncidentKind.Corner) Team(item.Attack).Stats.Corners++;
        PreparePeople(item);
        if (item.Category < 1 || item.Category > _tree.Categories.Count) return false;
        _incidentDriven = true;
        _parent = _tree.Category(item.Category).NodeIndex;
        _sceneMinutes = 0.15;
        _follow.Clear();
        _followChain = 0;
        return true;
    }

    private void EmitExcerpt(PlannedIncident item)
    {
        _attack = item.Attack;
        var (att, def) = Books();
        if (item.Kind == IncidentKind.Foul)
        {
            var fouler = Outfield(item.Fouler, Attr.Tackling) ?? Team(item.Fouler).OnPitch.FirstOrDefault();
            if (fouler is not null)
            {
                Team(item.Fouler).Stats.Fouls++;
                fouler.Fouls++;
            }
        }
        else if (item.Kind == IncidentKind.Offside)
            Team(item.Attack).Stats.Offsides++;
        else if (item.Kind == IncidentKind.Corner)
            Team(item.Attack).Stats.Corners++;
        else if (item.Kind == IncidentKind.Shot)
            AddShortShot(item, att);
        else if (item.Kind == IncidentKind.Rare && item.RareTag is { } tag)
        {
            _rareTags.Add(tag);
            _extraStoppage += item.Stoppage;
        }
        if (item.Node < 0) return;
        var node = _tree.Nodes[item.Node];
        var type = item.Kind switch
        {
            IncidentKind.Foul => MatchEventType.Foul,
            IncidentKind.Offside => MatchEventType.Offside,
            IncidentKind.Corner => MatchEventType.Corner,
            IncidentKind.Shot => MatchEventType.Shot,
            _ => MatchEventType.Attack,
        };
        var player = item.Kind == IncidentKind.Foul ? Outfield(item.Fouler, Attr.Tackling) : Actor(node, att, def) ?? Outfield(item.Attack, Attr.Finishing);
        Emit(type, item.Kind == IncidentKind.Foul ? item.Fouler : item.Attack, player, narrative: Narrative(node, att, def),
            outcome: item.Kind == IncidentKind.Shot ? (item.OnTarget ? ShotOutcome.Saved : ShotOutcome.Wide) : null,
            xg: item.Kind == IncidentKind.Shot ? (item.OnTarget ? _opt.ShortXgOnTarget : _opt.ShortXgWide) : 0);
    }

    private void AddShortShot(PlannedIncident item, RoleBook att)
    {
        var team = Team(item.Attack);
        team.Stats.Shots++;
        team.Stats.Xg += item.OnTarget ? _opt.ShortXgOnTarget : _opt.ShortXgWide;
        if (item.OnTarget) team.Stats.ShotsOnTarget++;
        var shooter = att.Find((int)FmRole.St, 0) ?? Outfield(item.Attack, Attr.Finishing);
        if (shooter is null) return;
        shooter.Shots++;
        if (item.OnTarget) shooter.ShotsOnTarget++;
    }

    private void PreparePeople(PlannedIncident item)
    {
        if (item.Kind == IncidentKind.Card)
        {
            var player = CardTarget(item.Fouler, item.Category >= 27);
            if (player is null || player.Red) { item.Category = 0; return; }
            var second = player.Yellow;
            var conduct = _rng.Chance(0.22);
            player.Red = second || item.Category >= 27;
            if (!player.Red)
            {
                player.Yellow = true;
                Team(item.Fouler).Stats.YellowCards++;
                player.RatingScore -= 0.15;
                _pendingCard = CardPending.Yellow;
            }
            else
            {
                if (!second) player.Yellow = false;
                Team(item.Fouler).Stats.RedCards++;
                player.RatingScore -= 1.2;
                _pendingCard = second ? CardPending.Second : CardPending.Red;
                Remove(player);
                if (Team(item.Fouler).OnPitch.All(p => p.Position != Position.GK)) EnsureGoalkeeper(Team(item.Fouler));
            }
            _cardPlayer = player;
            _reason = conduct ? (byte)37 : (byte)5;
            item.Category = CardCategory(conduct, _pendingCard);
        }
        else if (item.Kind == IncidentKind.Injury)
        {
            var player = Outfield(item.Attack, Attr.Bravery) ?? Outfield(item.Fouler, Attr.Bravery);
            if (player is null || player.Injured) return;
            var (key, days, label) = CardCatalog.Injury((byte)(player.Player.Hidden.InjuryProneness > 14 ? 18 : 31));
            player.Injured = true;
            player.InjuryType = key;
            player.InjuryDays = Math.Max(1, (int)Math.Round(days * Math.Max(0.2, Setup.InjuryFrequency)));
            _teamOf[player].Stats.Injuries++;
            player.RatingScore -= 0.2;
            _injuryPlayer = player;
            _injuryLabel = label;
            _victim = player;
            if (!_teamOf[player].HumanControlled) AutoReplace(_teamOf[player], player);
        }
    }

    private RareSpot? PickRare()
    {
        var spots = SceneLibrary.For(_tree).Rare;
        var weights = new double[spots.Length];
        var crowd = CrowdFactor();
        var any = false;
        for (var i = 0; i < spots.Length; i++)
        {
            if (!SceneRare.Allows(spots[i].Kind, Setup.Weather, Setup.Floodlit, _opt.EasterEggs)) continue;
            weights[i] = spots[i].Kind == RareKind.Crowd ? 1.15 * crowd : 1;
            any = true;
        }
        if (!any) return null;
        return spots[_rng.WeightedIndex(weights)];
    }

    private double CrowdFactor()
    {
        var home = Home.Club;
        var away = Away.Club;
        var derby = home.Rivals.Contains(away.Id) || away.Rivals.Contains(home.Id);
        var weight = derby ? 2.4 : 1.0;
        if (home.StadiumCapacity >= 30000) weight *= 1.45;
        else if (home.StadiumCapacity < 6000) weight *= 0.65;
        if (home.Fans.Mood < 42) weight *= 1.7;
        else if (home.Fans.Mood > 78) weight *= 0.75;
        return weight;
    }

    private double ExpectFouls() => _opt.FoulsPerMatch * 0.5 * (FoulFactor(Side.Home) + FoulFactor(Side.Away));
    private double ExpectOffsides() => _opt.OffsidesPerMatch * 0.5 * (OffsideFactor(Side.Home) + OffsideFactor(Side.Away));
    private double ExpectCorners() => _opt.CornersPerMatch * 0.5 * (CornerFactor(Side.Home) + CornerFactor(Side.Away));
    private double ShotFactor() => 0.5 * (FinishFactor(Side.Home) + FinishFactor(Side.Away));

    private double FoulFactor(Side side)
    {
        var team = Team(side);
        var ins = team.Tactic.Instructions;
        var tackle = ins.Tackling switch { TacklingStyle.Hard => 1.32, TacklingStyle.Fair => 0.74, _ => 1 };
        var press = ins.Pressing switch { PressingHeight.High => 1.16, PressingHeight.Low => 0.84, _ => 1 };
        var agg = Average(team, Attr.Aggression) / 10.5;
        var diff = team.Goals - Team(side.Other()).Goals;
        var score = diff < 0 ? 1.08 : diff > 1 ? 0.92 : 1;
        return tackle * press * Math.Clamp(agg, 0.75, 1.35) * score;
    }

    private double OffsideFactor(Side attack)
    {
        var line = Team(attack.Other()).Tactic.Instructions;
        var trap = line.OffsideTrap ? 1.5 : 0.82;
        var height = line.Line switch { DefensiveLine.High => 1.35, DefensiveLine.Deep => 0.68, _ => 1 };
        return trap * height * Math.Clamp(Average(Team(attack), Attr.OffTheBall) / 11.0, 0.8, 1.3);
    }

    private double CornerFactor(Side attack)
    {
        var ins = Team(attack).Tactic.Instructions;
        var width = ins.Width switch { Width.Wide => 1.28, Width.Narrow => 0.74, _ => 1 };
        var focus = ins.Focus switch { AttackFocus.Wings => 1.22, AttackFocus.Centre => 0.8, _ => 1 };
        return width * focus * Math.Clamp(Average(Team(attack), Attr.Crossing) / 11.0, 0.8, 1.3);
    }

    private double FinishFactor(Side attack)
    {
        var tempo = Team(attack).Tactic.Instructions.Tempo switch { Tempo.High => 1.14, Tempo.Low => 0.86, _ => 1 };
        var mind = 1 + 0.05 * (int)Team(attack).Tactic.Mentality;
        return tempo * mind * Math.Clamp(Average(Team(attack), Attr.Finishing) / 11.0, 0.8, 1.25);
    }

    private double KeeperFactor(Side side) => Math.Clamp(Average(Team(side), Attr.Reflexes) / 11.0, 0.75, 1.3);

    private static double Average(MatchTeamState team, Attr attr)
    {
        var players = team.OnPitch.Where(p => attr is Attr.Reflexes or Attr.Handling ? p.Position == Position.GK : p.Position != Position.GK).ToList();
        return players.Count == 0 ? 10 : players.Average(p => p.Player.Attributes[attr]);
    }

    private double CardChance(Side fouler, bool red)
    {
        var strict = Math.Clamp(Setup.RefereeStrictness / 10.0, 0.6, 1.7);
        var hard = Team(fouler).Tactic.Instructions.Tackling switch { TacklingStyle.Hard => 1.28, TacklingStyle.Fair => 0.72, _ => 1 };
        return (red ? _opt.RedPerFoul : _opt.YellowPerFoul) * strict * hard;
    }

    private double InjuryChance(Side side)
    {
        var prone = Team(side).OnPitch.Count == 0 ? 10 : Team(side).OnPitch.Average(p => p.Player.Hidden.InjuryProneness);
        return _opt.InjuryPerFoul * Math.Max(0.2, Setup.InjuryFrequency) * Math.Clamp(prone / 10.0, 0.6, 1.6);
    }

    private double BoxBias(Side attack) => Team(attack).Tactic.Mentality >= Mentality.Attacking ? 1.25 : 1;

    private int FreeKickCategory() => _rng.Next(3) switch { 0 => 2, 1 => 5, _ => 4 };

    private Side PickSide(Func<Side, double> weight) =>
        (Side)_rng.WeightedIndex(new[] { Math.Max(0.05, weight(Side.Home)), Math.Max(0.05, weight(Side.Away)) });

    private int Pick(int[] pool) => pool.Length == 0 ? -1 : pool[_rng.Next(pool.Length)];

    private MatchPlayer? CardTarget(Side side, bool red)
    {
        var list = Team(side).OnPitch.Where(p => p.Position != Position.GK && !p.Red).ToList();
        if (!red)
        {
            var clean = list.Where(p => !p.Yellow).ToList();
            if (clean.Count > 0) list = clean;
        }
        if (list.Count == 0) return null;
        var weights = list.Select(p => (double)Math.Max(1, p.Player.Attributes[Attr.Aggression])).ToArray();
        return list[_rng.WeightedIndex(weights)];
    }

    private MatchPlayer? Outfield(Side side, Attr attr)
    {
        var list = Team(side).OnPitch.Where(p => p.Position != Position.GK && !p.Red).ToList();
        if (list.Count == 0) return Team(side).OnPitch.FirstOrDefault(p => !p.Red);
        var weights = list.Select(p => (double)Math.Max(1, p.Player.Attributes[attr])).ToArray();
        return list[_rng.WeightedIndex(weights)];
    }

    private int Poisson(double lambda)
    {
        if (lambda <= 0) return 0;
        var limit = Math.Exp(-lambda);
        var count = 0;
        var product = 1.0;
        do { count++; product *= _rng.NextDouble(); } while (product > limit && count < 80);
        return count - 1;
    }
}
