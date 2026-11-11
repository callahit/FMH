using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Herausforderungen (AP 12a): Vereinsauswahl aus den Daten und Prüfung des Ziels. Saisonziele (Platz, Pokal,
/// Kontostand) werden am Saisonende geprüft, Ligaziele (Klassenerhalt, Aufstieg) beim Saisonwechsel an der neuen
/// Ligaebene. Ein erreichtes Ziel gewinnt sofort; nach der letzten Saison der Frist ist die Herausforderung verloren.
/// </summary>
public static class Challenges
{
    public static ChallengeRule? Find(GameData data, string id) => data.Challenges.Challenges.FirstOrDefault(c => c.Id == id);

    /// <summary>Verein der Herausforderung: nach Ruf in der Ebene des Landes (ohne zweite Mannschaften).</summary>
    public static ClubDefinition? PickClub(GameData data, ChallengeRule rule)
    {
        var country = data.Countries.Where(c => c.Code == rule.Country).OrderByDescending(c => c.ValidFrom).FirstOrDefault();
        var level = country?.Levels.FirstOrDefault(l => l.Level == rule.Level);
        if (level is null) return null;
        var ids = level.Divisions.SelectMany(d => d.Groups.Select(g => g.Id).Append(d.Id)).ToHashSet();
        var clubs = data.Clubs.Where(c => ids.Contains(c.Division) && c.ReserveOf is null)
            .OrderBy(c => c.Reputation).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
        if (clubs.Count == 0) return null;
        return rule.Pick switch
        {
            "strongest" => clubs[^1],
            "median" => clubs[clubs.Count / 2],
            _ => clubs[0],
        };
    }

    /// <summary>Letzte Saison der Frist.</summary>
    public static int LastSeason(GameModeState state, ChallengeRule rule) => state.StartSeason + rule.Seasons - 1;

    public static bool IsLeagueGoal(ChallengeRule rule) => rule.Goal.Type is "avoidRelegation" or "promotion";

    /// <summary>Saisonziel zum Saisonende <paramref name="season"/>: erreicht?</summary>
    public static bool SeasonGoalMet(World world, GameModeState state, ChallengeRule rule, int season)
    {
        var club = world.Clubs[state.ClubId];
        return rule.Goal.Type switch
        {
            "finishAtLeast" => world.ClubSeasons.Any(s => s.ClubId == club.Id && s.SeasonYear == season && s.Position <= rule.Goal.Value
                                                          && world.FindLeague(s.LeagueId) is { } l && l.Level <= state.StartLevel),
            "winCup" => world.History.Any(h => h.ChampionId == club.Id && h.SeasonYear >= state.StartSeason && h.SeasonYear <= season
                                               && world.FindLeague(h.LeagueId) is null),
            "balanceAtLeast" => club.Budget.Balance >= rule.Goal.Value,
            _ => false,
        };
    }

    /// <summary>Ligaebene des Vereins (1 = höchste Liga).</summary>
    public static int Level(World world, int clubId) =>
        world.Clubs.TryGetValue(clubId, out var c) && world.FindLeague(c.LeagueId) is { } l ? l.Level : int.MaxValue;
}
