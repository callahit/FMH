using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Tabellenzeile einer Turniergruppe.</summary>
public sealed record TournamentRow(string Code, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst)
{
    public int Points => Won * 3 + Drawn;
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public double PointsPerGame => Played == 0 ? 0 : Points / (double)Played;
}

/// <summary>
/// Turniere der Nationalteams (AP 8b): Auslosung nach Stärke, Spielpläne, Gruppentabellen, Qualifikanten und
/// K.-o.-Paarungen. Reine Rechenregeln; Zeitpunkt und Spielbetrieb steuert das Modul.
/// </summary>
public static class InternationalTournaments
{
    public static bool IsTournamentYear(TournamentRule rule, int year) =>
        year >= rule.FirstYear && (year - rule.FirstYear) % rule.Every == 0;

    public static string Key(TournamentRule rule, int year) => $"{rule.Id}-{year}";

    /// <summary>Spielstärke eines Nationalteams: Ø der elf Besten (Hintergrundländer: feste Stärke).</summary>
    public static double Rating(World world, GameData data, NationalTeam team)
    {
        if (team.Background) return team.Strength;
        var best = world.Players.Values.Where(p => NationalSquads.Eligible(p, team.Code, world, data))
            .Select(p => NationalSquads.Score(p, data)).OrderByDescending(x => x).Take(11).ToList();
        return best.Count == 0 ? 0 : best.Average();
    }

    /// <summary>
    /// Auslosung in <paramref name="groups"/> Gruppen: Töpfe nach Stärke (je Topf so viele Teams wie Gruppen),
    /// innerhalb eines Topfs entscheidet der Zufall über die Gruppe.
    /// </summary>
    public static List<TournamentGroup> Draw(IEnumerable<(string Code, double Rating)> teams, int groups, Rng rng)
    {
        var result = Enumerable.Range(0, groups).Select(i => new TournamentGroup { Name = ((char)('A' + i)).ToString() }).ToList();
        var seeded = teams.OrderByDescending(t => t.Rating).ThenBy(t => t.Code, StringComparer.Ordinal).Select(t => t.Code).ToList();
        for (var pot = 0; pot * groups < seeded.Count; pot++)
        {
            var members = seeded.Skip(pot * groups).Take(groups).ToList();
            var slots = Enumerable.Range(0, groups).ToList();
            foreach (var code in members)
            {
                var i = rng.Next(slots.Count);
                result[slots[i]].Codes.Add(code);
                slots.RemoveAt(i);
            }
        }
        return result;
    }

    /// <summary>Spielplan jeder gegen jeden (Kreismethode), auf Wunsch mit Rückrunde; spielfreie Teams fehlen.</summary>
    public static List<List<(string Home, string Away)>> RoundRobin(IReadOnlyList<string> codes, bool twice)
    {
        var list = codes.Cast<string?>().ToList();
        if (list.Count % 2 == 1) list.Add(null);
        var n = list.Count;
        var rounds = new List<List<(string, string)>>();
        for (var r = 0; r < n - 1; r++)
        {
            var round = new List<(string, string)>();
            for (var i = 0; i < n / 2; i++)
            {
                var a = list[i];
                var b = list[n - 1 - i];
                if (a is null || b is null) continue;
                round.Add(r % 2 == 0 ? (a, b) : (b, a));
            }
            rounds.Add(round);
            // Erstes Team bleibt stehen, die übrigen rücken eine Position weiter.
            var last = list[n - 1];
            list.RemoveAt(n - 1);
            list.Insert(1, last);
        }
        if (twice) rounds.AddRange(rounds.ToList().Select(r => r.Select(m => (m.Item2, m.Item1)).ToList()));
        return rounds;
    }

    /// <summary>Gruppentabelle: Punkte, Tordifferenz, Tore, dann Ländercode (feste Reihenfolge).</summary>
    public static List<TournamentRow> Standings(IEnumerable<InternationalFixture> fixtures, TournamentGroup group)
    {
        var rows = group.Codes.ToDictionary(c => c, c => new TournamentRow(c, 0, 0, 0, 0, 0, 0));
        foreach (var f in fixtures)
        {
            if (f.Result is not { } r || !rows.ContainsKey(f.HomeCode) || !rows.ContainsKey(f.AwayCode)) continue;
            rows[f.HomeCode] = Add(rows[f.HomeCode], r.HomeGoals, r.AwayGoals);
            rows[f.AwayCode] = Add(rows[f.AwayCode], r.AwayGoals, r.HomeGoals);
        }
        return Order(rows.Values).ToList();
    }

