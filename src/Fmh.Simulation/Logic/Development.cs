using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Spielerentwicklung zum Saisonende (Kapitel 4), ergänzt das tägliche Training um Spielpraxis und Alterung: Wachstum bis ca. 24, Plateau bis 29,
/// Abbau physischer Werte ab 30, mentale Werte können bis 33 noch steigen.
/// </summary>
public static class Development
{
    public static void ApplySeason(Player p, DateOnly today, PositionProfiles profiles, Rng rng)
    {
        var age = p.Age(today);
        var h = p.Hidden;
        var headroom = Math.Max(0, h.PotentialAbility - h.CurrentAbility);
        var practice = Math.Clamp(p.SeasonStats.Minutes / 2000.0, 0.2, 1.2);
        var pro = 0.6 + h.Professionalism / 20.0 * 0.8;

        foreach (var a in AttrInfo.All)
        {
            var group = AttrInfo.GroupOf(a);
            double change = 0;
            if (age <= 24)
                change = headroom / 75.0 * practice * pro * rng.Range(0.3, 1.2);
            else if (age <= 29)
                change = headroom / 150.0 * practice * pro * rng.Range(0, 1) - 0.08;
            else
            {
                if (group == AttrGroup.Physical) change = -rng.Range(0.3, 1.0) * (age - 29) * 0.35;
                else if (group == AttrGroup.Mental && age <= 33) change = rng.Range(-0.1, 0.4);
                else change = -rng.Range(0, 0.4);
            }

            // Ganzzahlige Attribute: Bruchteile wirken als Wahrscheinlichkeit.
            var whole = (int)Math.Truncate(change);
            var frac = change - whole;
            var delta = whole + (rng.NextDouble() < Math.Abs(frac) ? Math.Sign(frac) : 0);
            p.Attributes[a] += delta;
        }
        h.CurrentAbility = Ratings.CurrentAbility(p, profiles);
        if (h.PotentialAbility < h.CurrentAbility) h.PotentialAbility = h.CurrentAbility;
    }

    /// <summary>Wahrscheinlichkeit für ein Karriereende zum Saisonende.</summary>
    public static double RetirementChance(Player p, DateOnly today)
    {
        var age = p.Age(today);
        return age switch
        {
            < 32 => 0,
            < 34 => 0.06,
            < 36 => 0.3,
            _ => 0.85,
        };
    }
}
