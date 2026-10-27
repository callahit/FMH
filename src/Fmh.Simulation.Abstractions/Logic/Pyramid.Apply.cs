using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Vollzug des Plans: Vereine wechseln die Liga; geteilte Ebenen werden auf ihre Staffeln verteilt.</summary>
public static partial class Pyramid
{
    private static void Apply(World world, CountryConfig country, List<List<League>> levels, List<Exchange> plan, Rng rng,
                              Action<int> onRelegated)
    {
        // Lizenzflags gelten für die abgelaufene Saison; die Vereine unterhalb der obersten Ebene beginnen neu.
        foreach (var level in levels.Skip(1))
            foreach (var id in level.SelectMany(l => l.ClubIds))
                world.Clubs[id].License.ForcedRelegation = false;

        var arrivals = levels.Select(_ => new List<int>()).ToList();
        for (var i = 0; i < plan.Count; i++)
        {
            foreach (var id in plan[i].Down)
            {
                Move(world, id, levels[i + 1], arrivals[i + 1]);
                onRelegated(id);
            }
            foreach (var id in plan[i].Up) Move(world, id, levels[i], arrivals[i]);
        }
        for (var i = 0; i < levels.Count; i++)
            if (levels[i].Count > 1) AssignGroups(world, levels[i], arrivals[i], rng);
    }

    /// <summary>Verlässt die alte Liga; in eine ungeteilte Ebene sofort, in eine geteilte erst bei der Staffelzuteilung.</summary>
    private static void Move(World world, int clubId, List<League> target, List<int> arrivals)
    {
        var club = world.Clubs[clubId];
        world.FindLeague(club.LeagueId)?.ClubIds.Remove(clubId);
        if (target.Count == 1)
        {
            target[0].ClubIds.Add(clubId);
            club.LeagueId = target[0].Id;
        }
        else arrivals.Add(clubId);
    }

    /// <summary>Verteilt eine geteilte Ebene auf ihre Staffeln (fest, geografisch oder ausgelost).</summary>
    public static void AssignGroups(World world, List<League> groups, List<int> arrivals, Rng rng)
    {
        var assignment = groups[0].Format.Assignment;
        if (assignment == GroupAssignment.Fixed)
        {
            foreach (var id in arrivals) Place(world, groups, id);
            return;
        }
        var all = groups.SelectMany(g => g.ClubIds).Concat(arrivals).ToList();
        if (assignment == GroupAssignment.Geographic)
            all = all.OrderBy(id => world.Clubs[id].Longitude ?? 0).ThenBy(id => world.Clubs[id].Latitude ?? 0).ThenBy(id => id).ToList();
        else
        {
            all.Sort();
            rng.Shuffle(all);
        }
        foreach (var g in groups) g.ClubIds.Clear();
        var next = 0;
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var take = gi == groups.Count - 1 ? all.Count - next : Math.Min(groups[gi].Format.Teams, all.Count - next);
            foreach (var id in all.Skip(next).Take(take))
            {
                groups[gi].ClubIds.Add(id);
                world.Clubs[id].LeagueId = groups[gi].Id;
            }
            next += take;
        }
    }

    /// <summary>Feste Staffeln: Region der Staffel, sonst die nächstgelegene, sonst die mit den meisten freien Plätzen.</summary>
    private static void Place(World world, List<League> groups, int clubId)
    {
        var club = world.Clubs[clubId];
        var free = groups.Where(g => g.ClubIds.Count < g.Format.Teams).ToList();
        if (free.Count == 0) free = groups;
        var target = free.FirstOrDefault(g => club.Region is { } r && GroupRegions(g).Contains(r))
                     ?? (club.Longitude is { } lon && club.Latitude is { } lat
                         ? free.OrderBy(g => Distance(world, g, lat, lon)).First()
                         : free.OrderBy(g => g.ClubIds.Count).First());
        target.ClubIds.Add(clubId);
        club.LeagueId = target.Id;
    }

    private static IEnumerable<string> GroupRegions(League group) =>
        group.Format.Groups.FirstOrDefault(g => g.Id == group.Id)?.Regions ?? Enumerable.Empty<string>();

    private static double Distance(World world, League group, double lat, double lon)
    {
        var located = group.ClubIds.Select(id => world.Clubs[id]).Where(c => c.Latitude is not null && c.Longitude is not null).ToList();
        if (located.Count == 0) return double.MaxValue / 2;
        var cLat = located.Average(c => c.Latitude!.Value);
        var cLon = located.Average(c => c.Longitude!.Value);
        return (cLat - lat) * (cLat - lat) + (cLon - lon) * (cLon - lon);
    }
}
