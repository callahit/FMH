using Fmh.Core.Model;

namespace Fmh.Calibrate;

/// <summary>
/// Insolvenzen und Lizenz-Punktabzüge je Land und Ebene (Review F-01/F-03). Beobachtet die Welt nach jedem Tag und
/// zählt Übergänge: Insolvenz (9 Punkte), Punktabzug bei der Lizenzentscheidung, verpasste Auflagen-Frist und
/// Lizenzverweigerung (Zwangsabstieg).
/// </summary>
public static class EconomyReport
{
    /// <summary>Höchstens so viele Insolvenzen je Saison, bezogen auf die Vereinszahl einer Ebene (Mittel über alle Saisons).</summary>
    public const double MaxInsolvencyRate = 0.02;
    /// <summary>Höchstens so viele Lizenz-Punktabzüge je Ebene und Saison (Mittel über alle Saisons).</summary>
    public const double MaxDeductionsPerSeason = 1.0;

    private sealed class Counts
    {
        public int Clubs;
        public int Insolvencies;
        public int Deductions;
        public int Refusals;
    }

    private static readonly Dictionary<(int Season, string Country, int Level), Counts> Seasons = new();
    private static readonly Dictionary<int, (bool Insolvent, LicenseStage Stage, LicenseOutcome Outcome)> Last = new();

    /// <summary>Nach jedem Tag aufrufen.</summary>
    public static void Observe(World w)
    {
        foreach (var c in w.Clubs.Values)
        {
            if (w.FindLeague(c.LeagueId) is not { } league) continue;
            var counts = Get(w.SeasonYear, league.CountryCode, league.Level);
            var now = (c.Finance.Insolvent, c.License.Stage, c.License.Outcome);
            if (Last.TryGetValue(c.Id, out var before))
            {
                if (now.Insolvent && !before.Insolvent) counts.Insolvencies++;
                if (now.Stage == LicenseStage.Decided && before.Stage != LicenseStage.Decided)
                {
                    if (now.Outcome == LicenseOutcome.PointDeduction) counts.Deductions++;
                    if (now.Outcome == LicenseOutcome.Refused) counts.Refusals++;
                }
                // Nicht erfüllte Auflagen zur Frist kosten ebenfalls Punkte.
                if (now.Stage == LicenseStage.Final && before.Stage != LicenseStage.Final
                    && before.Outcome == LicenseOutcome.Conditions && now.Outcome == LicenseOutcome.PointDeduction)
                    counts.Deductions++;
            }
            Last[c.Id] = now;
        }
    }

    private static Counts Get(int season, string country, int level)
    {
        if (!Seasons.TryGetValue((season, country, level), out var c)) Seasons[(season, country, level)] = c = new Counts();
        return c;
    }

    /// <summary>Saisonende: Vereinszahlen festhalten und die Saison ausgeben.</summary>
    public static void PrintSeason(World w)
    {
        foreach (var g in w.Leagues.GroupBy(l => (l.CountryCode, l.Level)).OrderBy(g => g.Key.CountryCode).ThenBy(g => g.Key.Level))
            Get(w.SeasonYear, g.Key.CountryCode, g.Key.Level).Clubs = g.Sum(l => l.ClubIds.Count);
        var rows = Seasons.Where(kv => kv.Key.Season == w.SeasonYear).OrderBy(kv => kv.Key.Country).ThenBy(kv => kv.Key.Level)
            .Select(kv => $"{kv.Key.Country}{kv.Key.Level} {kv.Value.Insolvencies}/{kv.Value.Deductions}/{kv.Value.Refusals}");
        Console.WriteLine($"Insolvenzen/Punktabzüge/Verweigerungen je Ebene: {string.Join(", ", rows)}");
    }

    /// <summary>Mittel über alle ausgewerteten Saisons; true, wenn jede Ebene die Grenzen einhält.</summary>
    public static bool PrintSummary()
    {
        var ok = true;
        var parts = new List<string>();
        foreach (var g in Seasons.Where(kv => kv.Value.Clubs > 0).GroupBy(kv => (kv.Key.Country, kv.Key.Level)).OrderBy(g => g.Key.Country).ThenBy(g => g.Key.Level))
        {
            var seasons = g.Count();
            var rate = g.Sum(kv => kv.Value.Insolvencies) / (double)g.Sum(kv => kv.Value.Clubs);
            var deductions = g.Sum(kv => kv.Value.Deductions) / (double)seasons;
            var inside = rate <= MaxInsolvencyRate && deductions <= MaxDeductionsPerSeason;
            ok &= inside;
            parts.Add($"{g.Key.Country}{g.Key.Level} {rate:P1}/{deductions:0.0}{(inside ? "" : " ABWEICHUNG")}");
        }
        Console.WriteLine($"Ø Insolvenzquote/Punktabzüge je Saison (Grenzen {MaxInsolvencyRate:P0}/{MaxDeductionsPerSeason:0.0}): {string.Join(", ", parts)}");
        return ok;
    }
}
