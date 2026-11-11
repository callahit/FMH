using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record HistoryClubChoice(int Id, string Name);

public sealed record ClubSeasonLine(string Season, string League, string Position, int Played, string Record, string Goals, int Points);

public sealed record ChronicleLine(string Season, string Champion);

/// <summary>
/// Historie (AP 11b): Vereinsrekorde und Saisonbilanzen eines wählbaren Vereins, Ligarekorde und Meisterchronik eines
/// wählbaren Wettbewerbs. Spielrekorde zählen nur Pflichtspiele.
/// </summary>
public sealed partial class HistoryPageViewModel : PageViewModel, IRefreshable
{
    private const int Leaders = 5;

    public HistoryPageViewModel(ShellViewModel shell) : base(shell)
    {
        var w = Session.World;
        var own = Session.HumanClub;
        Clubs = w.Clubs.Values.Where(c => c.ReserveOf is null && w.FindLeague(c.LeagueId) is not null)
            .OrderBy(c => c.Id != own.Id).ThenBy(c => c.Name, StringComparer.CurrentCulture)
            .Select(c => new HistoryClubChoice(c.Id, c.Name)).ToList();
        // Ligen der Welt und alle Wettbewerbe der Meisterchronik (Pokale, Europapokale).
        Competitions = w.Leagues.Select(l => l.Id).Concat(w.History.Select(h => h.LeagueId)).Distinct()
            .Select(id => new LeagueChoice(id, CompetitionName(id), w.FindLeague(id) is null)).ToList();
        _club = Clubs.FirstOrDefault(c => c.Id == own.Id) ?? Clubs.FirstOrDefault();
        _competition = Competitions.FirstOrDefault(c => c.Id == own.LeagueId) ?? Competitions.FirstOrDefault();
        Refresh();
    }

    public override string Title => T["menu.history"];

    public IReadOnlyList<HistoryClubChoice> Clubs { get; }
    public IReadOnlyList<LeagueChoice> Competitions { get; }

    [ObservableProperty] private HistoryClubChoice? _club;
    [ObservableProperty] private LeagueChoice? _competition;
    [ObservableProperty] private IReadOnlyList<InfoLine> _clubRecords = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _honours = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _scorers = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _appearances = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<ClubSeasonLine> _seasons = Array.Empty<ClubSeasonLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _competitionRecords = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _titleTable = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<ChronicleLine> _chronicle = Array.Empty<ChronicleLine>();

    partial void OnClubChanged(HistoryClubChoice? value) => RefreshClub();
    partial void OnCompetitionChanged(LeagueChoice? value) => RefreshCompetition();

    public void Refresh()
    {
        RefreshClub();
        RefreshCompetition();
    }

    private string CompetitionName(string id) =>
        T.TryGet("competition." + id) ?? Session.World.History.LastOrDefault(h => h.LeagueId == id)?.LeagueName ?? UiText.Competition(id);

    private string ClubName(int id) => Session.World.Clubs.TryGetValue(id, out var c) ? c.Name : "–";

    private string PlayerName(int id) => Session.World.Players.TryGetValue(id, out var p) ? p.Name : "–";

    private string None => T["history.none"];

    /// <summary>"12.03.2027 · Heim – Gast 5 : 0 (Liga)" bzw. mit Zuschauerzahl.</summary>
    private string Match(MatchRecord? r, bool attendance = false)
    {
        if (r is null) return None;
        var text = $"{ClubName(r.HomeId)} – {ClubName(r.AwayId)} {r.HomeGoals} : {r.AwayGoals}";
        if (attendance) text = T.Format("history.spectators", Fmt.Int(r.Attendance)) + " · " + text;
        return $"{text} ({CompetitionName(r.CompetitionId)}, {Fmt.Date(r.Date)})";
    }

