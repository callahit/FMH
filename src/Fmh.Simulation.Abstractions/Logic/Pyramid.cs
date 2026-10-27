using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Ligapyramide eines Landes (Umsetzungskonzept 6.1): Auf- und Abstieg über alle Ebenen nach den Bewegungsregeln,
/// mit Lizenzentscheidungen, Relegation, zweiten Mannschaften und Staffeln. Zu- und Abgänge je Ebene gleichen
/// sich immer aus, damit jede Liga ihre Teamzahl behält.
/// </summary>
public static partial class Pyramid
{
    /// <summary>Wechsel zwischen zwei benachbarten Ebenen: <c>Down</c> verlässt die obere, <c>Up</c> die untere Ebene.</summary>
    public sealed class Exchange
    {
        public List<int> Down { get; } = new();
        public List<int> Up { get; } = new();
        /// <summary>Absteiger, die nicht durch einen Aufsteiger ersetzt werden dürfen (Lizenz, zweite Mannschaft).</summary>
        public HashSet<int> Forced { get; } = new();
    }

    /// <summary>Ligen eines Landes nach Ebene; jede Ebene in Anlegereihenfolge.</summary>
    public static List<List<League>> Levels(World world, string countryCode) =>
        world.Leagues.Where(l => l.CountryCode == countryCode).GroupBy(l => l.Level).OrderBy(g => g.Key)
            .Select(g => g.ToList()).ToList();

    /// <summary>Ligen, die zu einer Division (alle Staffeln) oder genau einer Staffel gehören.</summary>
    public static IEnumerable<League> Matching(World world, string divisionOrLeague) =>
        world.Leagues.Where(l => l.Id == divisionOrLeague || l.Format.Id == divisionOrLeague);

    /// <summary>Direkte Auf- oder Abstiegszone eines Tabellenplatzes (für Anzeige und Saisonbilanz).</summary>
    public static MovementDirection? DirectZone(League league, int position, int seasonYear) =>
        league.Format.MovementsOf(league.Id).Where(m => m.Kind == MovementKind.Direct && m.AppliesTo(seasonYear))
            .FirstOrDefault(m => m.PositionList().Contains(position))?.To;

    /// <summary>Plant und vollzieht Auf- und Abstieg eines Landes zum Saisonwechsel.</summary>
    public static void PromoteAndRelegate(World world, CountryConfig country, Rng rng, Action<int> onRelegated)
    {
        var levels = Levels(world, country.Code);
        if (levels.Count == 0) return;
        var tables = levels.SelectMany(l => l).ToDictionary(l => l.Id, l => LeagueTable.Compute(l).Select(r => r.ClubId).ToList());
        // Zwei Durchgänge: Erst stehen die Ebenen der ersten Mannschaften fest, dann gelten die Regeln für zweite Mannschaften.
        var plan = Plan(world, country, levels, tables, null);
        var newLevels = NewLevels(world, levels, plan);
        plan = Plan(world, country, levels, tables, newLevels);
        var pool = PoolExchange(world, country, levels[^1], tables, rng, newLevels);
        Apply(world, country, levels, plan, pool, rng, onRelegated);
    }

    private static Dictionary<int, int> NewLevels(World world, List<List<League>> levels, List<Exchange> plan)
    {
        var result = new Dictionary<int, int>();
        for (var i = 0; i < levels.Count; i++)
            foreach (var l in levels[i])
                foreach (var id in l.ClubIds) result[id] = l.Level;
        for (var i = 0; i < plan.Count; i++)
        {
            foreach (var id in plan[i].Down) result[id] = levels[i + 1][0].Level;
            foreach (var id in plan[i].Up) result[id] = levels[i][0].Level;
        }
        return result;
    }

    /// <summary>Darf ein Verein in die Ebene <paramref name="level"/> aufsteigen (Lizenz, Abstand der zweiten Mannschaft)?</summary>
    private static bool MayRise(World world, CountryConfig country, int clubId, int level, Dictionary<int, int>? newLevels)
    {
        var club = world.Clubs[clubId];
        if (!club.License.PromotionAllowed) return false;
        return club.ReserveOf is not { } parent || newLevels is null || !newLevels.TryGetValue(parent, out var p)
               || level >= p + country.Reserves.MinLevelGap;
    }

    /// <summary>Zweite Mannschaft, die nach dem Abstieg der ersten zu hoch spielt, muss absteigen.</summary>
    private static bool MustDrop(World world, CountryConfig country, int clubId, int level, Dictionary<int, int>? newLevels) =>
        world.Clubs[clubId].ReserveOf is { } parent && newLevels is not null && newLevels.TryGetValue(parent, out var p)
        && level < p + country.Reserves.MinLevelGap;

