using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Eine Zeile der Kaderliste; sortierbare Werte als eigene Eigenschaften.</summary>
public sealed class PlayerRow
{
    public PlayerRow(Player p, Club club)
    {
        var s = Session;
        var profiles = Data.PositionProfiles;
        Player = p;
        Position = UiText.Position(p.MainPosition);
        PositionOrder = (int)p.MainPosition;
        Name = p.Name;
        Age = p.Age(s.World.Date);
        Strength = Ratings.Strength(p, profiles);
        var rating = (int)Math.Round(Ratings.PositionRating(p, p.MainPosition, profiles));
        StrengthBrush = new SolidColorBrush(Palette.Current.AttributeColor(rating));
        Condition = Math.Round(p.State.Condition);
        ConditionBrush = new SolidColorBrush(Palette.Current.ConditionColor(p.State.Condition));
        Morale = UiText.Morale(p.State.Morale);
        MoraleOrder = (int)p.State.Morale;
        MoraleBrush = new SolidColorBrush(p.State.Morale switch
        {
            <= Core.Model.Morale.Bad => Palette.Current.Bad,
            <= Core.Model.Morale.Okay => Palette.Current.Warn,
            _ => Palette.Current.Good,
        });
        Form = p.State.Form;
        Status = UiText.Status(p, club);
        StatusBrush = new SolidColorBrush(p.IsAvailable ? Palette.Current.Muted : Palette.Current.Bad);
        Apps = p.SeasonStats.Appearances;
        Goals = p.SeasonStats.Goals;
        Grade = p.SeasonStats.AverageGrade ?? 9;
        GradeText = UiText.Grade(p.SeasonStats.AverageGrade);
        ContractUntil = p.Contract?.Until.ToString("MM/yyyy") ?? "–";
        ContractOrder = p.Contract?.Until.DayNumber ?? 0;
        WageValue = p.Contract?.WeeklyWage ?? 0;
        Wage = p.Contract is { } c ? UiText.Wage(c.WeeklyWage, s.HumanManager.Profile, UiText.Currency(club)) : "–";
        // Meldeliste: gemeldet, ausgenommen (zu jung) oder nicht gemeldet; dazu lokal bzw. im Verein ausgebildet.
        if (Registration.RulesFor(s.World, club) is { } rules)
        {
            var exempt = Registration.IsExempt(p, s.World, rules);
            var trained = Registration.IsClubTrained(p, club.Id) ? T["registration.club"] : Registration.IsLocal(p, s.World, club.CountryCode) ? T["registration.local"] : "";
            RegistrationText = (exempt ? T.Format("registration.exempt", rules.ExemptAge) : p.Registration.ClubId == club.Id ? "✓" : "–")
                               + (trained.Length > 0 ? " · " + trained : "");
            RegistrationBrush = new SolidColorBrush(Registration.IsEligible(p, s.World) ? Palette.Current.Text : Palette.Current.Bad);
        }
    }

    private static Core.Data.Texts T => Data.Texts;
    public string RegistrationText { get; } = "";
    public IBrush RegistrationBrush { get; } = Brushes.Transparent;

    private static Core.Game.GameSession Session => AppState.Current.Session!;
    private static Core.Data.GameData Data => AppState.Current.Data;

    public Player Player { get; }
    public string Position { get; }
    public int PositionOrder { get; }
    public string Name { get; }
    public int Age { get; }
    public double Strength { get; }
    public string StrengthText => Fmt.Num(Strength, "0.0");
    public IBrush StrengthBrush { get; }
    public double Condition { get; }
    public string ConditionText => $"{Condition:0} %";
    public IBrush ConditionBrush { get; }
    public string Morale { get; }
    public int MoraleOrder { get; }
    public IBrush MoraleBrush { get; }
    public int Form { get; }
    public string Status { get; }
    public IBrush StatusBrush { get; }
    public int Apps { get; }
    public int Goals { get; }
    public double Grade { get; }
    public string GradeText { get; }
    public string ContractUntil { get; }
    public int ContractOrder { get; }
    public int WageValue { get; }
    public string Wage { get; }
}

