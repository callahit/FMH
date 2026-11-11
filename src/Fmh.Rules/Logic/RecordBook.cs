using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Erfolge eines Vereins in einem Wettbewerb.</summary>
public sealed record Honour(string CompetitionId, string CompetitionName, IReadOnlyList<int> Years)
{
    public int Count => Years.Count;
}

/// <summary>Spieler mit Summe über alle Saisons (Tore oder Einsätze).</summary>
public sealed record PlayerTotal(int PlayerId, int Value);

/// <summary>Beste Einzelsaison eines Spielers.</summary>
public sealed record PlayerSeasonBest(int PlayerId, int ClubId, int SeasonYear, int Value);

/// <summary>
/// Rekordbuch (AP 11b): schreibt Spielrekorde nach jedem Pflichtspiel fort und leitet Vereins- und Ligarekorde aus
/// Meisterchronik, Vereinssaisons, Spielerstatistiken und Transfers ab. Ein Rekord wird nur durch ein echt besseres
/// Spiel abgelöst; bei Gleichstand bleibt der ältere stehen.
/// </summary>
public static class RecordBook
{
    /// <summary>Spielrekorde nach einem Pflichtspiel fortschreiben (Verein beider Seiten und Wettbewerb).</summary>
    public static void Update(World world, Fixture f, int seasonYear)
    {
        if (f.Result is not { } r || f.Kind == CompetitionKind.Friendly) return;
        foreach (var kind in RecordKinds.All)
        {
            Consider(world, MatchRecord.ClubScope(f.HomeId), kind, f, r, seasonYear, f.HomeId);
            Consider(world, MatchRecord.ClubScope(f.AwayId), kind, f, r, seasonYear, f.AwayId);
            if (kind != RecordKinds.BiggestDefeat)
                Consider(world, MatchRecord.CompetitionScope(f.CompetitionId), kind, f, r, seasonYear, null);
        }
    }

    private static void Consider(World world, string scope, string kind, Fixture f, MatchResult r, int seasonYear, int? clubId)
    {
        var score = Score(kind, f.HomeId, r.HomeGoals, r.AwayGoals, r.Attendance, clubId);
        if (score is null) return;
        var key = MatchRecord.Key(scope, kind);
        if (world.Records.TryGetValue(key, out var old)
            && Compare(score.Value, Score(kind, old.HomeId, old.HomeGoals, old.AwayGoals, old.Attendance, clubId)) <= 0) return;
        world.Records[key] = new MatchRecord
        {
            Scope = scope, Kind = kind, SeasonYear = seasonYear, Date = f.Date, CompetitionId = f.CompetitionId,
            HomeId = f.HomeId, AwayId = f.AwayId, HomeGoals = r.HomeGoals, AwayGoals = r.AwayGoals, Attendance = r.Attendance,
        };
    }

    /// <summary>Wertung eines Spiels für eine Rekordart; null, wenn das Spiel nicht in Frage kommt.</summary>
    private static (int Main, int Tie)? Score(string kind, int homeId, int homeGoals, int awayGoals, int attendance, int? clubId)
    {
        var own = clubId is null ? Math.Max(homeGoals, awayGoals) : clubId == homeId ? homeGoals : awayGoals;
        var other = clubId is null ? Math.Min(homeGoals, awayGoals) : clubId == homeId ? awayGoals : homeGoals;
        return kind switch
        {
            RecordKinds.BiggestWin when own > other => (own - other, own),
            RecordKinds.BiggestDefeat when other > own => (other - own, other),
            RecordKinds.MostGoals when homeGoals + awayGoals > 0 => (homeGoals + awayGoals, 0),
            RecordKinds.Attendance when attendance > 0 => (attendance, 0),
            _ => null,
        };
    }

    private static int Compare((int Main, int Tie) a, (int Main, int Tie)? b) =>
        b is not { } o ? 1 : a.Main != o.Main ? a.Main.CompareTo(o.Main) : a.Tie.CompareTo(o.Tie);

    public static MatchRecord? Find(World world, string scope, string kind) =>
        world.Records.GetValueOrDefault(MatchRecord.Key(scope, kind));

    /// <summary>Abschlussbilanzen aller Vereine einer Liga zum Saisonende ablegen.</summary>
    public static void ArchiveSeason(World world, League league, IReadOnlyList<TableRow> table, int seasonYear)
    {
        world.ClubSeasons.RemoveAll(s => s.SeasonYear == seasonYear && s.LeagueId == league.Id);
        world.ClubSeasons.AddRange(table.Select(row => new ClubSeason
        {
            ClubId = row.ClubId, SeasonYear = seasonYear, LeagueId = league.Id, Position = row.Position, Played = row.Played,
            Won = row.Won, Drawn = row.Drawn, Lost = row.Lost, GoalsFor = row.GoalsFor, GoalsAgainst = row.GoalsAgainst, Points = row.Points,
        }));
    }

