using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Spielberechtigter Spieler mit Haken für die Vormerkliste.</summary>
public sealed partial class NationalCandidateVm : ObservableObject
{
    private readonly Action _changed;

    public NationalCandidateVm(Player player, string club, string position, string strength, bool selected, Action changed)
    {
        Player = player;
        Club = club;
        Position = position;
        Strength = strength;
        _selected = selected;
        _changed = changed;
    }

    public Player Player { get; }
    public string Name => Player.Name;
    public string Club { get; }
    public string Position { get; }
    public string Strength { get; }

    [ObservableProperty] private bool _selected;

    partial void OnSelectedChanged(bool value) => _changed();
}

public sealed record TournamentRowVm(int Position, string Name, int Played, int Won, int Drawn, int Lost, string Goals, int Points, FontWeight Weight);

public sealed record TournamentGroupVm(string Title, IReadOnlyList<TournamentRowVm> Rows);

public sealed record IntlFixtureVm(string Date, string Stage, string Home, string Away, string Result, FontWeight Weight);

/// <summary>
/// Nationalteam (AP 8c): Amt des Nationaltrainers mit Vormerkliste und Spielsystem, dazu das laufende Turnier
/// (Gruppen, K.-o.-Runde), die Länderspiele der Saison und die bisherigen Turniersieger.
/// </summary>
public sealed partial class NationalPageViewModel : PageViewModel, IRefreshable
{
    public NationalPageViewModel(ShellViewModel shell) : base(shell) => Refresh();

    public override string Title => T["menu.national"];

    [ObservableProperty] private bool _isCoach;
    [ObservableProperty] private string _jobText = "";
    [ObservableProperty] private string _coachTitle = "";
    [ObservableProperty] private string _targetText = "";
    [ObservableProperty] private string _recordText = "";
    [ObservableProperty] private string _squadTitle = "";
    [ObservableProperty] private IReadOnlyList<NationalCandidateVm> _candidates = Array.Empty<NationalCandidateVm>();
    [ObservableProperty] private IReadOnlyList<string> _formations = Array.Empty<string>();
    [ObservableProperty] private string? _formation;
    [ObservableProperty] private string _tournamentTitle = "";
    [ObservableProperty] private IReadOnlyList<TournamentGroupVm> _groups = Array.Empty<TournamentGroupVm>();
    [ObservableProperty] private IReadOnlyList<IntlFixtureVm> _knockout = Array.Empty<IntlFixtureVm>();
    [ObservableProperty] private IReadOnlyList<IntlFixtureVm> _fixtures = Array.Empty<IntlFixtureVm>();
    [ObservableProperty] private string _champions = "";

    private bool _loading;

    public void Refresh()
    {
        _loading = true;
        var s = Session;
        var w = s.World;
        var nation = s.HumanNation;
        IsCoach = nation is not null;
        JobText = s.PendingNationalOffer is { OfferExpires: { } until } offer
            ? T.Format("national.offer", offer.Name, Fmt.Date(until))
            : T.Format("national.noJob", Data.Internationals.Coach.OfferReputation);
        if (nation is not null)
        {
            CoachTitle = T.Format("national.coach", nation.Name);
            TargetText = T.Format("national.target", T["intl.target." + (nation.CoachTarget is { Length: > 0 } t ? t : "Q")], nation.CoachUntil);
            RecordText = T.Format("national.record", nation.Played, nation.Won, nation.Drawn, nation.Lost, nation.GoalsFor, nation.GoalsAgainst);
            Formations = Data.Formations.Select(f => f.Id).ToList();
            Formation = nation.Formation is { Length: > 0 } f ? f : Data.Internationals.Formation;
            var planned = nation.PlannedSquad.ToHashSet();
            Candidates = s.NationalCandidates(nation).Take(60).Select(p => new NationalCandidateVm(p,
                p.ClubId is { } c ? w.Clubs[c].Name : "", UiText.Position(p.MainPosition),
                UiText.Strength(NationalSquads.Score(p, Data)), planned.Contains(p.Id), SquadChanged)).ToList();
            UpdateSquadTitle();
        }
        else Candidates = Array.Empty<NationalCandidateVm>();
        RefreshTournament(nation);
        _loading = false;
    }

