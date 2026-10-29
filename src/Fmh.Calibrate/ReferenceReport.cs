using System.Globalization;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Calibrate;

/// <summary>
/// Vergleicht Erträge und Aufwand einer Saison je Spielklasse mit den veröffentlichten Ligazahlen aus den Daten
/// (<see cref="DivisionFinance.Reference"/>), ohne Transfers. Geprüft wird der Durchschnitt aller bisher ausgewerteten
/// Saisons, weil auch die Ligazahlen Durchschnitte sind und ein einzelner reicher Absteiger eine Saison verzerrt.
/// </summary>
public static class ReferenceReport
{
    /// <summary>Erlaubte Abweichung der Summen (Review F-03).</summary>
    public const double TotalTolerance = 0.10;
    /// <summary>Erlaubte Abweichung einzelner Kategorien; begründete Ausnahmen stehen je Liga in den Daten.</summary>
    public const double CategoryTolerance = 0.20;

    /// <summary>Summe und Anzahl der Saisonwerte je Spielklasse und Kennzahl, dazu Ziel und Toleranz.</summary>
    private static readonly Dictionary<string, (double Sum, int Seasons, long Target, double Tolerance)> History = new();

    /// <summary>Toleranz einer Kennzahl („income.media“): Ausnahme aus den Daten oder Standard.</summary>
    public static double Tolerance(FinanceReference r, string side, string key) =>
        r.Tolerances.TryGetValue(side + "." + key, out var t) ? t : key == "total" ? TotalTolerance : CategoryTolerance;

    /// <summary>Gibt den Vergleich der Saison und den Durchschnitt aller bisher ausgewerteten Saisons aus.</summary>
    public static void Print(World w)
    {
        static string M(double v) => (v / 1_000_000).ToString("F1", CultureInfo.InvariantCulture);
        foreach (var div in w.Leagues.GroupBy(l => (l.CountryCode, l.Format.Id)))
        {
            var clubs = div.SelectMany(l => l.ClubIds).Select(id => w.Clubs[id]).ToList();
            if (ClubRules.DivisionFinance(w, clubs[0]).Reference is not { } r) continue;
            var actual = Actuals(clubs);
            var parts = new List<string>();
            var means = new List<string>();
            var seasons = 0;
            void Check(string side, Dictionary<string, long> targets)
            {
                foreach (var (key, target) in targets)
                {
                    var value = actual.GetValueOrDefault(side + "." + key);
                    var tol = Tolerance(r, side, key);
                    parts.Add($"{key} {M(value)}/{M(target)}{(Math.Abs(value / target - 1) <= tol ? "" : " abweichend")}");
                    var id = $"{div.Key.CountryCode}/{div.Key.Id}/{side}.{key}";
                    var (sum, n, _, _) = History.GetValueOrDefault(id);
                    History[id] = (sum + value, n + 1, target, tol);
                    seasons = n + 1;
                    var mean = (sum + value) / (n + 1);
                    means.Add($"{key} {M(mean)}{(Math.Abs(mean / target - 1) <= tol ? "" : " ABWEICHUNG")}");
                }
            }
            Check("income", r.Income);
            Check("expenses", r.Expenses);
            Console.WriteLine($"Ligazahlen {div.First().Format.Name} ({r.Source}{(r.Official ? "" : ", nicht amtlich")}), Spiel/Bericht in Mio.: {string.Join(", ", parts)}");
            Console.WriteLine($"  Ø {seasons} Saison(s): {string.Join(", ", means)}");
        }
    }

    /// <summary>
    /// Ergebnis des Laufs: Der Durchschnitt aller ausgewerteten Saisons jeder Kennzahl liegt in der Toleranz. Auch die
    /// Ligazahlen sind Durchschnitte; einzelne Saisons (etwa mit einem reichen Absteiger) weichen stärker ab.
    /// </summary>
    public static bool MeansOk() =>
        History.Values.All(h => Math.Abs(h.Sum / h.Seasons / h.Target - 1) <= h.Tolerance);

    /// <summary>Durchschnitt je Verein nach Kategorie, Erträge und Aufwand jeweils positiv.</summary>
    public static Dictionary<string, double> Actuals(IReadOnlyCollection<Club> clubs)
    {
        var sums = new Dictionary<string, double>();
        foreach (var (type, amount) in clubs.SelectMany(c => c.Finance.Season))
        {
            if (FinanceReference.Category(type) is not { } cat) continue;
            var side = FinanceReference.IncomeCategories.Contains(cat) ? "income" : "expenses";
            var value = side == "income" ? amount : -amount;
            sums[side + "." + cat] = sums.GetValueOrDefault(side + "." + cat) + value;
            sums[side + ".total"] = sums.GetValueOrDefault(side + ".total") + value;
        }
        return sums.ToDictionary(kv => kv.Key, kv => kv.Value / clubs.Count);
    }
}
