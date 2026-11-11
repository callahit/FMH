using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record OpponentMatchLine(string Date, string Competition, string Venue, string Opponent, string Result, IBrush Brush);

/// <summary>Datenzentrale (AP 11c), Reiter Gegneranalyse: letzte fünf Pflichtspiele, Spielsystem, Stammkräfte, Torschützen.</summary>
public sealed partial class HistoryPageViewModel
{
    private const int AnalysedMatches = 5;

    [ObservableProperty] private HistoryClubChoice? _opponent;
    [ObservableProperty] private string _opponentTitle = "";
    [ObservableProperty] private string _opponentSummary = "";
    [ObservableProperty] private string _opponentFormation = "";
    [ObservableProperty] private IReadOnlyList<OpponentMatchLine> _opponentMatches = Array.Empty<OpponentMatchLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _opponentRegulars = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _opponentScorers = Array.Empty<InfoLine>();

    partial void OnOpponentChanged(HistoryClubChoice? value) => RefreshOpponent();

    private void InitOpponent()
    {
        var next = DataCenter.NextOpponent(Session.World, Session.HumanClub.Id);
        Opponent = (next is { } id ? Clubs.FirstOrDefault(c => c.Id == id) : null)
                    ?? Clubs.FirstOrDefault(c => c.Id != Session.HumanClub.Id);
    }

    private void RefreshOpponent()
    {
        var w = Session.World;
        if (Opponent is null || !w.Clubs.TryGetValue(Opponent.Id, out var club)) return;
        var report = DataCenter.Analyse(w, club, AnalysedMatches);
        var next = DataCenter.NextOpponent(w, Session.HumanClub.Id) == club.Id;
        OpponentTitle = next ? T.Format("data.nextOpponent", club.Name) : club.Name;
        OpponentSummary = report.LastMatches.Count == 0 ? T["data.noMatches"]
            : T.Format("data.formSummary", report.LastMatches.Count, report.Won, report.Drawn, report.Lost, report.GoalsFor, report.GoalsAgainst);
        OpponentFormation = T.Format("data.formation", report.FormationId);
        OpponentMatches = report.LastMatches.Select(f =>
        {
            var home = f.HomeId == club.Id;
            var r = f.Result!;
            var (own, other) = home ? (r.HomeGoals, r.AwayGoals) : (r.AwayGoals, r.HomeGoals);
            var brush = own > other ? P.Good : own < other ? P.Bad : P.Warn;
            return new OpponentMatchLine(Fmt.ShortDate(f.Date), CompetitionName(f.CompetitionId), T[home ? "data.home" : "data.away"],
                ClubName(home ? f.AwayId : f.HomeId), $"{own} : {other}", new SolidColorBrush(brush));
        }).ToList();
        OpponentRegulars = report.Regulars.Select(p => new InfoLine(p.Name,
            $"{UiText.Position(p.MainPosition)} · {T.Format("history.apps", p.SeasonStats.Appearances)}")).ToList();
        OpponentScorers = report.Scorers.Count == 0 ? new[] { new InfoLine(None, "") }
            : report.Scorers.Select(p => new InfoLine(p.Name, T.Format("history.goals", p.SeasonStats.Goals))).ToList();
    }
}
