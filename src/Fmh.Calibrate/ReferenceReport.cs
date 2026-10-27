using System.Globalization;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Calibrate;

/// <summary>
/// Vergleicht Erträge und Aufwand einer Saison je Spielklasse mit den veröffentlichten Ligazahlen aus den Daten
/// (<see cref="DivisionFinance.Reference"/>), ohne Transfers.
/// </summary>
public static class ReferenceReport
{
    /// <summary>Erlaubte Abweichung der Summen.</summary>
    public const double TotalTolerance = 0.25;
    /// <summary>Erlaubte Abweichung einzelner Kategorien.</summary>
    public const double CategoryTolerance = 0.4;

    /// <summary>Gibt den Vergleich aus; true, wenn alle Werte in der Toleranz liegen.</summary>
    public static bool Print(World w)
    {
        var ok = true;
        static string M(double v) => (v / 1_000_000).ToString("F1", CultureInfo.InvariantCulture);
        foreach (var div in w.Leagues.GroupBy(l => (l.CountryCode, l.Format.Id)))
        {
            var clubs = div.SelectMany(l => l.ClubIds).Select(id => w.Clubs[id]).ToList();
            if (ClubRules.DivisionFinance(w, clubs[0]).Reference is not { } r) continue;
            var actual = Actuals(clubs);
            var parts = new List<string>();
            void Check(string side, Dictionary<string, long> targets)
            {
                foreach (var (key, target) in targets)
                {
                    var value = actual.GetValueOrDefault(side + "." + key);
                    var tol = key == "total" ? TotalTolerance : CategoryTolerance;
                    var inside = Math.Abs(value / target - 1) <= tol;
                    ok &= inside;
                    parts.Add($"{key} {M(value)}/{M(target)}{(inside ? "" : " ABWEICHUNG")}");
                }
            }
            Check("income", r.Income);
            Check("expenses", r.Expenses);
            Console.WriteLine($"Ligazahlen {div.First().Format.Name} ({r.Source}{(r.Official ? "" : ", nicht amtlich")}), Spiel/Bericht in Mio.: {string.Join(", ", parts)}");
        }
        return ok;
    }

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
