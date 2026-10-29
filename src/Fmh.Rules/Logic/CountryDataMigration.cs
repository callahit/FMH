using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Hebt die wirtschaftlichen Länderregeln eines geladenen Spielstands auf den aktuellen Datenstand (Review F-10):
/// Finanzen (TV, Sponsoren, Betriebskosten, Gehaltsniveaus, Ligazahlen), Kaderregeln und Nachwuchs. Ligaaufbau,
/// Pokale und Play-offs bleiben wie gespeichert, weil ein Wechsel mitten im Spielstand die Saisonübergänge stören würde.
/// </summary>
public static class CountryDataMigration
{
    /// <summary>Ersetzt abweichende Regeln durch die der Daten; gibt die Länder zurück, deren Regeln sich geändert haben.</summary>
    public static IReadOnlyList<string> Refresh(World world, GameData data)
    {
        var changed = new List<string>();
        foreach (var country in world.Countries)
        {
            var current = data.Countries.Where(c => c.Code == country.Code && c.ValidIn(world.SeasonYear))
                .OrderByDescending(c => c.ValidFrom).FirstOrDefault();
            if (current is null || ReferenceEquals(current, country)) continue;
            var differs = !Same(country.Finance, current.Finance) || !Same(country.Squad, current.Squad) || !Same(country.Youth, current.Youth);
            if (!differs) continue;
            country.Finance = current.Finance;
            country.Squad = current.Squad;
            country.Youth = current.Youth;
            changed.Add(country.Code);
        }
        return changed;
    }

    private static bool Same<T>(T a, T b) =>
        System.Text.Json.JsonSerializer.Serialize(a, Json.Compact) == System.Text.Json.JsonSerializer.Serialize(b, Json.Compact);
}