    private void SquadChanged()
    {
        if (_loading) return;
        Session.SetNationalSquad(Candidates.Where(c => c.Selected).Select(c => c.Player.Id));
        UpdateSquadTitle();
    }

    private void UpdateSquadTitle() =>
        SquadTitle = T.Format("national.squad", Candidates.Count(c => c.Selected), Data.Internationals.SquadSize);

    partial void OnFormationChanged(string? value)
    {
        if (!_loading && value is not null) Session.SetNationalFormation(value);
    }

    private string NameOf(string code) => Session.World.NationalTeams.FirstOrDefault(t => t.Code == code)?.Name ?? code;

    private void RefreshTournament(NationalTeam? nation)
    {
        var w = Session.World;
        var t = w.Tournaments.Where(x => x.Phase != TournamentPhase.Done).OrderBy(x => x.Year).FirstOrDefault()
                ?? w.Tournaments.OrderByDescending(x => x.Year).FirstOrDefault();
        var own = nation?.Code;
        FontWeight Bold(string code) => code == own ? FontWeight.Bold : FontWeight.Normal;
        if (t is null)
        {
            TournamentTitle = T["national.noTournament"];
            Groups = Array.Empty<TournamentGroupVm>();
            Knockout = Array.Empty<IntlFixtureVm>();
        }
        else
        {
            TournamentTitle = $"{t.Name} {t.Year} · {T["national.phase." + t.Phase]}" + (t.Champion is { Length: > 0 } c ? $" · {NameOf(c)}" : "");
            var finals = t.Phase != TournamentPhase.Qualification;
            var prefix = finals ? "G-" : "Q-";
            var fixtures = w.InternationalFixtures.Where(f => f.TournamentId == t.Id).ToList();
            Groups = (finals ? t.Groups : t.QualificationGroups).Select(g => new TournamentGroupVm(T.Format("national.group", g.Name),
                InternationalTournaments.Standings(fixtures.Where(f => f.Stage == prefix + g.Name), g)
                    .Select((r, i) => new TournamentRowVm(i + 1, NameOf(r.Code), r.Played, r.Won, r.Drawn, r.Lost, $"{r.GoalsFor}:{r.GoalsAgainst}", r.Points, Bold(r.Code)))
                    .ToList())).ToList();
            Knockout = fixtures.Where(f => f.Stage.StartsWith("KO", StringComparison.Ordinal)).OrderBy(f => f.Date).ThenBy(f => f.Id).Select(Make).ToList();
        }
        Fixtures = w.InternationalFixtures.Where(f => own is null || f.Involves(own)).OrderBy(f => f.Date).ThenBy(f => f.Id).Take(40).Select(Make).ToList();
        Champions = string.Join(" · ", w.Tournaments.Where(x => x.Champion != "").OrderByDescending(x => x.Year).Select(x => $"{x.Name} {x.Year}: {NameOf(x.Champion)}"));

        IntlFixtureVm Make(InternationalFixture f) => new(Fmt.ShortDate(f.Date), f.Stage, NameOf(f.HomeCode), NameOf(f.AwayCode),
            f.Result is { } r ? $"{r.HomeGoals}:{r.AwayGoals}" + (f.WinnerCode is { } win && r.HomeGoals == r.AwayGoals ? $" ({NameOf(win)})" : "") : "– : –",
            f.Involves(own ?? "") ? FontWeight.Bold : FontWeight.Normal);
    }

    [RelayCommand]
    private void Suggest()
    {
        Session.SuggestNationalSquad();
        Refresh();
    }

    [RelayCommand]
    private void Resign()
    {
        Session.ResignNationalJob();
        Refresh();
    }
}