    private void RefreshClub()
    {
        if (Club is not { } choice) return;
        var w = Session.World;
        var id = choice.Id;
        var scope = MatchRecord.ClubScope(id);
        var cur = w.Clubs.TryGetValue(id, out var club) ? UiText.Currency(club) : CurrencyConfig.Base;
        string Transfer(TransferRecord? t) => t is null ? None
            : $"{PlayerName(t.PlayerId)} · {Fmt.Money(t.Fee, cur)} ({(t.FromClubId == id ? ClubName(t.ToClubId ?? 0) : ClubName(t.FromClubId ?? 0))}, {GameSession.SeasonLabel(t.SeasonYear)})";
        var best = RecordBook.BestSeason(w, id);
        ClubRecords = new List<InfoLine>
        {
            new(T["history.biggestWin"], Match(RecordBook.Find(w, scope, RecordKinds.BiggestWin))),
            new(T["history.biggestDefeat"], Match(RecordBook.Find(w, scope, RecordKinds.BiggestDefeat))),
            new(T["history.mostGoals"], Match(RecordBook.Find(w, scope, RecordKinds.MostGoals))),
            new(T["history.attendance"], Match(RecordBook.Find(w, scope, RecordKinds.Attendance), attendance: true)),
            new(T["history.transferIn"], Transfer(RecordBook.RecordTransfer(w, id, incoming: true))),
            new(T["history.transferOut"], Transfer(RecordBook.RecordTransfer(w, id, incoming: false))),
            new(T["history.bestSeason"], best is null ? None
                : T.Format("history.seasonLine", GameSession.SeasonLabel(best.SeasonYear), CompetitionName(best.LeagueId), best.Position, best.Points)),
        };
        Honours = RecordBook.Honours(w, id).Select(h => new InfoLine($"{CompetitionName(h.CompetitionId)} ({h.Count}×)",
            string.Join(", ", h.Years.Select(GameSession.SeasonLabel)))).ToList();
        if (Honours.Count == 0) Honours = new[] { new InfoLine(T["history.noTitles"], "") };
        Scorers = Leaderboard(RecordBook.ClubLeaders(w, id, s => s.Goals, Leaders), "history.goals");
        Appearances = Leaderboard(RecordBook.ClubLeaders(w, id, s => s.Appearances, Leaders), "history.apps");
        Seasons = w.ClubSeasons.Where(s => s.ClubId == id).OrderByDescending(s => s.SeasonYear)
            .Select(s => new ClubSeasonLine(GameSession.SeasonLabel(s.SeasonYear), CompetitionName(s.LeagueId), $"{s.Position}.",
                s.Played, $"{s.Won}-{s.Drawn}-{s.Lost}", $"{s.GoalsFor}:{s.GoalsAgainst}", s.Points)).ToList();
    }

    private IReadOnlyList<InfoLine> Leaderboard(IReadOnlyList<PlayerTotal> totals, string unitKey) => totals.Count == 0
        ? new[] { new InfoLine(None, "") }
        : totals.Select((t, i) => new InfoLine($"{i + 1}. {PlayerName(t.PlayerId)}", T.Format(unitKey, t.Value))).ToList();

    private void RefreshCompetition()
    {
        if (Competition is not { } choice) return;
        var w = Session.World;
        var scope = MatchRecord.CompetitionScope(choice.Id);
        var records = new List<InfoLine>
        {
            new(T["history.biggestWin"], Match(RecordBook.Find(w, scope, RecordKinds.BiggestWin))),
            new(T["history.mostGoals"], Match(RecordBook.Find(w, scope, RecordKinds.MostGoals))),
            new(T["history.attendance"], Match(RecordBook.Find(w, scope, RecordKinds.Attendance), attendance: true)),
        };
        if (!choice.IsCup)
        {
            var most = RecordBook.MostPoints(w, choice.Id);
            records.Add(new(T["history.mostPoints"], most is null ? None
                : T.Format("history.pointsLine", ClubName(most.ClubId), GameSession.SeasonLabel(most.SeasonYear), most.Points, most.Played)));
            var top = RecordBook.TopScorerSeason(w, choice.Id);
            records.Add(new(T["history.topScorerSeason"], top is null ? None
                : T.Format("history.scorerLine", PlayerName(top.PlayerId), ClubName(top.ClubId), GameSession.SeasonLabel(top.SeasonYear), top.Value)));
        }
        CompetitionRecords = records;
        TitleTable = RecordBook.TitleTable(w, choice.Id).Select(t => new InfoLine(t.Name, T.Format("history.titles", t.Titles))).ToList();
        Chronicle = w.History.Where(h => h.LeagueId == choice.Id).OrderByDescending(h => h.SeasonYear)
            .Select(h => new ChronicleLine(GameSession.SeasonLabel(h.SeasonYear), h.ChampionName)).ToList();
        if (Chronicle.Count == 0) Chronicle = new[] { new ChronicleLine("", T["history.noChronicle"]) };
    }
}
