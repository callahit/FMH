using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Stellt die beste verfügbare Elf und Bank für eine Formation auf (KI und "Automatisch aufstellen").</summary>
public static class LineupSelector
{
    /// <summary>Bewertung eines Spielers für einen Slot inkl. Kondition und Form.</summary>
    public static double SlotScore(Player p, Position pos, PositionProfiles profiles)
    {
        var rating = Ratings.EffectiveRating(p, pos, profiles);
        var condition = 0.75 + 0.25 * Math.Clamp(p.State.Condition, 0, 100) / 100.0;
        var form = 1 + (p.State.Form - 5) * 0.006;
        return rating * condition * form;
    }

    /// <summary>
    /// Ordnet Spieler den Slots zu. Torwart zuerst, danach die Slots mit den wenigsten passenden Kandidaten,
    /// damit seltene Positionen nicht leer ausgehen.
    /// </summary>
    public static List<int?> Assign(Formation formation, IReadOnlyCollection<Player> candidates, PositionProfiles profiles) =>
        Assign(formation, candidates, profiles, new SlotScores(profiles));

    /// <summary>Wie oben, mit gemeinsamem Zwischenspeicher der Slot-Bewertungen (z. B. beim Vergleich aller Formationen).</summary>
    public static List<int?> Assign(Formation formation, IReadOnlyCollection<Player> candidates, PositionProfiles profiles, SlotScores scores)
    {
        var result = new List<int?>(new int?[formation.Slots.Count]);
        var free = candidates.ToList();
        var rows = free.Select(scores.Row).ToList();

        var order = Enumerable.Range(0, formation.Slots.Count)
            .OrderBy(i => formation.Slots[i].Position == Position.GK ? 0 : 1)
            .ThenBy(i => scores.GoodCount(candidates, formation.Slots[i].Position))
            .ToList();

        foreach (var slot in order)
        {
            if (free.Count == 0) break;
            var pos = formation.Slots[slot].Position;
            // Wie MaxBy: bei Gleichstand gewinnt der erste Kandidat.
            var bestIndex = 0;
            var bestScore = double.MinValue;
            for (var k = 0; k < free.Count; k++)
            {
                var p = free[k];
                var score = scores.Get(rows[k], p, pos) - (pos != Position.GK && p.MainPosition == Position.GK ? 100 : 0);
                if (k == 0 || score > bestScore) { bestScore = score; bestIndex = k; }
            }
            result[slot] = free[bestIndex].Id;
            free.RemoveAt(bestIndex);
            rows.RemoveAt(bestIndex);
        }
        return result;
    }

    /// <summary>Komplette Aufstellung für einen Verein: Startelf, Bank (mit Ersatztorwart) und Standardschützen.</summary>
    public static void PickBest(Club club, World world, GameData data, int benchSize, SlotScores? scores = null, Func<Player, bool>? eligible = null)
    {
        scores ??= new SlotScores(data.PositionProfiles);
        var formation = TacticResolver.Resolve(club.Tactic, data);
        var available = world.SquadOf(club).Where(p => p.IsAvailable && (eligible is null || eligible(p))).ToList();
        club.Tactic.Lineup = Assign(formation, available, data.PositionProfiles, scores);
        club.Tactic.Bench = PickBench(club.Tactic.Lineup, available, benchSize, data.PositionProfiles, scores);
        PickSetPieceTakers(club, world);
    }

    public static List<int> PickBench(IEnumerable<int?> lineup, IEnumerable<Player> available, int benchSize, PositionProfiles profiles,
                                      SlotScores? scores = null)
    {
        scores ??= new SlotScores(profiles);
        var used = lineup.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var rest = available.Where(p => !used.Contains(p.Id)).ToList();
        var bench = new List<int>();
        var gk = rest.Where(p => p.MainPosition == Position.GK).MaxBy(p => scores.Get(p, Position.GK));
        if (gk is not null && benchSize > 0)
        {
            bench.Add(gk.Id);
            rest.Remove(gk);
        }
        bench.AddRange(rest.Where(p => p.MainPosition != Position.GK)
            .OrderByDescending(p => scores.Get(p, p.MainPosition))
            .Take(benchSize - bench.Count)
            .Select(p => p.Id));
        return bench;
    }

