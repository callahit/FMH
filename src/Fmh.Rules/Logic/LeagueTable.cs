using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public sealed class TableRow
{
    public int ClubId { get; init; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int AwayGoals { get; set; }
    public int Points { get; set; }
    /// <summary>Abgezogene Punkte (Lizenzierung).</summary>
    public int Deducted { get; set; }
    public int Position { get; set; }
    /// <summary>Letzte Ergebnisse als S/U/N, neuestes zuletzt.</summary>
    public List<char> Form { get; } = new();

    public int GoalDifference => GoalsFor - GoalsAgainst;
}

/// <summary>Tabellenberechnung mit frei konfigurierbaren Kriterien (Kapitel 3).</summary>
public static class LeagueTable
{
    public static List<TableRow> Compute(League league, int? upToMatchday = null)
    {
        if (league.Format.Phases.Count > 0 && league.PhasesScheduled > 0 && (upToMatchday is null || upToMatchday > league.RegularMatchdays))
        {
            var table = Compute(league, league.RegularMatchdays);
            for (var i = 0; i < league.PhasesScheduled && i < league.Format.Phases.Count; i++)
            {
                if (!league.Fixtures.Any(f => InPhase(f, league, i + 1))) break;
                if (upToMatchday is not null && !league.Fixtures.Any(f => InPhase(f, league, i + 1) && f.Matchday <= upToMatchday)) break;
                table = ApplyPhase(league, league.Format.Phases[i], i + 1, table, upToMatchday);
            }
            return table;
        }
        var played = league.Fixtures
            .Where(f => f.Result is not null && (upToMatchday is null || f.Matchday <= upToMatchday))
            .OrderBy(f => f.Matchday)
            .ToList();
        var rows = Accumulate(league.ClubIds, played, league.Format.Points);
        foreach (var (club, points) in league.Deductions)
            if (rows.TryGetValue(club, out var row)) { row.Points -= points; row.Deducted = points; }
        var sorted = Sort(rows.Values.ToList(), played, league.Format);
        for (var i = 0; i < sorted.Count; i++) sorted[i].Position = i + 1;
        return sorted;
    }

    /// <summary>Spiel der Folgephase <paramref name="phaseNumber"/> (1 = erste). Ältere Spielstände ohne Nummer zählen als Phase 1.</summary>
    private static bool InPhase(Fixture f, League league, int phaseNumber) =>
        f.Phase == phaseNumber || (phaseNumber == 1 && f.Phase == 0 && f.Matchday > league.RegularMatchdays);

    /// <summary>
    /// Eine Folgephase: Die Gruppen stehen nach ihren Plätzen der vorherigen Tabelle hintereinander. Je Gruppe zählen die
    /// übernommenen Punkte plus die Punkte dieser Phase; Tore und Bilanz laufen über alle bisherigen Spiele.
    /// </summary>
    private static List<TableRow> ApplyPhase(League league, PhaseConfig phase, int phaseNumber, List<TableRow> basis, int? upToMatchday)
    {
        var played = league.Fixtures
            .Where(f => f.Result is not null && (upToMatchday is null || f.Matchday <= upToMatchday))
            .OrderBy(f => f.Matchday)
            .ToList();
        var season = Accumulate(league.ClubIds, played, league.Format.Points);
        var result = new List<TableRow>();
        foreach (var group in phase.Groups)
        {
            var ids = group.PositionList().Where(p => p <= basis.Count).Select(p => basis[p - 1].ClubId).ToList();
            var idSet = ids.ToHashSet();
            var phaseFixtures = played.Where(f => InPhase(f, league, phaseNumber) && idSet.Contains(f.HomeId) && idSet.Contains(f.AwayId)).ToList();
            var phasePoints = Accumulate(ids, phaseFixtures, league.Format.Points);
            foreach (var id in ids)
            {
                var row = season[id];
                var before = basis.First(r => r.ClubId == id);
                row.Points = phase.Carry(before.Points) + phasePoints[id].Points;
                row.Deducted = before.Deducted;
            }
            result.AddRange(Sort(ids.Select(id => season[id]).ToList(), played, league.Format));
        }
        for (var i = 0; i < result.Count; i++) result[i].Position = i + 1;
        return result;
    }

    private static Dictionary<int, TableRow> Accumulate(IEnumerable<int> clubIds, IEnumerable<Fixture> fixtures, PointsConfig points)
    {
        var rows = clubIds.ToDictionary(id => id, id => new TableRow { ClubId = id });
        foreach (var f in fixtures)
        {
            if (!rows.TryGetValue(f.HomeId, out var h) || !rows.TryGetValue(f.AwayId, out var a)) continue;
            var r = f.Result!;
            h.Played++; a.Played++;
            h.GoalsFor += r.HomeGoals; h.GoalsAgainst += r.AwayGoals;
            a.GoalsFor += r.AwayGoals; a.GoalsAgainst += r.HomeGoals;
            a.AwayGoals += r.AwayGoals;
            if (r.HomeGoals > r.AwayGoals) { h.Won++; a.Lost++; h.Points += points.Win; a.Points += points.Loss; h.Form.Add('S'); a.Form.Add('N'); }
            else if (r.HomeGoals < r.AwayGoals) { a.Won++; h.Lost++; a.Points += points.Win; h.Points += points.Loss; a.Form.Add('S'); h.Form.Add('N'); }
            else { h.Drawn++; a.Drawn++; h.Points += points.Draw; a.Points += points.Draw; h.Form.Add('U'); a.Form.Add('U'); }
        }
        foreach (var row in rows.Values)
            while (row.Form.Count > 5) row.Form.RemoveAt(0);
        return rows;
    }

    private static List<TableRow> Sort(List<TableRow> rows, List<Fixture> fixtures, DivisionConfig format)
    {
        var result = new List<TableRow>();
        foreach (var group in rows.GroupBy(r => r.Points).OrderByDescending(g => g.Key))
            result.AddRange(SortTied(group.ToList(), fixtures, format, 0));
        return result;
    }

    /// <summary>Sortiert punktgleiche Teams rekursiv nach den Kriterien in der konfigurierten Reihenfolge.</summary>
    private static List<TableRow> SortTied(List<TableRow> tied, List<Fixture> fixtures, DivisionConfig format, int criterion)
    {
        if (tied.Count <= 1) return tied;
        if (criterion >= format.TieBreakers.Count)
            return tied.OrderBy(r => r.ClubId).ToList();

        Func<TableRow, double> key;
        var tb = format.TieBreakers[criterion];
        if (tb == TieBreaker.HeadToHead)
        {
            var ids = tied.Select(r => r.ClubId).ToHashSet();
            var mini = Accumulate(ids, fixtures.Where(f => ids.Contains(f.HomeId) && ids.Contains(f.AwayId)), format.Points);
            key = r => mini[r.ClubId].Points * 10000.0 + mini[r.ClubId].GoalDifference * 100.0 + mini[r.ClubId].GoalsFor;
        }
        else
        {
            key = tb switch
            {
                TieBreaker.GoalDifference => r => r.GoalDifference,
                TieBreaker.GoalsFor => r => r.GoalsFor,
                TieBreaker.AwayGoals => r => r.AwayGoals,
                TieBreaker.Wins => r => r.Won,
                _ => _ => 0,
            };
        }

        var result = new List<TableRow>();
        foreach (var g in tied.GroupBy(key).OrderByDescending(g => g.Key))
            result.AddRange(SortTied(g.ToList(), fixtures, format, criterion + 1));
        return result;
    }
}
