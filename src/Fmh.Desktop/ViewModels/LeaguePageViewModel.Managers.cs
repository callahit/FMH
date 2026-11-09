using Avalonia.Media;
using Fmh.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Trainer eines Vereins in der Liga-Ansicht „Trainer“.</summary>
public sealed record ManagerRowVm(string Club, string Primary, string Secondary, string Manager, string Since, string Record,
                                  string PointsPerGame, int Reputation, string Style, string Age, IBrush RowBrush)
{
    public string Initials => Crest.InitialsOf(Club);
}

/// <summary>Liga-Ansicht „Trainer“: Trainer je Verein, Trainerwechsel der Saison, vereinslose Trainer.</summary>
public sealed partial class LeaguePageViewModel
{
    [ObservableProperty] private bool _showManagers;
    [ObservableProperty] private IReadOnlyList<ManagerRowVm> _managers = Array.Empty<ManagerRowVm>();
    [ObservableProperty] private IReadOnlyList<string> _coachChanges = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _freeManagers = Array.Empty<string>();

    public bool ShowManagerList => !IsCup && ShowManagers;
    public bool NoCoachChanges => CoachChanges.Count == 0;

    partial void OnShowManagersChanged(bool value)
    {
        if (value) ShowScorers = false;
        OnPropertyChanged(nameof(ShowTable));
        OnPropertyChanged(nameof(ShowManagerList));
    }

    partial void OnCoachChangesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(NoCoachChanges));

    private void RefreshManagers(League league)
    {
        var world = Session.World;
        var own = Session.HumanClub.Id;
        Managers = league.ClubIds.Select(id => world.Clubs[id]).OrderBy(c => c.Name, StringComparer.CurrentCulture).Select(club =>
        {
            var m = Session.ManagerOf(club);
            var st = m?.CurrentStation ?? (m?.Human == true ? m.Career.Stations.LastOrDefault(s => s.To is null) : null);
            var points = st is null ? 0 : st.Wins * 3 + st.Draws;
            return new ManagerRowVm(club.Name, club.PrimaryColor, club.SecondaryColor,
                m is null ? T["managers.vacant"] : m.Human ? $"{m.Name} {T["managers.you"]}" : m.Name,
                st is null ? "" : Fmt.Date(st.From),
                st is null ? "" : $"{st.Wins}-{st.Draws}-{st.Losses}",
                st is null || st.Matches == 0 ? "–" : Fmt.Num((double)points / st.Matches, "0.00"),
                m?.Reputation ?? 0,
                m is null || m.Human ? "" : T["managerStyle." + m.Traits.Style],
                m is null || m.Human ? "" : (world.SeasonYear - m.Traits.BirthYear).ToString(),
                club.Id == own ? new SolidColorBrush(P.Accent, 0.16) : Brushes.Transparent);
        }).ToList();

        var ids = league.ClubIds.ToHashSet();
        CoachChanges = Session.CoachChanges(world.SeasonYear).Where(c => ids.Contains(c.Club.Id))
            .Select(c => $"{Fmt.Date(c.Date)} · " + T.Format("managers.change", c.Club.Name, c.Previous.Name,
                T["managers.exit." + c.Exit], c.Successor?.Name ?? T["managers.vacant"]))
            .ToList();
        FreeManagers = Session.FreeManagers.OrderByDescending(m => m.Reputation).ThenBy(m => m.Name, StringComparer.CurrentCulture).Take(12)
            .Select(m => $"{m.Name} · {T["managers.reputation"]} {m.Reputation} · {T["managerStyle." + m.Traits.Style]} · {world.SeasonYear - m.Traits.BirthYear}" +
                         (m.Traits.Sackings > 0 ? $" · {T["managers.sackings"]} {m.Traits.Sackings}" : ""))
            .ToList();
    }
}
