using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record SeasonChoice(int Year, string Label);

public sealed record PlayerStatLine(int Rank, string Name, string Club, int Apps, int Goals, int Assists, int Cards, string Grade);

public sealed record TeamStatLine(int Position, string Club, int Played, string Record, string Goals, string PerGame, int Points);

/// <summary>Datenzentrale (AP 11c), Reiter Statistiken: Spieler- und Teamwerte je Liga und Saison.</summary>
public sealed partial class HistoryPageViewModel
{
    private const int StatRows = 30;

    public IReadOnlyList<LeagueChoice> StatLeagues { get; private set; } = Array.Empty<LeagueChoice>();
    public IReadOnlyList<SeasonChoice> StatSeasons { get; private set; } = Array.Empty<SeasonChoice>();
    public IReadOnlyList<ValueOption> StatSorts { get; private set; } = Array.Empty<ValueOption>();

    [ObservableProperty] private LeagueChoice? _statLeague;
    [ObservableProperty] private SeasonChoice? _statSeason;
    [ObservableProperty] private ValueOption? _statSort;
    [ObservableProperty] private IReadOnlyList<PlayerStatLine> _playerStats = Array.Empty<PlayerStatLine>();
    [ObservableProperty] private IReadOnlyList<TeamStatLine> _teamStats = Array.Empty<TeamStatLine>();

    partial void OnStatLeagueChanged(LeagueChoice? value) => RefreshStats();
    partial void OnStatSeasonChanged(SeasonChoice? value) => RefreshStats();
    partial void OnStatSortChanged(ValueOption? value) => RefreshStats();

    private void InitStats()
    {
        var w = Session.World;
        StatLeagues = w.Leagues.Select(l => new LeagueChoice(l.Id, CompetitionName(l.Id))).ToList();
        StatSeasons = DataCenter.SeasonYears(w).Select(y => new SeasonChoice(y, GameSession.SeasonLabel(y))).ToList();
        StatSorts = new[] { "goals", "assists", "grade", "apps", "cards" }.Select(k => new ValueOption(T["data.sort." + k], k)).ToList();
        StatSort = StatSorts[0];
        StatSeason = StatSeasons.FirstOrDefault();
        StatLeague = StatLeagues.FirstOrDefault(l => l.Id == Session.HumanClub.LeagueId) ?? StatLeagues.FirstOrDefault();
    }

    private void RefreshStats()
    {
        if (StatLeague is not { } league || StatSeason is not { } season) return;
        var w = Session.World;
        var rows = DataCenter.PlayerStats(w, league.Id, season.Year);
        // Notenliste nur mit genug benoteten Spielen, sonst führt ein einziges gutes Spiel.
        var minGraded = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Stats.GradedMatches) / 3);
        IOrderedEnumerable<PlayerStatRow> sorted = (StatSort?.Value as string) switch
        {
            "assists" => rows.OrderByDescending(r => r.Stats.Assists).ThenByDescending(r => r.Stats.Goals),
            "grade" => rows.Where(r => r.Stats.GradedMatches >= minGraded).OrderBy(r => r.Stats.AverageGrade),
            "apps" => rows.OrderByDescending(r => r.Stats.Appearances).ThenByDescending(r => r.Stats.Minutes),
            "cards" => rows.OrderByDescending(r => r.Stats.RedCards * 3 + r.Stats.YellowCards),
            _ => rows.OrderByDescending(r => r.Stats.Goals).ThenByDescending(r => r.Stats.Assists),
        };
        PlayerStats = sorted.ThenBy(r => r.Stats.Minutes).ThenBy(r => r.Player.Id).Take(StatRows)
            .Select((r, i) => new PlayerStatLine(i + 1, r.Player.Name, r.Stats.ClubId is { } c ? ClubName(c) : "–", r.Stats.Appearances,
                r.Stats.Goals, r.Stats.Assists, r.Stats.YellowCards + r.Stats.RedCards, UiText.Grade(r.Stats.AverageGrade)))
            .ToList();
        TeamStats = DataCenter.TeamStats(w, league.Id, season.Year).Select(t => new TeamStatLine(t.Position, ClubName(t.ClubId), t.Played,
            $"{t.Won}-{t.Drawn}-{t.Lost}", $"{t.GoalsFor}:{t.GoalsAgainst}",
            t.Played == 0 ? "–" : $"{Fmt.Num(t.GoalsFor / (double)t.Played, "0.0")} : {Fmt.Num(t.GoalsAgainst / (double)t.Played, "0.0")}", t.Points)).ToList();
    }
}