    public static void PickSetPieceTakers(Club club, World world)
    {
        var eleven = club.Tactic.Lineup.Where(id => id.HasValue).Select(id => world.Players[id!.Value]).ToList();
        if (eleven.Count == 0) return;
        var outfield = eleven.Where(p => p.MainPosition != Position.GK).ToList();
        if (outfield.Count == 0) outfield = eleven;
        club.Tactic.PenaltyTakerId = outfield.MaxBy(p => p.Attributes[Attr.Penalties] * 2 + p.Attributes[Attr.Composure])!.Id;
        club.Tactic.FreeKickTakerId = outfield.MaxBy(p => p.Attributes[Attr.FreeKicks] * 2 + p.Attributes[Attr.Technique])!.Id;
        club.Tactic.CornerTakerId = outfield.MaxBy(p => p.Attributes[Attr.Corners] * 2 + p.Attributes[Attr.Crossing])!.Id;
        if (club.Tactic.CaptainId is not { } cap || !eleven.Any(p => p.Id == cap))
            club.Tactic.CaptainId = eleven.MaxBy(p => p.Attributes[Attr.Leadership] * 2 + p.Reputation)!.Id;
    }

    /// <summary>
    /// Ersetzt nur die Lücken einer Aufstellung (verletzte, gesperrte, fehlende Spieler) durch den besten
    /// verfügbaren Ersatz und lässt alle übrigen Entscheidungen des Managers stehen.
    /// </summary>
    public static bool Repair(Club club, World world, GameData data, int benchSize, Func<Player, bool>? eligible = null)
    {
        var formation = TacticResolver.Resolve(club.Tactic, data);
        var lineup = club.Tactic.Lineup;
        var changed = false;
        while (lineup.Count < formation.Slots.Count) { lineup.Add(null); changed = true; }
        while (lineup.Count > formation.Slots.Count) { lineup.RemoveAt(lineup.Count - 1); changed = true; }

        bool Usable(int? id) => id is { } v && world.Players.TryGetValue(v, out var p) && p.ClubId == club.Id && p.IsAvailable
                                && (eligible is null || eligible(p));
        var seen = new HashSet<int>();
        for (var i = 0; i < lineup.Count; i++)
        {
            if (!Usable(lineup[i]) || !seen.Add(lineup[i]!.Value)) { lineup[i] = null; changed = true; }
        }

        var available = world.SquadOf(club).Where(p => p.IsAvailable && (eligible is null || eligible(p))).ToList();
        for (var i = 0; i < lineup.Count; i++)
        {
            if (lineup[i] is not null) continue;
            var used = lineup.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
            var pos = formation.Slots[i].Position;
            var best = available.Where(p => !used.Contains(p.Id))
                .OrderByDescending(p => SlotScore(p, pos, data.PositionProfiles) - (pos != Position.GK && p.MainPosition == Position.GK ? 100 : 0))
                .FirstOrDefault();
            lineup[i] = best?.Id;
        }

        var inXi = lineup.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var bench = club.Tactic.Bench.Where(id => Usable(id) && !inXi.Contains(id)).Distinct().Take(benchSize).ToList();
        if (bench.Count != club.Tactic.Bench.Count) changed = true;
        if (bench.Count < benchSize)
        {
            var extra = PickBench(lineup.Concat(bench.Select(b => (int?)b)), available, benchSize - bench.Count, data.PositionProfiles);
            if (extra.Count > 0) changed = true;
            bench.AddRange(extra);
        }
        club.Tactic.Bench = bench;
        PickSetPieceTakersIfMissing(club, world);
        return changed;
    }

