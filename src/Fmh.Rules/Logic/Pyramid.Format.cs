using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Formatversionen eines Landes: Beim Saisonwechsel gilt die Version, deren Spanne die neue Saison enthält.</summary>
public static partial class Pyramid
{
    /// <summary>Wechselt für jedes Land auf die gültige Formatversion und baut die Ligen um. true, wenn sich etwas geändert hat.</summary>
    public static bool SwitchFormat(World world, GameData data, Rng rng)
    {
        var changed = false;
        for (var i = 0; i < world.Countries.Count; i++)
        {
            var current = world.Countries[i];
            var next = data.Countries.Where(c => c.Code == current.Code && c.ValidIn(world.SeasonYear))
                .OrderByDescending(c => c.ValidFrom).FirstOrDefault();
            if (next is null || next.ValidFrom == current.ValidFrom) continue;
            Restructure(world, next, rng);
            world.Countries[i] = next;
            changed = true;
        }
        return changed;
    }

    /// <summary>Neue Ligen je Ebene anlegen; die Vereine jeder Ebene werden auf die neuen Ligen bzw. Staffeln verteilt.</summary>
    private static void Restructure(World world, CountryConfig next, Rng rng)
    {
        var byLevel = Levels(world, next.Code).ToDictionary(l => l[0].Level, l => l.SelectMany(x => x.ClubIds).ToList());
        world.Leagues.RemoveAll(l => l.CountryCode == next.Code);
        foreach (var level in next.Levels.OrderBy(l => l.Level))
        {
            var leagues = level.Divisions.SelectMany(div => div.LeagueIds.Select(id => new League
            {
                Id = id,
                Name = div.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? div.Name,
                CountryCode = next.Code,
                Level = level.Level,
                Format = div,
            })).ToList();
            world.Leagues.AddRange(leagues);
            var clubs = byLevel.GetValueOrDefault(level.Level) ?? new List<int>();
            if (leagues.Count == 1)
            {
                leagues[0].ClubIds.AddRange(clubs);
                foreach (var id in clubs) world.Clubs[id].LeagueId = leagues[0].Id;
            }
            else AssignGroups(world, leagues, clubs, rng);
        }
    }
}
