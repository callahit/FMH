using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

/// <summary>Spielerprofil: Stammdaten, Attribute (Anzeige je Profil), Zustand, Statistik.</summary>
public sealed class PlayerView : View
{
    private readonly IReadOnlyList<Player> _players;
    private int _index;

    public PlayerView(App app, IReadOnlyList<Player> players, int index) : base(app)
    {
        _players = players;
        _index = index;
    }

    private GameSession S => App.Session!;

    public override void Render(Canvas c)
    {
        var p = _players[_index];
        var profiles = App.Data.PositionProfiles;
        var mode = S.HumanManager.Profile.AttributeDisplay;
        var today = S.World.Date;
        var club = p.ClubId is { } cid ? S.World.Clubs[cid] : null;

        c.Fill(0, 0, c.Width, 1, ' ', Th.MenuFg, Th.MenuBg);
        c.Write(1, 0, $"{p.Name.ToUpperInvariant()}  ·  {Display.Position(p.MainPosition, T)}  ·  {club?.Name ?? T["player.noClub"]}", Th.MenuFg, Th.MenuBg);
        var nav = $"{_index + 1}/{_players.Count}";
        c.Write(c.Width - nav.Length - 1, 0, nav, Th.MenuFg, Th.MenuBg);

        // Stammdaten
        var x = 1;
        var y = 2;
        void Line(string label, string value, ConsoleColor? color = null)
        {
            c.WriteField(x, y, 15, label, Th.Dim, Th.Bg);
            c.Write(x + 16, y, value, color ?? Th.Fg, Th.Bg, 30);
            y++;
        }
        c.Write(x, y++, T["player.personal"], Th.Accent, Th.Bg);
        Line(T["player.age"], T.Format("player.ageValue", p.Age(today), Fmt.Date(p.BirthDate)));
        Line(T["player.nationality"], string.Join(", ", p.Nationalities));
        Line(T["player.body"], $"{p.Height} cm, {p.Weight} kg");
        Line(T["player.foot"], $"{T["foot." + p.Foot]} (L {p.LeftFoot}, R {p.RightFoot})");
        Line(T["player.reputation"], $"{p.Reputation}/20");
        Line(T["player.strength"], Display.Strength(Ratings.Strength(p, profiles)), Th.Accent);
        if (p.Contract is { } contract)
        {
            Line(T["player.contract"], Fmt.Date(contract.Until));
            Line(T["player.wage"], Display.Wage(contract.WeeklyWage, S.HumanManager.Profile, S.World.Country(club?.CountryCode ?? "DE").Currency, T));
        }

        y++;
        c.Write(x, y++, T["player.positions"], Th.Accent, Th.Bg);
        foreach (var (pos, suit) in p.Positions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            var rating = Ratings.EffectiveRating(p, pos, profiles);
            c.WriteField(x, y, 5, Display.Position(pos, T), Th.Fg, Th.Bg);
            c.WriteField(x + 5, y, 13, T["suit." + suit], Th.Dim, Th.Bg);
            c.Write(x + 19, y, Display.Strength(Ratings.StrengthFromRating(rating)), Th.AttributeColor((int)Math.Round(rating)), Th.Bg);
            y++;
        }

        y++;
        c.Write(x, y++, T["player.state"], Th.Accent, Th.Bg);
        Line(T["player.condition"], $"{Display.Bar(p.State.Condition, 100, 10)} {p.State.Condition:0}%", Display.ConditionColor(p.State.Condition, Th));
        Line(T["player.morale"], Display.Morale(p.State.Morale, T), Display.MoraleColor(p.State.Morale, Th));
        Line(T["player.form"], $"{p.State.Form}/10  {string.Join(" ", p.State.RecentGrades.Select(g => Fmt.Num(g, "0.0")))}");
        Line(T["player.practice"], $"{Display.Bar(p.State.MatchPractice, 100, 10)} {p.State.MatchPractice:0}%");
        if (p.State.Injury is { } inj)
            Line(T["player.injury"], T.Format("player.injuryValue", T["injury." + inj.Type], inj.DaysRemaining), Th.Bad);
        if (p.State.SuspendedMatches > 0)
            Line(T["player.suspension"], T.Format("status.suspended", p.State.SuspendedMatches), Th.Bad);

        // Attribute
        var ax = 48;
        var ay = 2;
        var perRow = 1;
        if (mode == AttributeDisplay.StrengthOnly)
        {
            c.Write(ax, ay, T["player.attributesHidden"], Th.Dim, Th.Bg, c.Width - ax - 1);
            perRow = 3;
        }
        else
        {
            var groups = p.MainPosition == Position.GK
                ? new[] { ("group.goalkeeping", AttrInfo.Goalkeeping), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) }
                : new[] { ("group.technical", AttrInfo.Technical), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) };
            var valueWidth = mode switch { AttributeDisplay.Bars => 10, AttributeDisplay.Stars => 6, _ => 3 };
            const int nameWidth = 18;
            var colWidth = nameWidth + valueWidth + 2;
            perRow = Math.Clamp((c.Width - ax - 1) / colWidth, 1, 3);
            for (var gi = 0; gi < groups.Length; gi++)
            {
                var gx = ax + gi % perRow * colWidth;
                var gy = ay + gi / perRow * 15;
                var (key, attrs) = groups[gi];
                c.Write(gx, gy, T[key], Th.Accent, Th.Bg);
                for (var i = 0; i < attrs.Length; i++)
                {
                    var a = attrs[i];
                    var v = p.Attributes[a];
                    c.WriteField(gx, gy + 1 + i, nameWidth, T[AttrInfo.TextKey(a)], Th.Fg, Th.Bg);
                    c.Write(gx + nameWidth + 1, gy + 1 + i, Display.Attribute(v, mode), Th.AttributeColor(v), Th.Bg);
                }
            }
        }

        // Statistik: rechts unter den Attributen, wenn sie in eine Reihe passen, sonst links unten.
        var (sx, sy) = perRow >= 3 ? (ax, 18) : (1, y + 1);
        if (sy < c.Height - 4)
        {
            c.Write(sx, sy, T["player.stats"], Th.Accent, Th.Bg);
            Draw.TableHeader(c, sx, sy + 1, Th, (T["col.season"], 8, Align.Left), (T["col.apps"], 4, Align.Right), (T["col.minutes"], 5, Align.Right),
                (T["col.goals"], 4, Align.Right), (T["col.assists"], 4, Align.Right), (T["col.yellow"], 3, Align.Right), (T["col.red"], 3, Align.Right), (T["col.grade"], 5, Align.Right));
            var rows = new[] { p.SeasonStats }.Concat(p.History.AsEnumerable().Reverse()).Where(r => r.SeasonYear > 0).Take(Math.Max(1, c.Height - sy - 5));
            var ry = sy + 2;
            foreach (var r in rows)
            {
                var cells = new[]
                {
                    (GameSession.SeasonLabel(r.SeasonYear), 8, Align.Left), (r.Appearances.ToString(), 4, Align.Right), (r.Minutes.ToString(), 5, Align.Right),
                    (r.Goals.ToString(), 4, Align.Right), (r.Assists.ToString(), 4, Align.Right), (r.YellowCards.ToString(), 3, Align.Right),
                    (r.RedCards.ToString(), 3, Align.Right), (Display.Grade(r.AverageGrade), 5, Align.Right),
                };
                var cx = sx;
                foreach (var (text, w, align) in cells)
                {
                    c.WriteField(cx, ry, w, text, Th.Fg, Th.Bg, align);
                    cx += w + 1;
                }
                ry++;
            }
        }

        Draw.KeyHelp(c, c.Height - 1, Th, T["help.player"]);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter:
                App.Pop();
                break;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.UpArrow:
                _index = (_index + _players.Count - 1) % _players.Count;
                break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.DownArrow:
                _index = (_index + 1) % _players.Count;
                break;
        }
    }
}
