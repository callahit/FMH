using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record FormChip(string Text, IBrush Brush);

public sealed class TableRowVm
{
    public required int Position { get; init; }
    public required Club Club { get; init; }
    public required TableRow Row { get; init; }
    public required IBrush ZoneBrush { get; init; }
    public required IBrush RowBrush { get; init; }
    public required IReadOnlyList<FormChip> Form { get; init; }
    public string Name => Club.Name;
    public string Initials => Crest.InitialsOf(Club.Name);
    public string Primary => Club.PrimaryColor;
    public string Secondary => Club.SecondaryColor;
    public int Played => Row.Played;
    public int Won => Row.Won;
    public int Drawn => Row.Drawn;
    public int Lost => Row.Lost;
    public string Goals => $"{Row.GoalsFor}:{Row.GoalsAgainst}";
    public string Diff => Row.GoalDifference.ToString("+0;-0;0");
    public int Points => Row.Points;
}

public sealed record ScorerVm(int Rank, string Name, string Club, int Goals, int Assists, int Apps, string Grade, IBrush Brush);

public sealed record LeagueChoice(string Id, string Name, bool IsCup = false);
public sealed record TieVm(string Home, string Away, string Score, FontWeight HomeWeight, FontWeight AwayWeight, IBrush Brush, string Note);
public sealed record CupRoundVm(string Name, string Date, IReadOnlyList<TieVm> Ties, bool Drawn);

/// <summary>Liga und Welt: Tabelle mit Zonen und Form, Torjäger, andere Ligen.</summary>
public sealed partial class LeaguePageViewModel : PageViewModel, IRefreshable
{
    public LeaguePageViewModel(ShellViewModel shell) : base(shell)
    {
        Leagues = Session.World.Leagues.Select(l => new LeagueChoice(l.Id, UiText.Competition(l.Id)))
            .Concat(Session.World.Cups.Where(c => c.SeasonYear == Session.World.SeasonYear).Select(c => new LeagueChoice(c.Id, UiText.Competition(c.Id), true)))
            .ToList();
        _league = Leagues.First(l => l.Id == Session.HumanClub.LeagueId);
        Refresh();
    }

    public override string Title => T["menu.league"];
    public IReadOnlyList<LeagueChoice> Leagues { get; }