public sealed record FilterOption(string Label, Func<Position, bool> Match);

public sealed partial class SquadPageViewModel : PageViewModel, IRefreshable
{
    public SquadPageViewModel(ShellViewModel shell) : base(shell)
    {
        Filters = new List<FilterOption>
        {
            new(T["filter.all"], _ => true),
            new(T["filter.gk"], p => p == Core.Model.Position.GK),
            new(T["filter.def"], PositionInfo.IsDefender),
            new(T["filter.mid"], PositionInfo.IsMidfielder),
            new(T["filter.att"], PositionInfo.IsAttacker),
        };
        _filter = Filters[0];
        Refresh();
    }

    public override string Title => T["menu.squad"];
    public IReadOnlyList<FilterOption> Filters { get; }

    [ObservableProperty] private FilterOption _filter;
    [ObservableProperty] private IReadOnlyList<PlayerRow> _rows = Array.Empty<PlayerRow>();
    [ObservableProperty] private PlayerRow? _selected;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _showYouth;
    [ObservableProperty] private string _youthLabel = "";

    partial void OnFilterChanged(FilterOption value) => Refresh();
    partial void OnShowYouthChanged(bool value) => Refresh();

    public void Refresh()
    {
        var club = Session.HumanClub;
        var squad = Session.World.SquadOf(club).ToList();
        var youth = Session.YouthOf(club);
        YouthLabel = T.Format("squad.youthToggle", youth.Count);
        Rows = (ShowYouth ? youth : squad).Where(p => Filter.Match(p.MainPosition))
            .OrderBy(p => (int)p.MainPosition).ThenByDescending(p => Ratings.Strength(p, Data.PositionProfiles))
            .Select(p => new PlayerRow(p, club)).ToList();
        var injured = squad.Count(p => p.State.Injury is not null);
        var suspended = squad.Count(p => p.State.SuspendedMatches > 0);
        Summary = T.Format("squad.summary", squad.Count, Fmt.Num(squad.Average(p => p.Age(Session.World.Date)), "0.0"),
            UiText.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(club, Session.World, Data))), injured, suspended);
        var w = Session.World;
        if (Registration.RulesFor(w, club) is { } r)
        {
            var listed = squad.Where(p => !Registration.IsExempt(p, w, r) && p.Registration.ClubId == club.Id).ToList();
            RegistrationSummary = T.Format("squad.registration", listed.Count, Registration.Capacity(w, club, r),
                listed.Count(p => Registration.IsLocal(p, w, club.CountryCode)), r.MinLocal,
                listed.Count(p => Registration.IsClubTrained(p, club.Id)), r.MinClubTrained);
        }
        else RegistrationSummary = "";
    }

    [ObservableProperty] private string _registrationSummary = "";
    [ObservableProperty] private string _registrationMessage = "";

    /// <summary>Ausgewählten Spieler melden bzw. abmelden.</summary>
    [RelayCommand]
    private void ToggleRegistration()
    {
        if (Selected?.Player is not { } p) return;
        var error = Session.SetRegistration(p, p.Registration.ClubId != Session.HumanClub.Id);
        RegistrationMessage = error is null ? "" : T[error];
        Refresh();
    }

    [RelayCommand]
    private void SetFilter(FilterOption? f)
    {
        if (f is not null) Filter = f;
    }

    [RelayCommand]
    public void OpenPlayer(PlayerRow? row)
    {
        row ??= Selected;
        if (row is null) return;
        var list = Rows.Select(r => r.Player).ToList();
        Shell.ShowSubPage(new PlayerPageViewModel(Shell, list, list.IndexOf(row.Player)));
    }

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (key == Avalonia.Input.Key.Enter) { OpenPlayer(null); return true; }
        return false;
    }
}
