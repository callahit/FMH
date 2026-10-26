using Fmh.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;

namespace Fmh.Desktop.ViewModels;

public sealed class FixtureVm
{
    public required Fixture Fixture { get; init; }
    public required Club Home { get; init; }
    public required Club Away { get; init; }
    public required bool Own { get; init; }
    public string Matchday { get; init; } = "";
    public string Date => $"{Fmt.Weekday(Fixture.Date)} {Fmt.ShortDate(Fixture.Date)}";
    public string Result => Fixture.Result is { } r ? UiText.Score(r) : "– : –";
    public IBrush ResultBrush { get; init; } = Brushes.Gray;
    public string Detail { get; init; } = "";
    public string HomeName => Home.Name;
    public string AwayName => Away.Name;
    public string HomeInitials => Crest.InitialsOf(Home.Name);
    public string AwayInitials => Crest.InitialsOf(Away.Name);
    public FontWeight Weight => Own ? FontWeight.Bold : FontWeight.Normal;
}

/// <summary>Spielplan: eigene Spiele der Saison oder ein kompletter Spieltag der Liga.</summary>
public sealed partial class FixturesPageViewModel : PageViewModel, IRefreshable
{
    public FixturesPageViewModel(ShellViewModel shell) : base(shell)
    {
        var league = Session.World.LeagueOf(Session.HumanClub);
        _matchday = Math.Max(1, Math.Min(league.MatchdayCount, league.CurrentMatchday + 1));
        Refresh();
    }

    public override string Title => T["menu.fixtures"];
    public string Season => GameSession.SeasonLabel(Session.World.SeasonYear);

    [ObservableProperty] private bool _leagueView;
    [ObservableProperty] private int _matchday;
    [ObservableProperty] private IReadOnlyList<FixtureVm> _own = Array.Empty<FixtureVm>();
    [ObservableProperty] private IReadOnlyList<FixtureVm> _day = Array.Empty<FixtureVm>();
    [ObservableProperty] private string _dayTitle = "";

    partial void OnMatchdayChanged(int value) => Refresh();

    public void Refresh()
    {
        var world = Session.World;
        var club = Session.HumanClub;
        Own = Session.AllFixtures.Where(f => f.Involves(club.Id)).OrderBy(f => f.Date).Select(f => Make(f, club.Id)).ToList();
        var league = world.LeagueOf(club);
        var fixtures = league.Fixtures.Where(f => f.Matchday == Matchday).ToList();
        Day = fixtures.Select(f => Make(f, club.Id)).ToList();
        DayTitle = T.Format("fixtures.matchday", league.Name, Matchday, league.MatchdayCount)
                   + (fixtures.Count > 0 ? $" · {Fmt.Weekday(fixtures[0].Date)}, {Fmt.Date(fixtures[0].Date)}" : "");
    }

    private FixtureVm Make(Fixture f, int own)
    {
        var world = Session.World;
        var brush = P.Muted;
        var detail = "";
        if (f.Result is { } r)
        {
            if (f.Involves(own))
            {
                var home = f.HomeId == own;
                var g = home ? r.HomeGoals : r.AwayGoals;
                var o = home ? r.AwayGoals : r.HomeGoals;
                brush = g > o ? P.Good : g < o ? P.Bad : P.Warn;
                detail = string.Join(", ", r.Goals.Where(x => x.Home == home).Select(x => world.Players[x.ScorerId].LastName + (x.Penalty ? " (E)" : "")));
            }
            else
            {
                brush = P.Surface2;
                detail = $"({r.HalfTimeHome}:{r.HalfTimeAway})";
            }
        }
        return new FixtureVm
        {
            Fixture = f, Home = world.Clubs[f.HomeId], Away = world.Clubs[f.AwayId], Own = f.Involves(own),
            Matchday = T.Format("fixtures.md", f.Matchday), ResultBrush = new SolidColorBrush(brush), Detail = detail,
        };
    }

    [RelayCommand] private void PreviousDay() => Matchday = Math.Max(1, Matchday - 1);

    [RelayCommand]
    private void NextDay() => Matchday = Math.Min(Session.World.LeagueOf(Session.HumanClub).MatchdayCount, Matchday + 1);

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (key == Avalonia.Input.Key.L) { LeagueView = !LeagueView; return true; }
        if (LeagueView && key == Avalonia.Input.Key.Left) { PreviousDay(); return true; }
        if (LeagueView && key == Avalonia.Input.Key.Right) { NextDay(); return true; }
        return false;
    }
}
