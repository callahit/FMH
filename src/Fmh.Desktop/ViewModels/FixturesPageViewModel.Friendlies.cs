using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record FriendlyOpponentVm(Club Club, string Label)
{
    public override string ToString() => Label;
}

public sealed record FriendlyDateVm(DateOnly Date, string Label)
{
    public override string ToString() => Label;
}

public sealed record FriendlyRowVm(Friendly Friendly, string Date, string Match, string Detail, IRelayCommand Cancel);

/// <summary>Testspiele (AP 9): Gegner, Datum, Ort und Einnahmenteilung wählen, vereinbarte Spiele absagen.</summary>
public sealed partial class FixturesPageViewModel
{
    [ObservableProperty] private bool _friendlyView;
    [ObservableProperty] private IReadOnlyList<FriendlyOpponentVm> _opponents = Array.Empty<FriendlyOpponentVm>();
    [ObservableProperty] private FriendlyOpponentVm? _opponent;
    [ObservableProperty] private IReadOnlyList<FriendlyDateVm> _friendlyDates = Array.Empty<FriendlyDateVm>();
    [ObservableProperty] private FriendlyDateVm? _friendlyDate;
    [ObservableProperty] private bool _friendlyHome = true;
    [ObservableProperty] private IReadOnlyList<int> _shares = Array.Empty<int>();
    [ObservableProperty] private int _share;
    [ObservableProperty] private string _friendlyMessage = "";
    [ObservableProperty] private IReadOnlyList<FriendlyRowVm> _friendlies = Array.Empty<FriendlyRowVm>();
    [ObservableProperty] private bool _noFriendlies;

    private void InitFriendlies()
    {
        Opponents = Session.FriendlyCandidates()
            .Select(c => new FriendlyOpponentVm(c, $"{c.Name} ({UiText.Competition(c.LeagueId)}, {T["managers.reputation"]} {c.Reputation})")).ToList();
        Shares = Data.Friendlies.HomeShares;
        Share = Shares.FirstOrDefault();
        Opponent = Opponents.FirstOrDefault();
    }

    partial void OnFriendlyViewChanged(bool value)
    {
        if (value) LeagueView = false;
    }

    partial void OnOpponentChanged(FriendlyOpponentVm? value)
    {
        FriendlyDates = value is null ? Array.Empty<FriendlyDateVm>()
            : Session.FriendlyDates(value.Club).Select(d => new FriendlyDateVm(d, $"{Fmt.Weekday(d)} {Fmt.Date(d)}")).ToList();
        FriendlyDate = FriendlyDates.FirstOrDefault();
    }

    private void RefreshFriendlies()
    {
        var world = Session.World;
        Friendlies = Session.OwnFriendlies.Select(fr =>
        {
            var f = fr.Fixture;
            var detail = (fr.Camp ? T["friendly.camp"] + " · " : "") + $"{fr.HomeSharePercent} % / {100 - fr.HomeSharePercent} %";
            var result = f.Result is { } r ? $" {r.HomeGoals}:{r.AwayGoals}" : "";
            var friendly = fr;
            return new FriendlyRowVm(fr, $"{Fmt.Weekday(f.Date)} {Fmt.ShortDate(f.Date)}",
                $"{world.Clubs[f.HomeId].Name} – {world.Clubs[f.AwayId].Name}{result}", detail,
                new RelayCommand(() => { Session.CancelFriendly(friendly); Refresh(); }, () => friendly.Fixture.Result is null && friendly.Fixture.Date > Session.World.Date));
        }).ToList();
        NoFriendlies = Friendlies.Count == 0;
    }

    [RelayCommand]
    private void ArrangeFriendly()
    {
        if (Opponent is null || FriendlyDate is null) return;
        var error = Session.ArrangeFriendly(Opponent.Club, FriendlyDate.Date, FriendlyHome, Share);
        FriendlyMessage = error is null ? T.Format("friendly.arranged", Opponent.Club.Name, Fmt.Date(FriendlyDate.Date)) : T[error];
        Refresh();
        OnOpponentChanged(Opponent);
    }
}
