using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Teilnehmer eines Pokals je Einstiegsrunde aus den Startplätzen (<see cref="CupConfig.Entries"/>) und dem Auffüllen aus
/// <see cref="CupConfig.Participants"/>, dazu der Rundenplan mit Freilosen.
/// </summary>
public static class CupEntries
{
    /// <summary>Vereine je Einstiegsrunde (Schlüssel 1 = erste Runde). Startplätze in Eintragsreihenfolge, ohne Doppelte.</summary>
    public static SortedDictionary<int, List<int>> Resolve(World world, CountryConfig country, CupConfig cfg)
    {
        var rounds = new SortedDictionary<int, List<int>> { [1] = new() };
        var taken = new HashSet<int>();
        bool Eligible(Club c) => c.CountryCode == country.Code && (c.ReserveOf is null || country.Reserves.PlayInCups)
                                 && (cfg.Region.Length == 0 || c.Region == cfg.Region);
        foreach (var entry in cfg.Entries)
        {
            var ids = Candidates(world, entry)
                .Where(id => world.Clubs.TryGetValue(id, out var c) && Eligible(c) && !taken.Contains(id))
                .Where(id => entry.Europe is not { } europe || world.EuropeanEntries.Any(e => e.ClubId == id && e.SeasonYear == world.SeasonYear) == europe)
                .Take(entry.Count ?? int.MaxValue).ToList();
            if (!rounds.TryGetValue(Math.Max(1, entry.Round), out var list)) rounds[Math.Max(1, entry.Round)] = list = new List<int>();
            list.AddRange(ids);
            taken.UnionWith(ids);
        }
        // Freie Plätze: höhere Ebenen zuerst, innerhalb einer Ebene nach Ruf (ohne Startplätze: wie bisher bis Teams).
        var open = cfg.Entries.Count == 0 ? Math.Max(cfg.Teams, 2) : cfg.Teams - taken.Count;
        if (open > 0 && cfg.Participants.Count > 0)
        {
            var fill = Members(world, cfg.Participants).Where(id => Eligible(world.Clubs[id]) && !taken.Contains(id)).Take(open);
            rounds[1].AddRange(fill);
        }
        // Ausgeloste Pokale in fester Reihenfolge der Vereins-Ids; feste Paarungen behalten die Reihenfolge der Einträge.
        if (!cfg.FixedPairing)
            foreach (var key in rounds.Keys.ToList()) rounds[key] = rounds[key].OrderBy(id => id).ToList();
        return rounds;
    }

    /// <summary>
    /// Geplante Teilnehmerzahl je Runde: Im Finale zwei, davor jeweils doppelt so viele Plätze wie Sieger plus Neueinsteiger
    /// der Folgerunde; die erste Runde muss alle Starter fassen. Ohne späteren Einstieg eine Zweierpotenz ab <paramref name="minimum"/>.
    /// </summary>
    public static List<int> PlanRounds(int firstRound, IReadOnlyDictionary<int, List<int>> entrants, int minimum)
    {
        var lastEntry = entrants.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).DefaultIfEmpty(1).Max();
        for (var rounds = Math.Max(1, lastEntry); rounds <= 16; rounds++)
        {
            var sizes = new int[rounds];
            sizes[rounds - 1] = 2;
            var valid = true;
            for (var r = rounds - 2; r >= 0 && valid; r--)
            {
                var joining = entrants.TryGetValue(r + 2, out var e) ? e.Count : 0;
                sizes[r] = 2 * (sizes[r + 1] - joining);
                valid = sizes[r + 1] - joining >= 1;
            }
            if (valid && sizes[0] >= Math.Max(firstRound, lastEntry == 1 ? minimum : 0)) return sizes.ToList();
        }
        throw new InvalidOperationException("Pokal: kein gültiger Rundenplan.");
    }

    /// <summary>Bestplatzierte eines Pokals: Sieger, Finalist, dann je Runde die Ausgeschiedenen (höhere Ebene, Ruf zuerst).</summary>
    public static List<int> Ranking(World world, Cup cup)
    {
        var order = new List<int>();
        if (cup.WinnerId is { } w) order.Add(w);
        foreach (var round in cup.Rounds.OrderByDescending(r => r.Number))
            order.AddRange(round.Ties.Where(t => t.WinnerId is not null && t.Decision != "Bye")
                .Select(t => t.WinnerId == t.HomeId ? t.AwayId : t.HomeId)
                .OrderBy(id => CupLogic.Level(world, id)).ThenByDescending(id => world.Clubs[id].Reputation).ThenBy(id => id));
        return order.Distinct().ToList();
    }

    private static IEnumerable<int> Candidates(World world, CupEntry entry)
    {
        if (entry.QualifiersOf.Length > 0)
            return world.History.FirstOrDefault(h => h.SeasonYear == world.SeasonYear - 1 && h.LeagueId == entry.QualifiersOf)?.FinalOrder ?? new List<int>();
        if (entry.PreviousSeason)
        {
            var records = world.History.Where(h => h.SeasonYear == world.SeasonYear - 1 && world.FindLeague(h.LeagueId) is { } l
                                                   && (entry.Participants.Contains(l.Id) || entry.Participants.Contains(l.Format.Id))).ToList();
            if (records.Count > 0) return records.SelectMany(h => h.FinalOrder);
        }
        return Members(world, entry.Participants);
    }

    /// <summary>Aktuelle Vereine der Ligen, Staffeln (über die Division) und Pools: höhere Ebene, höherer Ruf zuerst.</summary>
    private static IEnumerable<int> Members(World world, List<string> participants) =>
        world.Clubs.Values
            .Where(c => participants.Contains(c.LeagueId) || (world.FindLeague(c.LeagueId) is { } l && participants.Contains(l.Format.Id)))
            .OrderBy(c => c.Id)
            .OrderBy(c => CupLogic.Level(world, c.Id)).ThenByDescending(c => c.Reputation).ThenBy(c => c.Id)
            .Select(c => c.Id);
}
