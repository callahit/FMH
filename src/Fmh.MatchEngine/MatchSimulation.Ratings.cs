using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>Werte und Modifikatoren: effektive Attribute, Wahrscheinlichkeiten, Taktik, Wetter, Kraft.</summary>
internal sealed partial class MatchSimulation
{
    // ------------------------------------------------------------------ Werte und Modifikatoren

    /// <summary>Effektiver Attributwert inkl. Moral, Kondition, Tagesform, Spielpraxis und Positionseignung.</summary>
    private double Eff(MatchPlayer mp, Attr a)
    {
        double v = mp.Player.Attributes[a];
        var group = AttrInfo.GroupOf(a);
        if (group == AttrGroup.Mental) v += ((int)mp.Morale - 3) * 0.67;
        var team = _teamOf[mp];
        if (a == Attr.Concentration && team.Shout == Shout.Concentrate) v += 2;
        if (a == Attr.Aggression && team.Shout == Shout.CalmDown) v -= 3;
        v += mp.DayForm;

        var cond = mp.Condition / 100.0;
        v *= 0.8 + 0.2 * cond;
        if (group == AttrGroup.Physical) v *= 0.9 + 0.1 * cond;
        v *= 0.95 + 0.05 * mp.Player.State.MatchPractice / 100.0;
        if (group != AttrGroup.Goalkeeping)
            v *= 0.8 + 0.2 * PositionInfo.SuitabilityFactor(mp.Player.SuitabilityFor(mp.Position));
        if (mp.Injured) v *= 0.75;
        return Math.Max(1, v);
    }

    private readonly Dictionary<MatchPlayer, double> _ownLevel = new();

    /// <summary>
    /// Effektiver Wert gemessen am eigenen Niveau des Spielers (10 = so hoch wie sein Feldspieler-Durchschnitt). Für Wesenszüge
    /// wie Aggressivität: Ein Regionalligaspieler foult so oft wie ein Bundesligaspieler, auch wenn alle seine Werte
    /// niedriger sind (Review F-11).
    /// </summary>
    private double RelEff(MatchPlayer mp, Attr a)
    {
        if (!_ownLevel.TryGetValue(mp, out var level))
        {
            var attrs = AttrInfo.Technical.Concat(AttrInfo.Mental).Concat(AttrInfo.Physical);
            _ownLevel[mp] = level = attrs.Average(x => (double)mp.Player.Attributes[x]);
        }
        return Math.Clamp(Eff(mp, a) * 10 / Math.Max(1, level), 1, 20);
    }

    private double Prob(Side attacking, double a, double d, double baseP)
    {
        if (!Setup.NeutralGround)
        {
            if (attacking == Side.Home) a *= 1 + C.HomeAdvantage;
            else d *= 1 + C.HomeAdvantage;
        }
        baseP = Math.Clamp(baseP, 0.02, 0.98);
        var logit = Math.Log(baseP / (1 - baseP)) + C.DuelSlope * (a - d);
        return 1 / (1 + Math.Exp(-logit));
    }

    private int EffMentality(MatchTeamState team)
    {
        var m = (int)team.Tactic.Mentality + team.Shout switch { Shout.MoreForward => 1, Shout.WasteTime => -1, _ => 0 };
        return Math.Clamp(m, -3, 3);
    }

    private double AttackBonus(MatchTeamState att, int row)
    {
        var bonus = 0.12 * EffMentality(att) + FamiliarityBonus(att);
        if (_counter) bonus += C.CounterBonus;
        bonus += att.Tactic.Instructions.Tempo switch { Tempo.High => -0.3, Tempo.Low => 0.15, _ => 0 };
        return bonus;
    }

    private double DefenceBonus(MatchTeamState def, int attRow)
    {
        var ins = def.Tactic.Instructions;
        var bonus = -0.1 * EffMentality(def) + FamiliarityBonus(def);
        if (ins.Pressing == PressingHeight.High && attRow <= 2) bonus += 1.0;
        if (ins.Pressing == PressingHeight.Low) bonus += attRow >= 5 ? 0.5 : attRow <= 2 ? -0.5 : 0;
        if (ins.Line == DefensiveLine.High && attRow >= 5) bonus -= 0.6;
        if (ins.Line == DefensiveLine.Deep) bonus += attRow >= 5 ? 0.4 : attRow == 4 ? -0.3 : 0;
        if (def.Shout == Shout.Concentrate) bonus += 0.3;
        return bonus;
    }

    /// <summary>Taktikvertrautheit: unter 70 % kostet sie bis zu 0,8 Attributpunkte, darüber bringt sie bis zu 0,4.</summary>
    private static double FamiliarityBonus(MatchTeamState team) => (Math.Clamp(team.Familiarity, 0, 100) - 70) * 0.012;

    private ResolvedOrder? OrderOf(MatchPlayer mp)
    {
        var team = _teamOf[mp];
        return mp.SlotIndex >= 0 && mp.SlotIndex < team.Orders.Count ? team.Orders[mp.SlotIndex] : null;
    }

    private double MarkingBonus(MatchPlayer defender) =>
        (OrderOf(defender) is { } o ? (o.Role?.Tackle ?? 0) + (o.TightMarking ? 0.5 : 0) : 0);

    private static double TacklingBonus(MatchTeamState def) =>
        def.Tactic.Instructions.Tackling switch { TacklingStyle.Hard => 0.5, TacklingStyle.Fair => -0.4, _ => 0 };

    private double WeatherPenalty() => Setup.Weather switch
    {
        Weather.Rain => 0.02,
        Weather.Snow => 0.04,
        Weather.Wind => 0.015,
        _ => 0,
    };

    private double StepSeconds(MatchTeamState att)
    {
        var s = C.StepSeconds * att.Tactic.Instructions.Tempo switch { Tempo.Low => 1.2, Tempo.High => 0.85, _ => 1.0 };
        return att.Shout == Shout.WasteTime ? s * 1.35 : s;
    }

    private static double StaminaFactor(MatchPlayer mp) => 1.45 - mp.Player.Attributes[Attr.Stamina] / 20.0 * 0.8;

    private static double Intensity(MatchTeamState team)
    {
        var ins = team.Tactic.Instructions;
        var f = ins.Pressing switch { PressingHeight.High => 1.12, PressingHeight.Low => 0.92, _ => 1.0 };
        if (ins.Tempo == Tempo.High) f *= 1.06;
        if (ins.Gegenpressing) f *= 1.05;
        return f;
    }

    private double InjuryFactor(MatchPlayer mp) =>
        Setup.InjuryFrequency * (0.5 + mp.Player.Hidden.InjuryProneness / 20.0) * (mp.Condition < 70 ? 1.4 : 1.0);

    private void DrainAction(MatchPlayer mp) =>
        mp.Condition = Math.Max(15, mp.Condition - C.ConditionPerAction * StaminaFactor(mp));
}
