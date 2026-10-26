using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Ein Bereich des Hauptfensters (F1–F10).</summary>
public abstract class PageViewModel : ViewModelBase
{
    protected PageViewModel(ShellViewModel shell) => Shell = shell;

    public ShellViewModel Shell { get; }
    public abstract string Title { get; }
}

public sealed partial class NavItem : ObservableObject
{
    public required string Label { get; init; }
    public required Geometry Icon { get; init; }
    public required Func<PageViewModel> Create { get; init; }
    public required int Index { get; init; }
    public required Type PageType { get; init; }
    public bool Later { get; init; }
    public string Shortcut => $"F{Index + 1}";

    [ObservableProperty] private bool _isActive;
}

/// <summary>
/// Grundlayout (Kapitel 11): Kopfzeile mit Verein, Datum, Liga, Platz und Weiter-Knopf, Menü links,
/// Hauptbereich, Statuszeile unten.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private PageViewModel? _previousPage;

    public ShellViewModel(MainWindowViewModel main)
    {
        _main = main;
        Palette.Apply(Session.HumanManager.Profile.Theme);
        Geometry Icon(string key) => (Geometry)Avalonia.Application.Current!.FindResource(key)!;
        PlaceholderPageViewModel Later(string title, string text) => new(this, title, text);
        var defs = new (string key, string icon, Type type, Func<PageViewModel> create, bool later)[]
        {
            ("menu.inbox", "IconInbox", typeof(InboxPageViewModel), () => new InboxPageViewModel(this), false),
            ("menu.squad", "IconSquad", typeof(SquadPageViewModel), () => new SquadPageViewModel(this), false),
            ("menu.tactics", "IconTactics", typeof(TacticsPageViewModel), () => new TacticsPageViewModel(this), false),
            ("menu.training", "IconTraining", typeof(TrainingPageViewModel), () => new TrainingPageViewModel(this), false),
            ("menu.fixtures", "IconFixtures", typeof(FixturesPageViewModel), () => new FixturesPageViewModel(this), false),
            ("menu.transfers", "IconTransfers", typeof(TransfersPageViewModel), () => new TransfersPageViewModel(this), false),
            ("menu.finances", "IconFinances", typeof(PlaceholderPageViewModel), () => Later("menu.finances", "placeholder.finances"), true),
            ("menu.stadium", "IconStadium", typeof(PlaceholderPageViewModel), () => Later("menu.stadium", "placeholder.stadium"), true),
            ("menu.club", "IconClub", typeof(ClubPageViewModel), () => new ClubPageViewModel(this), false),
            ("menu.league", "IconLeague", typeof(LeaguePageViewModel), () => new LeaguePageViewModel(this), false),
        };
        Nav = defs.Select((d, i) => new NavItem { Label = T[d.key], Icon = Icon(d.icon), PageType = d.type, Create = d.create, Index = i, Later = d.later }).ToList();
        Open(Nav[0]);
    }

    public IReadOnlyList<NavItem> Nav { get; }

    /// <summary>Neue Shell in der aktuellen Sprache, auf derselben Seite.</summary>
    public ShellViewModel Recreate()
    {
        var shell = new ShellViewModel(_main);
        var type = Nav.FirstOrDefault(n => n.IsActive)?.PageType;
        if (shell.Nav.FirstOrDefault(n => n.PageType == type && type != typeof(PlaceholderPageViewModel)) is { } item) shell.Open(item);
        return shell;
    }

    [ObservableProperty] private PageViewModel? _page;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _revision;

    private Club Club => Session.HumanClub;
    public string ClubName => Club.Name;
    public string ClubPrimary => Club.PrimaryColor;
    public string ClubSecondary => Club.SecondaryColor;
    public string ClubInitials => Crest.InitialsOf(Club.Name);
    public IBrush HeaderBrush => new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(150, Palette.ClubColor(Club.PrimaryColor).R, Palette.ClubColor(Club.PrimaryColor).G, Palette.ClubColor(Club.PrimaryColor).B), 0),
            new GradientStop(Colors.Transparent, 0.75),
        },
    };
    public string DateText => $"{Fmt.Weekday(Session.World.Date)}, {Fmt.Date(Session.World.Date)}";
    public string SeasonText => GameSession.SeasonLabel(Session.World.SeasonYear);
    public string LeagueText
    {
        get
        {
            var league = Session.World.LeagueOf(Club);
            var row = LeagueTable.Compute(league).First(r => r.ClubId == Club.Id);
            return $"{UiText.Competition(league.Id)} · {T.Format("ui.place", row.Position)} · {row.Points} {T["col.points"]}";
        }
    }
    public string ContinueText => Session.PendingHumanFixture is null ? T["ui.continueButton"] : T["ui.toMatchButton"];
    public string MoraleText
    {
        get
        {
            var squad = Session.World.SquadOf(Club).ToList();
            var m = (Morale)(int)Math.Round(squad.Average(p => (int)p.State.Morale));
            return $"{T["status.morale"]}: {T[Ratings.MoraleKey(m)]}";
        }
    }
    public string NextText
    {
        get
        {
            var next = Session.NextFixture(Club);
            if (next is null) return T["status.noFixture"];
            var opp = Session.World.Clubs[next.HomeId == Club.Id ? next.AwayId : next.HomeId];
            return T.Format("status.next", Fmt.Weekday(next.Date), Fmt.ShortDate(next.Date), opp.Name,
                next.HomeId == Club.Id ? T["ui.homeShort"] : T["ui.awayShort"]);
        }
    }
    public int Unread => Session.Inbox.Count(m => !m.Read);
    public bool HasUnread => Unread > 0;

    /// <summary>Kopf- und Statuszeile neu berechnen (nach Weiter, Spiel, Taktikänderung).</summary>
    public void RefreshHeader()
    {
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(SeasonText));
        OnPropertyChanged(nameof(LeagueText));
        OnPropertyChanged(nameof(ContinueText));
        OnPropertyChanged(nameof(MoraleText));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(Unread));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(ClubName));
        Revision++;
    }

    [RelayCommand]
    public void Open(NavItem? item)
    {
        if (item is null) return;
        foreach (var n in Nav) n.IsActive = n == item;
        _previousPage = null;
        Page = item.Create();
        RefreshHeader();
    }

    public void OpenPage<TPage>() where TPage : PageViewModel
    {
        var item = Nav.FirstOrDefault(n => n.PageType == typeof(TPage));
        Open(item);
    }

    /// <summary>Zeigt eine Unterseite (z. B. Spielerprofil) mit Zurück zur bisherigen Seite.</summary>
    public void ShowSubPage(PageViewModel page)
    {
        _previousPage ??= Page;
        Page = page;
    }

    [RelayCommand]
    public void Back()
    {
        if (_previousPage is null) return;
        Page = _previousPage;
        _previousPage = null;
        RefreshHeader();
    }

    public bool CanGoBack => _previousPage is not null;

    [RelayCommand]
    public void Continue()
    {
        var s = Session;
        if (s.PendingHumanFixture is { } pending)
        {
            StartMatch(pending);
            return;
        }
        var result = s.Advance();
        switch (result.Reason)
        {
            case StopReason.HumanMatch:
                StatusMessage = T["ui.matchday"];
                break;
            case StopReason.SeasonEnd:
                OpenPage<LeaguePageViewModel>();
                _main.ShowDialog(new MessageDialogViewModel(T["ui.seasonEnd"], result.NewMessages.Select(m => m.SubjectText(T))));
                break;
            case StopReason.Decision:
                StatusMessage = T["ui.decisionPending"];
                OpenPage<InboxPageViewModel>();
                break;
            case StopReason.NewSeason:
            case StopReason.Messages:
                OpenPage<InboxPageViewModel>();
                break;
        }
        RefreshHeader();
        if (Page is IRefreshable r) r.Refresh();
    }

    private void StartMatch(Fixture fixture)
    {
        try
        {
            State.Autosave();
        }
        catch (IOException ex)
        {
            StatusMessage = T.Format("ui.saveFailed", ex.Message);
        }
        _main.Navigate(new MatchViewModel(_main, this, fixture));
    }

    /// <summary>Rückkehr aus dem Spieltag.</summary>
    public void ReturnFromMatch()
    {
        _main.Navigate(this);
        OpenPage<LeaguePageViewModel>();
        RefreshHeader();
    }

    [RelayCommand]
    public void Save()
    {
        try
        {
            var path = State.SaveCurrent();
            StatusMessage = T.Format("ui.saved", Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            StatusMessage = T.Format("ui.saveFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void Options() => _main.ShowDialog(new OptionsViewModel(this));

    [RelayCommand]
    private void MainMenu() =>
        _main.ShowDialog(new ConfirmDialogViewModel(T["gameMenu.mainMenu"], T["gameMenu.confirmLeave"], () =>
        {
            State.Session = null;
            _main.Navigate(new StartViewModel(_main));
        }));

    public void ShowDialog(ViewModelBase dialog) => _main.ShowDialog(dialog);

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (key >= Key.F1 && key <= Key.F10)
        {
            Open(Nav[key - Key.F1]);
            return true;
        }
        if (key == Key.S && modifiers.HasFlag(KeyModifiers.Control)) { Save(); return true; }
        if (Page?.HandleKey(key, modifiers) == true) return true;
        if (key == Key.Space) { Continue(); return true; }
        if (key == Key.Escape && CanGoBack) { Back(); return true; }
        return false;
    }
}

/// <summary>Seiten, die ihre Daten nach "Weiter" neu laden.</summary>
public interface IRefreshable
{
    void Refresh();
}

public sealed class PlaceholderPageViewModel : PageViewModel
{
    public PlaceholderPageViewModel(ShellViewModel shell, string titleKey, string textKey) : base(shell)
    {
        Title = T[titleKey];
        Text = T[textKey];
    }

    public override string Title { get; }
    public string Text { get; }
    public string Roadmap => T["placeholder.roadmap"];
}
