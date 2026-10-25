using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
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

public sealed record LeagueChoice(string Id, string Name);

/// <summary>Liga und Welt: Tabelle mit Zonen und Form, Torjäger, andere Ligen.</summary>
public sealed partial class LeaguePageViewModel : PageViewModel, IRefreshable
{
    public LeaguePageViewModel(ShellViewModel shell) : base(shell)
    {
        Leagues = Session.World.Leagues.Select(l => new LeagueChoice(l.Id, l.Name)).ToList();
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
        var world = Session.World;
        var league = world.League(League.Id);
        var rows = LeagueTable.Compute(league);
        var own = Session.HumanClub.Id;
        Table = rows.Select(r =>
        {
            var zone = r.Position <= league.Format.Promotion || (league.Level == 1 && r.Position == 1) ? P.Good
                : r.Position > rows.Count - league.Format.Relegation ? P.Bad : Colors.Transparent;
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

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (key == Avalonia.Input.Key.T) { ToggleView(); return true; }
        return false;
    }
}