    /// <summary>Titel eines Vereins je Wettbewerb (Meisterschaften, Pokale, Europapokale), meiste zuerst.</summary>
    public static IReadOnlyList<Honour> Honours(World world, int clubId) => world.History
        .Where(h => h.ChampionId == clubId)
        .GroupBy(h => h.LeagueId)
        .Select(g => new Honour(g.Key, g.Last().LeagueName, g.Select(h => h.SeasonYear).OrderBy(y => y).ToList()))
        .OrderByDescending(h => h.Count).ThenBy(h => h.CompetitionId, StringComparer.Ordinal).ToList();

    /// <summary>Meisterchronik eines Wettbewerbs: Vereine nach Zahl der Titel.</summary>
    public static IReadOnlyList<(int ClubId, string Name, int Titles)> TitleTable(World world, string competitionId) => world.History
        .Where(h => h.LeagueId == competitionId)
        .GroupBy(h => h.ChampionId)
        .Select(g => (g.Key, world.Clubs.TryGetValue(g.Key, out var c) ? c.Name : g.Last().ChampionName, g.Count()))
        .OrderByDescending(t => t.Item3).ThenBy(t => t.Item2, StringComparer.Ordinal).ToList();

    /// <summary>Alle Saisonstatistiken eines Spielers einschließlich der laufenden.</summary>
    public static IEnumerable<PlayerSeasonStats> Seasons(Player p) =>
        p.SeasonStats.Appearances > 0 ? p.History.Append(p.SeasonStats) : p.History;

    /// <summary>Spieler mit den meisten Toren bzw. Einsätzen für einen Verein über alle Saisons.</summary>
    public static IReadOnlyList<PlayerTotal> ClubLeaders(World world, int clubId, Func<PlayerSeasonStats, int> value, int count) =>
        world.Players.Values
            .Select(p => new PlayerTotal(p.Id, Seasons(p).Where(s => s.ClubId == clubId).Sum(value)))
            .Where(t => t.Value > 0)
            .OrderByDescending(t => t.Value).ThenBy(t => t.PlayerId).Take(count).ToList();

    /// <summary>Höchste Ablöse, die der Verein gezahlt (<paramref name="incoming"/>) bzw. erhalten hat; ohne Leihen.</summary>
    public static TransferRecord? RecordTransfer(World world, int clubId, bool incoming) => world.Transfers
        .Where(t => !t.Loan && t.Fee > 0 && (incoming ? t.ToClubId == clubId : t.FromClubId == clubId))
        .OrderByDescending(t => t.Fee).ThenBy(t => t.Date).FirstOrDefault();

    /// <summary>Beste Ligasaison eines Vereins (meiste Punkte, bei Gleichstand bessere Tordifferenz).</summary>
    public static ClubSeason? BestSeason(World world, int clubId, string? leagueId = null) => world.ClubSeasons
        .Where(s => s.ClubId == clubId && (leagueId is null || s.LeagueId == leagueId))
        .OrderByDescending(s => s.Points).ThenByDescending(s => s.GoalsFor - s.GoalsAgainst).ThenBy(s => s.SeasonYear).FirstOrDefault();

    /// <summary>Punkterekord einer Liga.</summary>
    public static ClubSeason? MostPoints(World world, string leagueId) => world.ClubSeasons
        .Where(s => s.LeagueId == leagueId)
        .OrderByDescending(s => s.Points).ThenByDescending(s => s.GoalsFor - s.GoalsAgainst).ThenBy(s => s.SeasonYear).FirstOrDefault();

    /// <summary>
    /// Beste Torjägersaison in einer Liga (Pflichtspieltore der Saison für einen Verein dieser Liga). Abgeschlossene
    /// Saisons über die Vereinssaisons, die laufende über die aktuelle Liga des Vereins.
    /// </summary>
    public static PlayerSeasonBest? TopScorerSeason(World world, string leagueId)
    {
        var inLeague = world.ClubSeasons.Where(s => s.LeagueId == leagueId).Select(s => (s.ClubId, s.SeasonYear)).ToHashSet();
        var current = world.FindLeague(leagueId)?.ClubIds.ToHashSet() ?? new HashSet<int>();
        return world.Players.Values
            .SelectMany(p => Seasons(p).Where(s => s.ClubId is { } c && s.Goals > 0
                    && (inLeague.Contains((c, s.SeasonYear)) || (s.SeasonYear == world.SeasonYear && current.Contains(c))))
                .Select(s => new PlayerSeasonBest(p.Id, s.ClubId!.Value, s.SeasonYear, s.Goals)))
            .OrderByDescending(b => b.Value).ThenBy(b => b.SeasonYear).ThenBy(b => b.PlayerId).FirstOrDefault();
    }
}