    [ObservableProperty] private LeagueChoice _league;
    [ObservableProperty] private bool _showScorers;
    [ObservableProperty] private IReadOnlyList<TableRowVm> _table = Array.Empty<TableRowVm>();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoScorers))]
    private IReadOnlyList<ScorerVm> _scorers = Array.Empty<ScorerVm>();

    public bool NoScorers => Scorers.Count == 0;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _legend = "";
    [ObservableProperty] private bool _isCup;
    [ObservableProperty] private IReadOnlyList<CupRoundVm> _cupRounds = Array.Empty<CupRoundVm>();

    public bool ShowTable => !IsCup && !ShowScorers;
    public bool ShowScorerList => !IsCup && ShowScorers;
    partial void OnShowScorersChanged(bool value) { OnPropertyChanged(nameof(ShowTable)); OnPropertyChanged(nameof(ShowScorerList)); }
    partial void OnIsCupChanged(bool value) { OnPropertyChanged(nameof(ShowTable)); OnPropertyChanged(nameof(ShowScorerList)); }

    partial void OnLeagueChanged(LeagueChoice value) => Refresh();

    [RelayCommand]
    private void SelectLeague(LeagueChoice? l)
    {
        if (l is not null) League = l;
    }

    [RelayCommand]
    private void ToggleView() => ShowScorers = !ShowScorers;

    public void Refresh()
    {
        IsCup = League.IsCup;
        if (IsCup) { RefreshCup(); return; }
        var world = Session.World;
        var league = world.League(League.Id);
        var playoffs = world.Countries.SelectMany(c => c.Playoffs).ToList();
        var rows = LeagueTable.Compute(league);
        var own = Session.HumanClub.Id;
        Table = rows.Select(r =>
        {
            var direct = Pyramid.DirectZone(league.Format, r.Position, world.SeasonYear);
            var zone = direct == MovementDirection.Up || (league.Level == 1 && r.Position == 1) ? P.Good
                : direct == MovementDirection.Down ? P.Bad
                : playoffs.Any(po => po.Entrants.Any(e => (e.Division == league.Id || e.Division == league.Format.Id) && e.Position == r.Position)) ? P.Warn
                : Colors.Transparent;
            return new TableRowVm
            {
                Position = r.Position,
                Club = world.Clubs[r.ClubId],
                Row = r,
                ZoneBrush = new SolidColorBrush(zone),
                RowBrush = r.ClubId == own ? new SolidColorBrush(P.Accent, 0.16) : Brushes.Transparent,
                Form = r.Form.Select(f => new FormChip(T["form." + f], new SolidColorBrush(f == 'S' ? P.Good : f == 'N' ? P.Bad : P.Warn))).ToList(),
            };
        }).ToList();
        Subtitle = T.Format("league.after", league.CurrentMatchday);
        Legend = league.Format.Promotion > 0
            ? T.Format("league.legend", league.Format.Promotion, league.Format.Relegation)
            : T.Format("league.legendTop", league.Format.Relegation);

        Scorers = league.ClubIds.SelectMany(id => world.SquadOf(world.Clubs[id]))
            .Where(p => p.SeasonStats.SeasonYear == world.SeasonYear && (p.SeasonStats.Goals > 0 || p.SeasonStats.Assists > 0))
            .OrderByDescending(p => p.SeasonStats.Goals).ThenByDescending(p => p.SeasonStats.Assists).ThenBy(p => p.LastName)
            .Take(30)
            .Select((p, i) => new ScorerVm(i + 1, p.Name, world.Clubs[p.ClubId!.Value].Name, p.SeasonStats.Goals, p.SeasonStats.Assists,
                p.SeasonStats.Appearances, UiText.Grade(p.SeasonStats.AverageGrade),
                p.ClubId == own ? new SolidColorBrush(P.Accent) : new SolidColorBrush(P.Text)))
            .ToList();
    }

    /// <summary>Pokal bzw. Relegation: Runden mit Duellen, Ergebnissen und Entscheidungsart.</summary>
    private void RefreshCup()
    {
        var world = Session.World;
        var cup = world.FindCup(League.Id);
        if (cup is null) { CupRounds = Array.Empty<CupRoundVm>(); return; }
        var own = Session.HumanClub.Id;
        var fixtures = cup.Fixtures.ToDictionary(f => f.Id);
        CupRounds = cup.Rounds.Select(r => new CupRoundVm(UiText.Round(r), string.Join(", ", r.Dates.Select(Fmt.ShortDate)),
            r.Ties.Select(t =>
            {
                var score = t.Decision == "Bye" ? T["cup.bye"] : string.Join(" / ", t.FixtureIds.Select(id => fixtures.TryGetValue(id, out var f) && f.Result is { } res ? UiText.Score(res) : "– : –"));
                var note = t.WinnerId is null ? "" : t.Decision switch
                {
                    "AwayGoals" => T["cup.decision.awayGoals"],
                    "Penalties" => T["cup.decision.penalties"],
                    "ExtraTime" => T["cup.decision.extraTime"],
                    _ => "",
                };
                var involved = t.HomeId == own || t.AwayId == own;
                return new TieVm(world.Clubs[t.HomeId].Name, t.Decision == "Bye" ? "" : world.Clubs[t.AwayId].Name, score,
                    t.WinnerId == t.HomeId ? FontWeight.Bold : FontWeight.Normal, t.WinnerId == t.AwayId ? FontWeight.Bold : FontWeight.Normal,
                    involved ? new SolidColorBrush(P.Accent, 0.16) : Brushes.Transparent, note);
            }).ToList(), r.Drawn)).ToList();
        Subtitle = cup.WinnerId is { } w ? T.Format("cup.winner", world.Clubs[w].Name) : T.Format("cup.participants", cup.ParticipantIds.Count);
        Legend = "";
    }

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (key == Avalonia.Input.Key.T) { ToggleView(); return true; }
        return false;
    }
}
