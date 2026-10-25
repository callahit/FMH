using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Pages;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

public readonly record struct Rect(int X, int Y, int W, int H)
{
    public int Bottom => Y + H;
    public int Right => X + W;
}

/// <summary>
/// Grundlayout (Kapitel 11): Kopfzeile mit Verein, Datum, Liga, Platz und [WEITER &gt;&gt;],
/// Menü links (F1–F10), Hauptbereich, Statuszeile unten.
/// </summary>
public sealed class GameView : View
{
    private const int MenuWidth = 16;
    private readonly Page[] _pages;
    private int _page;

    public GameView(App app) : base(app)
    {
        _pages = new Page[]
        {
            new InboxPage(this),
            new SquadPage(this),
            new TacticsPage(this),
            new PlaceholderPage(this, "menu.training", "placeholder.training"),
            new FixturesPage(this),
            new PlaceholderPage(this, "menu.transfers", "placeholder.transfers"),
            new PlaceholderPage(this, "menu.finances", "placeholder.finances"),
            new PlaceholderPage(this, "menu.stadium", "placeholder.stadium"),
            new ClubPage(this),
            new LeaguePage(this),
        };
        _pages[0].OnShow();
    }

    public App Application => App;
    public GameSession Session => App.Session!;

    public void ShowPage<TPage>() where TPage : Page
    {
        var idx = Array.FindIndex(_pages, p => p is TPage);
        if (idx >= 0) _page = idx;
    }

    public override void OnResume() => _pages[_page].OnShow();

    public override void Render(Canvas c)
    {
        var s = Session;
        var club = s.HumanClub;
        var league = s.World.LeagueOf(club);
        var table = LeagueTable.Compute(league);
        var row = table.First(r => r.ClubId == club.Id);

        // Kopfzeile in Vereinsfarben
        var headBg = Display.ClubColor(club.PrimaryColor, Th.MenuBg);
        var headFg = Display.ContrastOn(headBg);
        c.Fill(0, 0, c.Width, 1, ' ', headFg, headBg);
        c.Write(1, 0, club.Name.ToUpperInvariant(), headFg, headBg);
        var date = $"{Fmt.Weekday(s.World.Date)}, {Fmt.Date(s.World.Date)}";
        var info = $"{date}   {league.Name}  {T.Format("ui.place", row.Position)}";
        c.Write(Math.Max(club.Name.Length + 4, (c.Width - info.Length) / 2), 0, info, headFg, headBg);
        var cont = s.PendingHumanFixture is null ? T["ui.continue"] : T["ui.toMatch"];
        c.Write(c.Width - cont.Length - 1, 0, cont, headFg, headBg);

        // Menü
        for (var i = 0; i < _pages.Length; i++)
        {
            var sel = i == _page;
            var placeholder = _pages[i] is PlaceholderPage;
            var label = $"F{i + 1,-2} {T[_pages[i].TitleKey]}";
            c.WriteField(0, 2 + i, MenuWidth, label, sel ? Th.SelFg : placeholder ? Th.Dim : Th.Fg, sel ? Th.SelBg : Th.Bg);
        }
        for (var y = 1; y < c.Height - 2; y++) c.Set(MenuWidth, y, '│', Th.Border, Th.Bg);
        c.HLine(0, 1, c.Width, Th.Border, Th.Bg);
        c.Set(MenuWidth, 1, '┬', Th.Border, Th.Bg);

        var area = new Rect(MenuWidth + 2, 2, c.Width - MenuWidth - 3, c.Height - 5);
        _pages[_page].Render(c, area);

        // Statuszeile
        c.HLine(0, c.Height - 3, c.Width, Th.Border, Th.Bg);
        c.Set(MenuWidth, c.Height - 3, '┴', Th.Border, Th.Bg);
        var squad = s.World.SquadOf(club).ToList();
        var morale = (Morale)(int)Math.Round(squad.Average(p => (int)p.State.Morale));
        var next = s.NextFixture(club);
        var nextText = next is null ? T["status.noFixture"] : T.Format("status.next",
            Fmt.Weekday(next.Date), Fmt.ShortDate(next.Date),
            s.World.Clubs[next.HomeId == club.Id ? next.AwayId : next.HomeId].Name,
            next.HomeId == club.Id ? T["ui.homeShort"] : T["ui.awayShort"]);
        var unread = s.Inbox.Count(m => !m.Read);
        var status = $"{T["status.morale"]}: {Display.Morale(morale, T)} │ {T["status.points"]}: {row.Points} │ {nextText}" +
                     (unread > 0 ? $" │ {T.Format("status.unread", unread)}" : "");
        c.WriteField(1, c.Height - 2, c.Width - 2, App.StatusMessage ?? status, App.StatusMessage is null ? Th.Fg : Th.Warn, Th.Bg);
        var pageHelp = T[_pages[_page].HelpKey];
        Draw.KeyHelp(c, c.Height - 1, Th, pageHelp.Length == 0 ? T["help.global"] : pageHelp + "  " + T["help.global"]);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (key.Key >= ConsoleKey.F1 && key.Key <= ConsoleKey.F10)
        {
            _page = key.Key - ConsoleKey.F1;
            _pages[_page].OnShow();
            return;
        }
        if (key.Key == ConsoleKey.Tab)
        {
            var d = (key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1;
            _page = (_page + _pages.Length + d) % _pages.Length;
            _pages[_page].OnShow();
            return;
        }
        if (key.Key == ConsoleKey.S && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            Save();
            return;
        }
        if (_pages[_page].Handle(key)) return;
        if (key.Key == ConsoleKey.Spacebar) { Continue(); return; }
        if (key.Key == ConsoleKey.Escape) OpenGameMenu();
    }

    private void Save()
    {
        try
        {
            var path = App.SaveCurrent();
            App.StatusMessage = T.Format("ui.saved", Path.GetFileName(path));
        }
        catch (IOException ex)
        {
            App.StatusMessage = T.Format("ui.saveFailed", ex.Message);
        }
    }

    /// <summary>"Weiter": bis zum nächsten Stopp-Ereignis rechnen.</summary>
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
                App.StatusMessage = T["ui.matchday"];
                break;
            case StopReason.SeasonEnd:
                ShowPage<LeaguePage>();
                App.Push(new MessageDialog(App, T["ui.seasonEnd"], result.NewMessages.Select(m => m.Subject).ToList()));
                break;
            case StopReason.NewSeason:
            case StopReason.Messages:
                ShowPage<InboxPage>();
                _pages[_page].OnShow();
                break;
        }
    }

    private void StartMatch(Fixture fixture)
    {
        try
        {
            App.Autosave();
        }
        catch (IOException ex)
        {
            App.StatusMessage = T.Format("ui.saveFailed", ex.Message);
        }
        App.Push(new MatchView(App, fixture));
    }

    private void OpenGameMenu()
    {
        var items = new[] { T["gameMenu.resume"], T["gameMenu.save"], T["gameMenu.options"], T["gameMenu.mainMenu"], T["gameMenu.quit"] };
        App.Push(new ListDialog(App, T["gameMenu.title"], items, i =>
        {
            switch (i)
            {
                case 1: Save(); break;
                case 2: App.Push(new OptionsView(App)); break;
                case 3: App.Session = null; App.Reset(new MainMenuView(App)); break;
                case 4: App.Quit(); break;
            }
        }));
    }
}
