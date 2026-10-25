using Fmh.Core;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>Spielplan: eigene Spiele oder kompletter Spieltag der Liga (umschaltbar mit L).</summary>
public sealed class FixturesPage : Page
{
    private readonly ListState _list = new();
    private bool _leagueView;
    private int _matchday;

    public FixturesPage(GameView shell) : base(shell) { }

    public override string TitleKey => "menu.fixtures";
    public override string HelpKey => _leagueView ? "help.page.fixturesLeague" : "help.page.fixturesOwn";

    public override void OnShow()
    {
        var league = S.World.LeagueOf(S.HumanClub);
        _matchday = Math.Max(1, Math.Min(league.MatchdayCount, league.CurrentMatchday + 1));
        var own = OwnFixtures();
        _list.Index = Math.Max(0, own.FindIndex(f => f.Result is null));
    }

    private List<Fixture> OwnFixtures() =>
        S.AllFixtures.Where(f => f.Involves(S.HumanClub.Id)).OrderBy(f => f.Date).ToList();

    public override void Render(Canvas c, Rect area)
    {
        if (_leagueView) RenderMatchday(c, area);
        else RenderOwn(c, area);
    }

    private void RenderOwn(Canvas c, Rect area)
    {
        var club = S.HumanClub;
        var fixtures = OwnFixtures();
        Title(c, area, T["fixtures.own"], GameSessionLabel());
        Draw.TableHeader(c, area.X, area.Y + 2, Th, (T["col.matchday"], 3, Align.Right), (T["col.date"], 10, Align.Left),
            (T["col.ha"], 3, Align.Center), (T["col.opponent"], 26, Align.Left), (T["col.result"], 7, Align.Center), (T["col.scorers"], area.W - 55, Align.Left));
        var rows = area.H - 4;
        var start = _list.VisibleStart(rows, fixtures.Count);
        for (var i = 0; i < rows && start + i < fixtures.Count; i++)
        {
            var f = fixtures[start + i];
            var y = area.Y + 3 + i;
            var sel = start + i == _list.Index;
            var home = f.HomeId == club.Id;
            var opp = S.World.Clubs[home ? f.AwayId : f.HomeId];
            var fg = sel ? Th.SelFg : Th.Fg;
            var bg = sel ? Th.SelBg : Th.Bg;
            c.Fill(area.X, y, area.W, 1, ' ', fg, bg);
            c.WriteField(area.X, y, 3, f.Matchday.ToString(), fg, bg, Align.Right);
            c.WriteField(area.X + 4, y, 10, $"{Fmt.Weekday(f.Date)} {Fmt.ShortDate(f.Date)}", fg, bg);
            c.WriteField(area.X + 15, y, 3, home ? T["ui.homeShort"] : T["ui.awayShort"], fg, bg, Align.Center);
            c.WriteField(area.X + 19, y, 26, opp.Name, fg, bg);
            if (f.Result is { } r)
            {
                var own = home ? r.HomeGoals : r.AwayGoals;
                var other = home ? r.AwayGoals : r.HomeGoals;
                var color = sel ? fg : own > other ? Th.Good : own < other ? Th.Bad : Th.Warn;
                c.WriteField(area.X + 46, y, 7, $"{r.HomeGoals}:{r.AwayGoals}", color, bg, Align.Center);
                var scorers = string.Join(", ", r.Goals.Where(g => g.Home == home).Select(g => S.World.Players[g.ScorerId].LastName + (g.Penalty ? " (E)" : "")));
                c.WriteField(area.X + 54, y, area.W - 54, scorers, sel ? fg : Th.Dim, bg);
            }
            else c.WriteField(area.X + 46, y, 7, "-:-", sel ? fg : Th.Dim, bg, Align.Center);
        }
    }

    private void RenderMatchday(Canvas c, Rect area)
    {
        var league = S.World.LeagueOf(S.HumanClub);
        var fixtures = league.Fixtures.Where(f => f.Matchday == _matchday).ToList();
        var date = fixtures.Count > 0 ? $"{Fmt.Weekday(fixtures[0].Date)}, {Fmt.Date(fixtures[0].Date)}" : "";
        Title(c, area, T.Format("fixtures.matchday", league.Name, _matchday, league.MatchdayCount), date);
        var y = area.Y + 2;
        foreach (var f in fixtures)
        {
            var own = f.Involves(S.HumanClub.Id);
            var fg = own ? Th.Accent : Th.Fg;
            c.WriteField(area.X, y, 26, S.World.Clubs[f.HomeId].Name, fg, Th.Bg, Align.Right);
            c.WriteField(area.X + 27, y, 7, f.Result is { } r ? $"{r.HomeGoals}:{r.AwayGoals}" : "-:-", fg, Th.Bg, Align.Center);
            c.WriteField(area.X + 35, y, 26, S.World.Clubs[f.AwayId].Name, fg, Th.Bg);
            if (f.Result is { } res && area.W > 75)
                c.Write(area.X + 62, y, $"({res.HalfTimeHome}:{res.HalfTimeAway})  {Fmt.Int(res.Attendance)}", Th.Dim, Th.Bg, area.W - 62);
            y++;
        }
    }

    private string GameSessionLabel() => Core.Game.GameSession.SeasonLabel(S.World.SeasonYear);

    public override bool Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.L)
        {
            _leagueView = !_leagueView;
            return true;
        }
        if (_leagueView)
        {
            var league = S.World.LeagueOf(S.HumanClub);
            if (key.Key == ConsoleKey.LeftArrow) { _matchday = Math.Max(1, _matchday - 1); return true; }
            if (key.Key == ConsoleKey.RightArrow) { _matchday = Math.Min(league.MatchdayCount, _matchday + 1); return true; }
            return false;
        }
        return _list.HandleKey(key, OwnFixtures().Count);
    }
}