    private static void PickSetPieceTakersIfMissing(Club club, World world)
    {
        var xi = club.Tactic.Lineup.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        if (club.Tactic.PenaltyTakerId is not { } a || !xi.Contains(a) ||
            club.Tactic.FreeKickTakerId is not { } b || !xi.Contains(b) ||
            club.Tactic.CornerTakerId is not { } c || !xi.Contains(c))
            PickSetPieceTakers(club, world);
    }

    /// <summary>Prüft eine Aufstellung: Liste von Text-Schlüsseln mit Problemen (leer = in Ordnung).</summary>
    public static List<string> Validate(Club club, World world, GameData data, Func<Player, bool>? eligible = null)
    {
        var problems = new List<string>();
        var formation = TacticResolver.Resolve(club.Tactic, data);
        var lineup = club.Tactic.Lineup;
        if (lineup.Count != formation.Slots.Count || lineup.Any(id => id is null))
            problems.Add("lineup.incomplete");
        var ids = lineup.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count) problems.Add("lineup.duplicate");
        foreach (var id in ids.Concat(club.Tactic.Bench))
        {
            if (!world.Players.TryGetValue(id, out var p) || p.ClubId != club.Id) { problems.Add("lineup.foreign"); break; }
        }
        if (ids.Any(id => world.Players.TryGetValue(id, out var p) && p.State.Injury is not null)) problems.Add("lineup.injured");
        if (ids.Any(id => world.Players.TryGetValue(id, out var p) && p.State.SuspendedMatches > 0)) problems.Add("lineup.suspended");
        if (club.Tactic.Bench.Any(id => world.Players.TryGetValue(id, out var p) && !p.IsAvailable)) problems.Add("lineup.benchUnavailable");
        if (club.Tactic.Bench.Any(ids.Contains)) problems.Add("lineup.duplicate");
        if (eligible is not null && ids.Concat(club.Tactic.Bench).Any(id => world.Players.TryGetValue(id, out var p) && !eligible(p)))
            problems.Add("lineup.reserveAge");
        return problems.Distinct().ToList();
    }
}

/// <summary>
/// Zwischenspeicher für <see cref="LineupSelector.SlotScore"/> je Spieler und Position. Gilt nur, solange sich
/// Attribute, Kondition und Form nicht ändern (also innerhalb einer Aufstellungsentscheidung).
/// </summary>
public sealed class SlotScores
{
    private static readonly int PositionCount = Enum.GetValues<Position>().Length;

    private readonly PositionProfiles _profiles;
    private readonly Dictionary<Player, double[]> _scores = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Position, int> _goodCounts = new();
    private object? _countsFor;

    public SlotScores(PositionProfiles profiles) => _profiles = profiles;

    public double Get(Player p, Position pos) => Get(Row(p), p, pos);

    /// <summary>Zeile der Bewertungen eines Spielers (für schnelle Zugriffe in Schleifen).</summary>
    public double[] Row(Player p)
    {
        if (!_scores.TryGetValue(p, out var row))
        {
            row = new double[PositionCount];
            Array.Fill(row, double.NaN);
            _scores[p] = row;
        }
        return row;
    }

    public double Get(double[] row, Player p, Position pos)
    {
        var s = row[(int)pos];
        if (double.IsNaN(s)) row[(int)pos] = s = LineupSelector.SlotScore(p, pos, _profiles);
        return s;
    }

    /// <summary>Wie viele Kandidaten eine Position mindestens gut spielen (für die Reihenfolge der Slots).</summary>
    public int GoodCount(IReadOnlyCollection<Player> candidates, Position pos)
    {
        if (!ReferenceEquals(_countsFor, candidates))
        {
            _goodCounts.Clear();
            _countsFor = candidates;
        }
        if (!_goodCounts.TryGetValue(pos, out var n))
            _goodCounts[pos] = n = candidates.Count(p => p.SuitabilityFor(pos) >= Suitability.Good);
        return n;
    }
}
