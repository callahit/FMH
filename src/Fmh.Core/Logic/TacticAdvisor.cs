using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Co-Trainer-Hinweis: Text-Schlüssel plus Platzhalterwerte.</summary>
public sealed record Advice(string Key, bool Warning, params object[] Args);

/// <summary>Taktik-Feedback (Kapitel 5): textliche Rückmeldung zu Stärken und Lücken der Taktik vor dem Spiel.</summary>
public static class TacticAdvisor
{
    public static List<Advice> Analyse(Club club, World world, GameData data)
    {
        var result = new List<Advice>();
        var formation = TacticResolver.Resolve(club.Tactic, data);
        var profiles = data.PositionProfiles;
        var eleven = new List<(Player player, FormationSlot slot)>();
        for (var i = 0; i < formation.Slots.Count && i < club.Tactic.Lineup.Count; i++)
        {
            if (club.Tactic.Lineup[i] is { } id && world.Players.TryGetValue(id, out var p))
                eleven.Add((p, formation.Slots[i]));
        }
        if (eleven.Count < formation.Slots.Count)
        {
            result.Add(new Advice("advice.incomplete", true));
            return result;
        }

        foreach (var (p, slot) in eleven)
        {
            var suit = p.SuitabilityFor(slot.Position);
            if (suit <= Suitability.Adequate)
                result.Add(new Advice(suit == Suitability.Unsuitable ? "advice.unsuitable" : "advice.adequate", true, p.Name, slot.Position));
            if (p.State.Condition < 80)
                result.Add(new Advice("advice.tired", true, p.Name, (int)p.State.Condition));
        }

        var ins = club.Tactic.Instructions;
        double Avg(Attr a) => eleven.Average(x => x.player.Attributes[a]);
        double AvgOf(Func<FormationSlot, bool> filter, Attr a) =>
            eleven.Where(x => filter(x.slot)).Select(x => (double)x.player.Attributes[a]).DefaultIfEmpty(10).Average();

        if (ins.Pressing == PressingHeight.High && Avg(Attr.Stamina) < 11.5)
            result.Add(new Advice("advice.pressingStamina", true));
        if (ins.Line == DefensiveLine.High && AvgOf(s => PositionInfo.IsDefender(s.Position), Attr.Pace) < 11)
            result.Add(new Advice("advice.lineSlow", true));
        if (ins.PassingLength == PassingLength.Short && Avg(Attr.Passing) < 10)
            result.Add(new Advice("advice.shortPassing", true));
        if (ins.Counter && AvgOf(s => PositionInfo.IsAttacker(s.Position), Attr.Pace) >= 13)
            result.Add(new Advice("advice.counterGood", false));
        if (ins.Width == Width.Wide && AvgOf(s => PositionInfo.IsWide(s.Position), Attr.Crossing) >= 12.5)
            result.Add(new Advice("advice.wingsGood", false));
        if (ins.Tackling == TacklingStyle.Hard && Avg(Attr.Aggression) >= 13)
            result.Add(new Advice("advice.cards", true));

        var aerial = eleven.Where(x => x.slot.Position == Position.ST)
            .Select(x => x.player)
            .FirstOrDefault(p => p.Attributes[Attr.Heading] + p.Attributes[Attr.JumpingReach] >= 30);
        if (aerial is not null && ins.Width != Width.Wide)
            result.Add(new Advice("advice.targetMan", false, aerial.Name));

        var leftSide = eleven.Where(x => x.slot.Col == 0 && x.slot.Position != Position.GK).Select(x => x.player).ToList();
        if (leftSide.Count > 0 && leftSide.All(p => p.LeftFoot < 10))
            result.Add(new Advice("advice.noLeftFoot", true));

        if (result.Count == 0) result.Add(new Advice("advice.allGood", false));
        return result;
    }
}