    private static TournamentRow Add(TournamentRow row, int goalsFor, int goalsAgainst) => row with
    {
        Played = row.Played + 1,
        Won = row.Won + (goalsFor > goalsAgainst ? 1 : 0),
        Drawn = row.Drawn + (goalsFor == goalsAgainst ? 1 : 0),
        Lost = row.Lost + (goalsFor < goalsAgainst ? 1 : 0),
        GoalsFor = row.GoalsFor + goalsFor,
        GoalsAgainst = row.GoalsAgainst + goalsAgainst,
    };

    private static IOrderedEnumerable<TournamentRow> Order(IEnumerable<TournamentRow> rows) => rows
        .OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalDifference).ThenByDescending(r => r.GoalsFor)
        .ThenBy(r => r.Code, StringComparer.Ordinal);

    /// <summary>
    /// Qualifikanten: je Gruppe gleich viele Plätze, Restplätze an die besten Nächstplatzierten (Punkte je Spiel,
    /// Tordifferenz, Tore, Code), damit ungleich große Gruppen fair verglichen werden.
    /// </summary>
    public static List<string> Qualified(IReadOnlyList<List<TournamentRow>> tables, int places)
    {
        var perGroup = Math.Min(places / tables.Count, tables.Max(t => t.Count));
        var result = tables.SelectMany(t => t.Take(perGroup)).Select(r => r.Code).ToList();
        var rest = tables.SelectMany(t => t.Skip(perGroup).Take(1))
            .OrderByDescending(r => r.PointsPerGame).ThenByDescending(r => r.GoalDifference).ThenByDescending(r => r.GoalsFor)
            .ThenBy(r => r.Code, StringComparer.Ordinal);
        result.AddRange(rest.Take(Math.Max(0, places - result.Count)).Select(r => r.Code));
        return result;
    }

    /// <summary>
    /// Reihenfolge der K.-o.-Teilnehmer, je zwei Nachbarn spielen gegeneinander: Gruppensieger gegen Zweite der
    /// Nachbargruppe (A1–B2, B1–A2, C1–D2 …); bei nur einer Gruppe 1–4, 2–3 bzw. 1–2.
    /// </summary>
    public static List<string> KnockoutEntrants(IReadOnlyList<List<TournamentRow>> tables, int advance)
    {
        if (tables.Count == 1)
        {
            var t = tables[0].Take(advance).Select(r => r.Code).ToList();
            return t.Count == 4 ? new List<string> { t[0], t[3], t[1], t[2] } : t;
        }
        var result = new List<string>();
        for (var g = 0; g + 1 < tables.Count; g += 2)
        {
            var a = tables[g];
            var b = tables[g + 1];
            result.AddRange(new[] { a[0].Code, b[1].Code, b[0].Code, a[1].Code });
        }
        return result;
    }

    /// <summary>Daten sind stimmig: Gruppen mal Weiterkommende eine Zweierpotenz, Qualifikation passt in die Fenster.</summary>
    public static IEnumerable<string> Validate(InternationalRules rules)
    {
        var matchDays = rules.Windows.Sum(w => w.MatchOffsets.Count(o => o < w.Days));
        foreach (var t in rules.Tournaments)
        {
            var groups = t.Teams / t.GroupSize;
            var ko = groups * t.Advance;
            if (t.Teams % t.GroupSize != 0) yield return $"{t.Id}: teams {t.Teams} nicht durch groupSize {t.GroupSize} teilbar";
            if (ko < 2 || (ko & (ko - 1)) != 0) yield return $"{t.Id}: {ko} K.-o.-Teilnehmer sind keine Zweierpotenz";
            if (groups > 1 && (groups % 2 != 0 || t.Advance != 2)) yield return $"{t.Id}: mehrere Gruppen brauchen eine gerade Zahl und advance 2";
            if (t.Advance > t.GroupSize) yield return $"{t.Id}: advance größer als groupSize";
            if (2 * (t.QualificationGroupSize - 1) > matchDays) yield return $"{t.Id}: Qualifikation braucht mehr als {matchDays} Spieltage";
        }
    }
}
