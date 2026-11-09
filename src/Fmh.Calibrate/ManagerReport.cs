using System.Globalization;
using Fmh.Core.Data;
using Fmh.Core.Game;

namespace Fmh.Calibrate;

/// <summary>Trainerkarussell: Entlassungen je Saison in den 1. Ligen, Ziel aus rules/managers.json.</summary>
internal static class ManagerReport
{
    private static readonly Dictionary<string, List<int>> Sackings = new();

    /// <summary>Am Saisonende: Entlassungen der Saison je 1. Liga (Land).</summary>
    public static void Observe(GameSession session)
    {
        var w = session.World;
        var changes = session.CoachChanges(w.SeasonYear).Where(c => c.Exit == "sacked").ToList();
        foreach (var league in w.Leagues.Where(l => l.Level == 1))
        {
            var count = changes.Count(c => c.Club.LeagueId == league.Id);
            (Sackings.TryGetValue(league.CountryCode, out var list) ? list : Sackings[league.CountryCode] = new()).Add(count);
            Console.WriteLine($"Trainerwechsel {league.Name} {GameSession.SeasonLabel(w.SeasonYear)}: {count} Entlassungen" +
                              (count > 0 ? $" ({string.Join(", ", changes.Where(c => c.Club.LeagueId == league.Id).Select(c => $"{c.Club.Name}: {c.Previous.Name} → {c.Successor?.Name ?? "?"}"))})" : ""));
        }
    }

    public static bool PrintSummary(GameData data)
    {
        if (Sackings.Count == 0) return true;
        // Ungültiger Zielbereich meldet der Datenvalidator; hier nur nicht abstürzen
        var range = data.ManagerRules.SackingsPerTopLeague is [_, _] r ? r : new double[] { 1, 8 };
        var ok = true;
        var parts = new List<string>();
        foreach (var (country, list) in Sackings.OrderBy(kv => kv.Key))
        {
            var mean = list.Average();
            var inside = mean >= range[0] && mean <= range[1];
            ok &= inside;
            parts.Add($"{country} {mean.ToString("0.0", CultureInfo.InvariantCulture)}{(inside ? "" : " ABWEICHUNG")}");
        }
        Console.WriteLine($"Ø Entlassungen je Saison in der 1. Liga (Ziel {range[0]:0}–{range[1]:0}): {string.Join(", ", parts)}");
        return ok;
    }
}
