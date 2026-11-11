using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Privatleben (AP 13b): Kosten, Prestige, Erholung und Käufe nach rules/privateLife.json.</summary>
public static class PrivateLives
{
    public const string Feature = "anstoss.privateLife";

    public enum Kind { Home, Car, Hobby }

    public static IReadOnlyList<PrivateItem> Items(PrivateLifeRules rules, Kind kind) => kind switch
    {
        Kind.Home => rules.Homes,
        Kind.Car => rules.Cars,
        _ => rules.Hobbies,
    };

    /// <summary>Alles, was der Manager gerade besitzt oder betreibt.</summary>
    public static IEnumerable<PrivateItem> Owned(PrivateLifeRules rules, PrivateLife life) =>
        rules.Homes.Where(h => h.Id == life.Home)
            .Concat(rules.Cars.Where(c => c.Id == life.Car))
            .Concat(rules.Hobbies.Where(h => life.Hobbies.Contains(h.Id)));

    public static long MonthlyCost(PrivateLifeRules rules, PrivateLife life) => Owned(rules, life).Sum(i => i.Monthly);
    public static double Prestige(PrivateLifeRules rules, PrivateLife life) => Owned(rules, life).Sum(i => i.Prestige);
    public static double Relief(PrivateLifeRules rules, PrivateLife life) => Owned(rules, life).Sum(i => i.Relief);

    /// <summary>Neues Privatleben: einfache Wohnung, einfaches Auto, Startvermögen aus dem Wochengehalt.</summary>
    public static PrivateLife Create(PrivateLifeRules rules, int weeklyWage) => new()
    {
        Savings = (long)Math.Round(weeklyWage * rules.StartSavingsWeeks),
        Home = rules.Homes.OrderBy(h => h.Price).FirstOrDefault()?.Id ?? "",
        Car = rules.Cars.OrderBy(c => c.Price).FirstOrDefault()?.Id ?? "",
    };

    /// <summary>Kaufpreis abzüglich Erlös des bisherigen Besitzes (Wohnung, Auto).</summary>
    public static long NetPrice(PrivateLifeRules rules, PrivateLife life, Kind kind, PrivateItem item)
    {
        var current = kind == Kind.Home ? life.Home : life.Car;
        var old = Items(rules, kind).FirstOrDefault(i => i.Id == current);
        return item.Price - (long)Math.Round((old?.Price ?? 0) * rules.ResaleShare);
    }

    /// <summary>Wohnung oder Auto wechseln, Hobby an- oder abmelden. Fehlertext-Schlüssel oder null.</summary>
    public static string? Change(PrivateLifeRules rules, PrivateLife life, Kind kind, string id)
    {
        if (Items(rules, kind).FirstOrDefault(i => i.Id == id) is not { } item) return "private.error.unknown";
        if (kind == Kind.Hobby)
        {
            if (life.Hobbies.Remove(id)) return null;
            if (life.Hobbies.Count >= rules.MaxHobbies) return "private.error.hobbies";
            if (life.Savings < item.Price) return "private.error.money";
            life.Savings -= item.Price;
            life.Hobbies.Add(id);
            return null;
        }
        if ((kind == Kind.Home ? life.Home : life.Car) == id) return "private.error.owned";
        var net = NetPrice(rules, life, kind, item);
        if (net > 0 && life.Savings < net) return "private.error.money";
        life.Savings -= net;
        if (kind == Kind.Home) life.Home = id;
        else life.Car = id;
        return null;
    }
}
