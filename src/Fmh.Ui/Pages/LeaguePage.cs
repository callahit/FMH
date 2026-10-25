using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>Liga und Welt: Tabelle mit Auf-/Abstiegszonen, Torjägerliste, andere Ligen.</summary>
public sealed class LeaguePage : Page
{
    private int _leagueIndex = -1;
    private int _view; // 0 = Tabelle, 1 = Torjäger

    public LeaguePage(GameView shell) : base(shell) { }

    public override string TitleKey => "menu.league";
    public override string HelpKey => "help.page.league";

    public override void OnShow()
    {
        _leagueIndex = S.World.Leagues.FindIndex(l => l.Id == S.HumanClub.LeagueId);
    }

    private League League
    {
        get
        {
            if (_leagueIndex < 0) OnShow();
            return S.World.Leagues[_leagueIndex];
        }
    }

    public override void Render(Canvas c, Rect area)
    {
        if (_view == 1) RenderScorers(c, area);
        else RenderTable(c, area);
    }

    private void RenderTable(Canvas c, Rect area)
    {
        var league = League;
        var table = LeagueTable.Compute(league);
        Title(c, area, $"{league.Name} – {T["league.table"]}", T.Format("league.after", league.CurrentMatchday));
        Draw.TableHeader(c, area.X, area.Y + 2, Th, ("#", 3, Align.Right), (T["col.club"], 26, Align.Left), (T["col.played"], 3, Align.Right),
            (T["col.won"], 3, Align.Right), (T["col.drawn"], 3, Align.Right), (T["col.lost"], 3, Align.Right), (T["col.goals"], 7, Align.Center),
            (T["col.diff"], 4, Align.Right), (T["col.points"], 4, Align.Right), (T["col.form"], 9, Align.Left));
        var y = area.Y + 3;
        var promotion = league.Format.Promotion;
        var relegation = league.Format.Relegation;
        foreach (var row in table)
        {
            if (y >= area.Bottom) break;
            var club = S.World.Clubs[row.ClubId];
            var own = club.Id == S.HumanClub.Id;
            var zone = row.Position <= promotion || (league.Level == 1 && row.Position == 1) ? Th.Good
                : row.Position > table.Count - relegation ? Th.Bad : Th.Dim;
            var fg = own ? Th.Accent : Th.Fg;
            c.Write(area.X - 1, y, "▌", zone, Th.Bg);
            var cells = new (string, int, Align)[]
            {
                (row.Position.ToString(), 3, Align.Right), (club.Name, 26, Align.Left), (row.Played.ToString(), 3, Align.Right),
                (row.Won.ToString(), 3, Align.Right), (row.Drawn.ToString(), 3, Align.Right), (row.Lost.ToString(), 3, Align.Right),
                ($"{row.GoalsFor}:{row.GoalsAgainst}", 7, Align.Center), (row.GoalDifference.ToString("+0;-0;0"), 4, Align.Right),
                (row.Points.ToString(), 4, Align.Right),
            };
            var x = area.X;
            foreach (var (text, w, align) in cells)
            {
                c.WriteField(x, y, w, text, fg, Th.Bg, align);
                x += w + 1;
            }
            foreach (var f in row.Form)
            {
                var color = f == 'S' ? Th.Good : f == 'N' ? Th.Bad : Th.Warn;
                c.Set(x, y, T["form." + f][0], color, Th.Bg);
                x += 2;
            }
            y++;
        }
        if (y + 1 < area.Bottom)
            c.Write(area.X, y + 1, promotion > 0 ? T.Format("league.legend", promotion, relegation) : T.Format("league.legendTop", relegation), Th.Dim, Th.Bg, area.W);
    }

    private void RenderScorers(Canvas c, Rect area)
    {
        var league = League;
        Title(c, area, $"{league.Name} – {T["league.scorers"]}");
        var scorers = league.ClubIds.SelectMany(id => S.World.SquadOf(S.World.Clubs[id]))
            .Where(p => p.SeasonStats.SeasonYear == S.World.SeasonYear && (p.SeasonStats.Goals > 0 || p.SeasonStats.Assists > 0))
            .OrderByDescending(p => p.SeasonStats.Goals).ThenByDescending(p => p.SeasonStats.Assists).ThenBy(p => p.LastName)
            .Take(area.H - 4)
            .ToList();
        Draw.TableHeader(c, area.X, area.Y + 2, Th, ("#", 3, Align.Right), (T["col.name"], 24, Align.Left), (T["col.club"], 24, Align.Left),
            (T["col.goals"], 4, Align.Right), (T["col.assists"], 4, Align.Right), (T["col.apps"], 4, Align.Right), (T["col.grade"], 5, Align.Right));
        var y = area.Y + 3;
        for (var i = 0; i < scorers.Count; i++)
        {
            var p = scorers[i];
            var club = S.World.Clubs[p.ClubId!.Value];
            var fg = club.Id == S.HumanClub.Id ? Th.Accent : Th.Fg;
            var cells = new (string, int, Align)[]
            {
                ((i + 1).ToString(), 3, Align.Right), (p.Name, 24, Align.Left), (club.Name, 24, Align.Left),
                (p.SeasonStats.Goals.ToString(), 4, Align.Right), (p.SeasonStats.Assists.ToString(), 4, Align.Right),
                (p.SeasonStats.Appearances.ToString(), 4, Align.Right), (Display.Grade(p.SeasonStats.AverageGrade), 5, Align.Right),
            };
            var x = area.X;
            foreach (var (text, w, align) in cells)
            {
                c.WriteField(x, y, w, text, fg, Th.Bg, align);
                x += w + 1;
            }
            y++;
        }
        if (scorers.Count == 0) c.Write(area.X, y, T["league.noScorers"], Th.Dim, Th.Bg);
    }

    public override bool Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _leagueIndex = (_leagueIndex + S.World.Leagues.Count - 1) % S.World.Leagues.Count;
                return true;
            case ConsoleKey.RightArrow:
                _leagueIndex = (_leagueIndex + 1) % S.World.Leagues.Count;
                return true;
            case ConsoleKey.T:
                _view = _view == 1 ? 0 : 1;
                return true;
        }
        return false;
    }
}
