using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>Kaderliste mit sortierbaren Spalten; Enter öffnet das Spielerprofil.</summary>
public sealed class SquadPage : Page
{
    private sealed record Column(string TitleKey, int Width, Align Align, int Priority,
                                 Func<Player, string> Text, Func<Player, IComparable> Sort, Func<Player, ConsoleColor?>? Color = null);

    private readonly ListState _list = new();
    private readonly List<Column> _columns;
    private int _sort = 3;
    private bool _descending = true;

    public SquadPage(GameView shell) : base(shell)
    {
        var profiles = App.Data.PositionProfiles;
        _columns = new List<Column>
        {
            new("col.pos", 4, Align.Left, 0, p => Display.Position(p.MainPosition, T), p => (int)p.MainPosition),
            new("col.name", 22, Align.Left, 0, p => p.Name, p => p.LastName),
            new("col.age", 3, Align.Right, 1, p => p.Age(S.World.Date).ToString(), p => p.Age(S.World.Date)),
            new("col.strength", 7, Align.Right, 0, p => Display.Strength(Ratings.Strength(p, profiles)), p => Ratings.Strength(p, profiles),
                p => Th.AttributeColor((int)Math.Round(Ratings.PositionRating(p, p.MainPosition, profiles)))),
            new("col.condition", 5, Align.Right, 1, p => $"{p.State.Condition:0}%", p => p.State.Condition, p => Display.ConditionColor(p.State.Condition, Th)),
            new("col.morale", 11, Align.Left, 2, p => Display.Morale(p.State.Morale, T), p => (int)p.State.Morale, p => Display.MoraleColor(p.State.Morale, Th)),
            new("col.form", 4, Align.Right, 3, p => p.State.Form.ToString(), p => p.State.Form),
            new("col.status", 13, Align.Left, 1, p => Display.Status(p, S.HumanClub, T), p => StatusOrder(p),
                p => p.State.Injury is not null || p.State.SuspendedMatches > 0 ? Th.Bad : null),
            new("col.apps", 3, Align.Right, 4, p => p.SeasonStats.Appearances.ToString(), p => p.SeasonStats.Appearances),
            new("col.goals", 4, Align.Right, 4, p => p.SeasonStats.Goals.ToString(), p => p.SeasonStats.Goals),
            new("col.grade", 4, Align.Right, 3, p => Display.Grade(p.SeasonStats.AverageGrade), p => -(p.SeasonStats.AverageGrade ?? 9),
                p => Display.GradeColor(p.SeasonStats.AverageGrade, Th)),
            new("col.contract", 7, Align.Right, 5, p => p.Contract is { } c ? c.Until.ToString("MM/yyyy") : "–", p => p.Contract?.Until ?? DateOnly.MinValue),
            new("col.wage", 18, Align.Right, 6, p => p.Contract is { } c ? Display.Wage(c.WeeklyWage, S.HumanManager.Profile, S.World.Country(S.HumanClub.CountryCode).Currency, T) : "–",
                p => p.Contract?.WeeklyWage ?? 0),
        };
    }

    public override string TitleKey => "menu.squad";
    public override string HelpKey => "help.page.squad";

    private int StatusOrder(Player p)
    {
        var club = S.HumanClub;
        if (club.Tactic.Lineup.Contains(p.Id)) return 3;
        if (club.Tactic.Bench.Contains(p.Id)) return 2;
        return p.IsAvailable ? 1 : 0;
    }

    public List<Player> SortedSquad()
    {
        var squad = S.World.SquadOf(S.HumanClub).ToList();
        var col = _columns[_sort];
        var ordered = _descending ? squad.OrderByDescending(col.Sort) : squad.OrderBy(col.Sort);
        return ordered.ThenBy(p => (int)p.MainPosition).ThenBy(p => p.LastName).ToList();
    }

    private List<Column> VisibleColumns(int width)
    {
        // Spalten mit niedriger Priorität fallen bei schmalen Fenstern weg.
        var visible = _columns.ToList();
        while (visible.Sum(c => c.Width + 1) > width && visible.Any(c => c.Priority > 0))
            visible.Remove(visible.Where(c => c.Priority > 0).MaxBy(c => c.Priority)!);
        return visible;
    }

    public override void Render(Canvas c, Rect area)
    {
        var squad = SortedSquad();
        Title(c, area, T["menu.squad"], T.Format("squad.count", squad.Count, S.World.Country(S.HumanClub.CountryCode).Squad.MaxSquad));
        var cols = VisibleColumns(area.W);
        var x = area.X;
        foreach (var col in cols)
        {
            var title = T[col.TitleKey] + (col == _columns[_sort] ? (_descending ? "▼" : "▲") : "");
            c.WriteField(x, area.Y + 2, col.Width, title, col == _columns[_sort] ? Th.Accent : Th.Dim, Th.Bg, col.Align);
            x += col.Width + 1;
        }

        var rows = area.H - 4;
        var start = _list.VisibleStart(rows, squad.Count);
        for (var i = 0; i < rows && start + i < squad.Count; i++)
        {
            var p = squad[start + i];
            var y = area.Y + 3 + i;
            var sel = start + i == _list.Index;
            x = area.X;
            foreach (var col in cols)
            {
                var fg = sel ? Th.SelFg : col.Color?.Invoke(p) ?? Th.Fg;
                c.WriteField(x, y, col.Width, col.Text(p), fg, sel ? Th.SelBg : Th.Bg, col.Align);
                x += col.Width + 1;
            }
        }
    }

    public override bool Handle(ConsoleKeyInfo key)
    {
        var squad = SortedSquad();
        if (_list.HandleKey(key, squad.Count)) return true;
        switch (key.Key)
        {
            case ConsoleKey.Enter when squad.Count > 0:
                App.Push(new PlayerView(App, squad, _list.Index));
                return true;
            case ConsoleKey.S:
                _sort = (_sort + 1) % _columns.Count;
                return true;
            case ConsoleKey.R:
                _descending = !_descending;
                return true;
        }
        return false;
    }
}
