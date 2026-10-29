using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Pool unter der untersten Ebene (z. B. Oberligen): Die Absteiger der untersten Ebene fallen in den Pool, dafür steigen
/// ebenso viele Poolvereine auf. Der Pool spielt keine Liga; aufsteigen dürfen die stärksten Vereine mit Lizenz,
/// bevorzugt aus der Region des jeweiligen Absteigers, damit die Staffeln regional ausgeglichen bleiben.
/// </summary>
public static partial class Pyramid
{
    private static (PoolConfig pool, Exchange x)? PoolExchange(World world, CountryConfig country, List<League> lowest,
                                                               Dictionary<string, List<int>> tables, Rng rng, Dictionary<int, int> newLevels)
    {
        var pool = country.Pools.FirstOrDefault(p => p.Feeds is { } f && lowest.Any(l => l.Format.Id == f));
        if (pool is null) return null;
        var x = new Exchange();
        foreach (var league in lowest)
        {
            var table = tables[league.Id];
            x.Down.AddRange(Rules(league, MovementDirection.Down, world.SeasonYear).Where(p => p <= table.Count).Select(p => table[p - 1]));
            x.Down.AddRange(table.Where(id => world.Clubs[id].License.ForcedRelegation && !x.Down.Contains(id)));
        }
        // Rangfolge im Pool: Ruf plus Zufall (der Pool hat keine Tabelle); Reihenfolge der Ziehung nach Kennung.
        var level = lowest[0].Level;
        var candidates = world.Clubs.Values.Where(c => c.CountryCode == country.Code && c.LeagueId == pool.Id).OrderBy(c => c.Id)
            .Select(c => (club: c, score: c.Reputation + rng.NextDouble() * 4)).ToList()
            .Where(t => MayRise(world, country, t.club.Id, level, newLevels))
            .OrderByDescending(t => t.score).ThenBy(t => t.club.Id).Select(t => t.club).ToList();
        foreach (var down in x.Down)
        {
            var region = world.Clubs[down].Region;
            var pick = candidates.FirstOrDefault(c => c.Region == region) ?? candidates.FirstOrDefault();
            if (pick is null) break;
            x.Up.Add(pick.Id);
            candidates.Remove(pick);
        }
        // Ohne Nachrücker bleiben die bestplatzierten Absteiger in ihrer Liga.
        while (x.Down.Count > x.Up.Count) x.Down.RemoveAt(0);
        return (pool, x);
    }
}
