using System.Globalization;
using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Calibrate;

/// <summary>
/// Transfermarkt (Review F-07): Anteil der Leihspieler in den Kadern je Land und Ebene am 1. März, Neuzugänge und
/// Ablösetransfers je Verein der höchsten Spielklasse und Saison, erste Saison im Vergleich zu den Folgesaisons.
/// Zielwerte stehen in den Daten (Land: market.loanShare, weltweit: rules/transfers.json reference).
/// </summary>
public static class MarketReport
{
    private static readonly Dictionary<(string Country, int Level), List<double>> LoanShares = new();
    private static readonly Dictionary<string, List<(double All, double Fee)>> TopSignings = new();
    private static readonly List<int> FeeTransfers = new();

    /// <summary>Nach jedem Tag aufrufen: am 1. März den Bestand an Leihspielern festhalten.</summary>
    public static void Observe(World w)
    {
        if (w.Date.Month != 3 || w.Date.Day != 1) return;
        foreach (var g in w.Leagues.GroupBy(l => (l.CountryCode, l.Level)))
        {
            var squads = g.SelectMany(l => l.ClubIds).SelectMany(id => w.SquadOf(w.Clubs[id])).ToList();
            if (squads.Count == 0) continue;
            if (!LoanShares.TryGetValue(g.Key, out var list)) LoanShares[g.Key] = list = new();
            list.Add(squads.Count(p => p.Loan is not null) / (double)squads.Count);
        }
    }

    /// <summary>Saisonende: Neuzugänge der höchsten Spielklasse je Land und alle Ablösetransfers der Saison.</summary>
    public static void PrintSeason(World w)
    {
        var season = w.Transfers.Where(t => t.SeasonYear == w.SeasonYear).ToList();
        FeeTransfers.Add(season.Count(t => !t.Loan && t.FromClubId is not null && t.Fee > 0));
        var parts = new List<string>();
        foreach (var g in w.Leagues.Where(l => l.Level == 1).GroupBy(l => l.CountryCode).OrderBy(g => g.Key))
        {
            var clubs = g.SelectMany(l => l.ClubIds).ToHashSet();
            var incoming = season.Where(t => t.ToClubId is { } to && clubs.Contains(to)).ToList();
            var all = incoming.Count / (double)clubs.Count;
            var fee = incoming.Count(t => !t.Loan && t.FromClubId is not null && t.Fee > 0) / (double)clubs.Count;
            if (!TopSignings.TryGetValue(g.Key, out var list)) TopSignings[g.Key] = list = new();
            list.Add((all, fee));
            parts.Add($"{g.Key} {all:F1}/{fee:F1}");
        }
        var shares = LoanShares.OrderBy(kv => kv.Key.Country).ThenBy(kv => kv.Key.Level).Where(kv => kv.Key.Level <= 2)
            .Select(kv => $"{kv.Key.Country}{kv.Key.Level} {kv.Value[^1]:P1}");
        Console.WriteLine($"Markt: Neuzugänge/Ablösetransfers je Verein der 1. Liga: {string.Join(", ", parts)}; Leihspieler am 1. März: {string.Join(", ", shares)}");
    }

    /// <summary>Mittel über alle Saisons gegen die Zielwerte; true, wenn alles im Rahmen liegt.</summary>
    public static bool PrintSummary(World w, GameData data)
    {
        var ok = true;
        var parts = new List<string>();
        static bool In(double v, double[] r) => v >= r[0] && v <= r[1];
        static string R(double[] r, string f) => $"{r[0].ToString(f, CultureInfo.InvariantCulture)}–{r[1].ToString(f, CultureInfo.InvariantCulture)}";
        foreach (var ((country, level), list) in LoanShares.OrderBy(kv => kv.Key.Country).ThenBy(kv => kv.Key.Level))
        {
            if (!w.Country(country).Market.LoanShare.TryGetValue(level, out var range)) continue;
            var mean = list.Average();
            var inside = In(mean * 100, range);
            ok &= inside;
            parts.Add($"{country}{level} {mean:P1} ({R(range, "0")} %){(inside ? "" : " ABWEICHUNG")}");
        }
        Console.WriteLine($"Ø Leihspieler am 1. März: {string.Join(", ", parts)}");

        var reference = data.TransferRules.Reference;
        parts.Clear();
        foreach (var (country, list) in TopSignings.OrderBy(kv => kv.Key))
        {
            var all = list.Average(x => x.All);
            var fee = list.Average(x => x.Fee);
            var inside = In(all, reference.NewSigningsPerClub) && In(fee, reference.FeeSigningsPerClub);
            ok &= inside;
            parts.Add($"{country} {all:F1}/{fee:F1}{(inside ? "" : " ABWEICHUNG")}");
        }
        Console.WriteLine($"Ø Neuzugänge/Ablösetransfers je Verein der 1. Liga (Ziel {R(reference.NewSigningsPerClub, "0.0")} / {R(reference.FeeSigningsPerClub, "0.0")}): {string.Join(", ", parts)}");

        if (FeeTransfers.Count >= 2)
        {
            var later = FeeTransfers.Skip(1).Average();
            var ratio = FeeTransfers[0] / Math.Max(1, later);
            var inside = ratio >= reference.SeasonOneMinRatio;
            ok &= inside;
            Console.WriteLine($"Ablösetransfers Saison 1: {FeeTransfers[0]}, Folgesaisons Ø {later:F0} (Verhältnis {ratio:P0}, mindestens {reference.SeasonOneMinRatio:P0}){(inside ? "" : " ABWEICHUNG")}");
        }
        return ok;
    }
}
