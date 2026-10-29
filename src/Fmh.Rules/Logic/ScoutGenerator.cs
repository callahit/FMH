using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Scouts erzeugen (Spielstart, neue Station des Managers).</summary>
public static class ScoutGenerator
{
    /// <summary>Neue Scouts für einen Verein (Anzahl je Reputation).</summary>
    public static void CreateScouts(World world, Club club, GameData data, Rng rng, int count)
    {
        var names = data.Names.TryGetValue(club.CountryCode, out var n) ? n : data.Names.Values.First();
        for (var i = 0; i < count; i++)
        {
            var quality = Math.Clamp((int)Math.Round(6 + club.Reputation * 0.5 + rng.Normal(0, 2)), 3, 20);
            world.Scouts.Add(new Scout
            {
                Id = world.NextScoutId++, ClubId = club.Id,
                Name = $"{rng.Pick(names.FirstNames)} {rng.Pick(names.LastNames)}",
                JudgingAbility = quality, JudgingPotential = Math.Clamp(quality + rng.Next(-3, 3), 3, 20),
                RegionKnowledge = Math.Clamp(quality + rng.Next(-2, 4), 3, 20), Region = club.CountryCode,
            });
        }
    }
}