    private static List<Exchange> Plan(World world, CountryConfig country, List<List<League>> levels,
                                       Dictionary<string, List<int>> tables, Dictionary<int, int>? newLevels)
    {
        var year = world.SeasonYear;
        var plan = new List<Exchange>();
        for (var i = 0; i + 1 < levels.Count; i++)
        {
            var x = new Exchange();
            var upperLevel = levels[i][0].Level;
            foreach (var league in levels[i])
            {
                var table = tables[league.Id];
                var rules = Rules(league, MovementDirection.Down, year);
                var down = rules.Where(p => p <= table.Count).Select(p => table[p - 1]).ToList();
                var count = down.Count;
                // Lizenz verweigert (oder zweite Mannschaft zu hoch): Zwangsabstieg; dafür bleibt der bestplatzierte sportliche Absteiger.
                foreach (var forced in table.Where(id => !down.Contains(id) &&
                             (world.Clubs[id].License.ForcedRelegation || MustDrop(world, country, id, upperLevel, newLevels))).ToList())
                {
                    down.Add(forced);
                    x.Forced.Add(forced);
                    if (down.Count > count && down.Count > 1 && !x.Forced.Contains(down[0])) down.RemoveAt(0);
                }
                x.Down.AddRange(down);
            }
            foreach (var league in levels[i + 1])
            {
                var table = tables[league.Id];
                var rules = Rules(league, MovementDirection.Up, year);
                if (rules.Count == 0) continue;
                // Wer keine Lizenz hat oder als zweite Mannschaft nicht aufsteigen darf, gibt den Platz an den Nächsten ab.
                x.Up.AddRange(table.Skip(rules.Min() - 1).Where(id => MayRise(world, country, id, upperLevel, newLevels)).Take(rules.Count));
            }
            AddPlayoffs(world, country, levels[i], levels[i + 1], x, upperLevel, newLevels);
            Balance(x, Successors(world, country, levels[i + 1], tables, upperLevel, newLevels));
            plan.Add(x);
        }
        return plan;
    }

    private static List<int> Rules(League league, MovementDirection to, int year) =>
        league.Format.MovementsOf(league.Id).Where(m => m.Kind == MovementKind.Direct && m.To == to && m.AppliesTo(year))
            .SelectMany(m => m.PositionList()).Distinct().Order().ToList();

    /// <summary>Relegation: Sieger aus der unteren Ebene steigen auf, unterlegene Teilnehmer der oberen ab.</summary>
    private static void AddPlayoffs(World world, CountryConfig country, List<League> upper, List<League> lower, Exchange x,
                                    int upperLevel, Dictionary<int, int>? newLevels)
    {
        bool InUpper(string? id) => id is not null && upper.Any(l => l.Id == id || l.Format.Id == id);
        bool InLower(string? id) => id is not null && lower.Any(l => l.Id == id || l.Format.Id == id);
        foreach (var po in world.Cups.Where(c => c.Kind == CompetitionKind.Playoff && c.CountryCode == country.Code &&
                                                 InUpper(c.UpperDivision) && InLower(c.LowerDivision) && c.WinnerId is not null))
        {
            var upperClubs = po.ParticipantIds.Where(id => upper.Any(l => l.ClubIds.Contains(id))).ToList();
            var winner = po.WinnerId!.Value;
            if (upperClubs.Contains(winner))
            {
                x.Down.AddRange(upperClubs.Where(id => id != winner && !x.Down.Contains(id)));
                continue;
            }
            if (!MayRise(world, country, winner, upperLevel, newLevels))
            {
                // Sieger darf nicht aufsteigen: der bestplatzierte Teilnehmer der oberen Ebene bleibt oben.
                x.Down.AddRange(upperClubs.Skip(1).Where(id => !x.Down.Contains(id)));
                continue;
            }
            x.Down.AddRange(upperClubs.Where(id => !x.Down.Contains(id)));
            if (!x.Up.Contains(winner)) x.Up.Add(winner);
        }
    }

    /// <summary>
    /// Gleicht Zu- und Abgänge aus: Überzählige sportliche Absteiger bleiben oben, danach rücken Vereine der unteren Ebene
    /// nach (bestplatzierte zuerst). Gibt es keinen Nachrücker, bleibt auch ein Zwangsabsteiger oben. Überzählige
    /// Aufsteiger bleiben unten.
    /// </summary>
    private static void Balance(Exchange x, IEnumerable<int> successors)
    {
        while (x.Down.Count > x.Up.Count && x.Down.FirstOrDefault(id => !x.Forced.Contains(id)) is var keep && keep != 0)
            x.Down.Remove(keep);
        using var next = successors.Where(id => !x.Up.Contains(id) && !x.Down.Contains(id)).GetEnumerator();
        while (x.Down.Count > x.Up.Count)
        {
            if (next.MoveNext()) x.Up.Add(next.Current);
            else x.Down.RemoveAt(x.Down.Count - 1);
        }
        while (x.Up.Count > x.Down.Count) x.Up.RemoveAt(x.Up.Count - 1);
    }

    /// <summary>Nachrücker der unteren Ebene: nach Tabellenplatz über alle Staffeln, nur wer aufsteigen darf.</summary>
    private static IEnumerable<int> Successors(World world, CountryConfig country, List<League> lower, Dictionary<string, List<int>> tables,
                                              int upperLevel, Dictionary<int, int>? newLevels) =>
        lower.SelectMany(l => tables[l.Id].Select((id, pos) => (id, pos))).OrderBy(t => t.pos).Select(t => t.id)
            .Where(id => MayRise(world, country, id, upperLevel, newLevels));
}
