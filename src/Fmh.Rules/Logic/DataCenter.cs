using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Saisonstatistik eines Spielers für einen Verein.</summary>
public sealed record PlayerStatRow(Player Player, PlayerSeasonStats Stats);

/// <summary>Bilanz eines Vereins in einer Ligasaison (abgeschlossen oder laufend).</summary>
public sealed record TeamStatRow(int ClubId, int Position, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int Points);

/// <summary>Gegneranalyse: letzte Pflichtspiele, Bilanz daraus, Spielsystem, Stammkräfte und Torschützen der Saison.</summary>
public sealed class OpponentReport
{
    public int ClubId { get; init; }
    public IReadOnlyList<Fixture> LastMatches { get; init; } = Array.Empty<Fixture>();
    public int Won { get; init; }
    public int Drawn { get; init; }
    public int Lost { get; init; }
    public int GoalsFor { get; init; }
    public int GoalsAgainst { get; init; }
    public string FormationId { get; init; } = "";
    /// <summary>Spieler mit den meisten Pflichtspieleinsätzen der Saison.</summary>
    public IReadOnlyList<Player> Regulars { get; init; } = Array.Empty<Player>();
    /// <summary>Beste Torschützen der Saison (nur Spieler mit Toren).</summary>
    public IReadOnlyList<Player> Scorers { get; init; } = Array.Empty<Player>();
}

/// <summary>
/// Datenzentrale (AP 11c): Spieler- und Teamstatistiken je Liga und Saison, Gegneranalyse. Abgeschlossene Saisons
/// kommen aus den Saisonbilanzen (Zuordnung Verein → Liga), die laufende aus den aktuellen Ligen.
/// </summary>
public static class DataCenter
{
    /// <summary>Saisons mit Daten, neueste zuerst.</summary>
    public static IReadOnlyList<int> SeasonYears(World world) =>
        world.ClubSeasons.Select(s => s.SeasonYear).Append(world.SeasonYear).Distinct().OrderByDescending(y => y).ToList();

    /// <summary>Spielte der Verein in dieser Saison in dieser Liga?</summary>
    private static Func<int, bool> InLeague(World world, string leagueId, int seasonYear)
    {
        var past = world.ClubSeasons.Where(s => s.SeasonYear == seasonYear && s.LeagueId == leagueId).Select(s => s.ClubId).ToHashSet();
        if (past.Count > 0 || seasonYear != world.SeasonYear) return past.Contains;
        var current = world.FindLeague(leagueId)?.ClubIds.ToHashSet() ?? new HashSet<int>();
        return current.Contains;
    }

    /// <summary>Pflichtspielstatistik aller Spieler, die in der Saison für einen Verein der Liga gespielt haben.</summary>
    public static IReadOnlyList<PlayerStatRow> PlayerStats(World world, string leagueId, int seasonYear)
    {
        var inLeague = InLeague(world, leagueId, seasonYear);
        return world.Players.Values
            .SelectMany(p => RecordBook.Seasons(p)
                .Where(s => s.SeasonYear == seasonYear && s.Appearances > 0 && s.ClubId is { } c && inLeague(c))
                .Select(s => new PlayerStatRow(p, s)))
            .OrderBy(r => r.Player.Id).ToList();
    }

    /// <summary>Teambilanzen einer Ligasaison: abgeschlossene aus den Saisonbilanzen, die laufende aus der Tabelle.</summary>
    public static IReadOnlyList<TeamStatRow> TeamStats(World world, string leagueId, int seasonYear)
    {
        var past = world.ClubSeasons.Where(s => s.SeasonYear == seasonYear && s.LeagueId == leagueId).OrderBy(s => s.Position).ToList();
        if (past.Count > 0)
            return past.Select(s => new TeamStatRow(s.ClubId, s.Position, s.Played, s.Won, s.Drawn, s.Lost, s.GoalsFor, s.GoalsAgainst, s.Points)).ToList();
        if (seasonYear != world.SeasonYear || world.FindLeague(leagueId) is not { } league) return Array.Empty<TeamStatRow>();
        return LeagueTable.Compute(league)
            .Select(r => new TeamStatRow(r.ClubId, r.Position, r.Played, r.Won, r.Drawn, r.Lost, r.GoalsFor, r.GoalsAgainst, r.Points)).ToList();
    }

    /// <summary>Letzte gespielte Pflichtspiele eines Vereins, neuestes zuerst.</summary>
    public static IReadOnlyList<Fixture> LastMatches(World world, int clubId, int count) => world.AllFixtures
        .Where(f => f.Result is not null && f.Involves(clubId))
        .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id).Take(count).ToList();

    /// <summary>Gegner des nächsten Pflichtspiels; null ohne angesetztes Spiel.</summary>
    public static int? NextOpponent(World world, int clubId) => world.AllFixtures
        .Where(f => f.Result is null && f.Involves(clubId) && f.Date >= world.Date)
        .OrderBy(f => f.Date).ThenBy(f => f.Id)
        .Select(f => (int?)(f.HomeId == clubId ? f.AwayId : f.HomeId)).FirstOrDefault();

    public static OpponentReport Analyse(World world, Club club, int matches = 5)
    {
        var last = LastMatches(world, club.Id, matches);
        int won = 0, drawn = 0, lost = 0, gf = 0, ga = 0;
        foreach (var f in last)
        {
            var r = f.Result!;
            var (own, other) = f.HomeId == club.Id ? (r.HomeGoals, r.AwayGoals) : (r.AwayGoals, r.HomeGoals);
            gf += own;
            ga += other;
            // Entschieden im Elfmeterschießen zählt wie ein Unentschieden nach Verlängerung.
            if (own > other) won++;
            else if (own == other) drawn++;
            else lost++;
        }
        var squad = world.SquadOf(club).ToList();
        return new OpponentReport
        {
            ClubId = club.Id, LastMatches = last, Won = won, Drawn = drawn, Lost = lost, GoalsFor = gf, GoalsAgainst = ga,
            FormationId = club.Tactic.FormationId,
            Regulars = squad.Where(p => p.SeasonStats.Appearances > 0).OrderByDescending(p => p.SeasonStats.Appearances)
                .ThenByDescending(p => p.SeasonStats.Minutes).ThenBy(p => p.Id).Take(11).ToList(),
            Scorers = squad.Where(p => p.SeasonStats.Goals > 0).OrderByDescending(p => p.SeasonStats.Goals)
                .ThenBy(p => p.Id).Take(3).ToList(),
        };
    }
}
