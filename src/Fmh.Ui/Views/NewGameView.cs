using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

/// <summary>Neues Spiel (Kapitel 2): Manager, Werdegang, Verein, Weltregeln.</summary>
public sealed class NewGameView : View
{
    private static readonly (string key, int reputation)[] Backgrounds =
    {
        ("background.hobby", 2), ("background.amateur", 4), ("background.pro", 8), ("background.international", 12),
    };
    private static readonly double[] InjuryLevels = { 0.5, 1.0, 1.6 };

    private readonly TextField _name;
    private readonly TextField _seed;
    private readonly List<ClubDefinition> _clubs;
    private int _row;
    private int _club;
    private int _background = 1;
    private int _injuries = 1;
    private bool _ironman;

    private const int Rows = 7;

    public NewGameView(App app) : base(app)
    {
        _name = new TextField(Environment.UserName is { Length: > 0 } u ? u : "Manager", 24);
        _seed = new TextField(((ulong)DateTime.Now.Ticks % 100_000_000UL).ToString(), 12);
        _clubs = App.Data.Clubs.OrderBy(c => c.Division).ThenByDescending(c => c.Reputation).ThenBy(c => c.Name).ToList();
        var preferred = _clubs.FindIndex(c => c.Key == "nordheide");
        _club = preferred >= 0 ? preferred : 0;
    }

    public override void Render(Canvas c)
    {
        var th = Theme.Classic;
        c.Clear(th.Fg, th.Bg);
        c.Write(2, 1, T["newGame.title"], th.Accent, th.Bg);
        c.HLine(2, 2, c.Width - 4, th.Border, th.Bg);

        var club = _clubs[_club];
        var division = App.Data.Countries.SelectMany(x => x.Divisions).FirstOrDefault(d => d.Id == club.Division);
        var values = new[]
        {
            _name.Value + (_row == 0 ? "_" : ""),
            T[Backgrounds[_background].key],
            $"{club.Name}  ({division?.Name ?? club.Division}, {T["newGame.reputation"]} {club.Reputation})",
            _seed.Value + (_row == 3 ? "_" : ""),
            T[$"newGame.injuries.{_injuries}"],
            _ironman ? T["ui.yes"] : T["ui.no"],
            "",
        };
        var labels = new[] { "newGame.name", "newGame.background", "newGame.club", "newGame.seed", "newGame.injuryRule", "newGame.ironman", "newGame.start" };

        for (var i = 0; i < Rows; i++)
        {
            var y = 4 + i * 2;
            var sel = i == _row;
            if (i == Rows - 1)
            {
                c.WriteField(4, y + 1, 30, T[labels[i]], sel ? th.SelFg : th.Accent, sel ? th.SelBg : th.Bg, Align.Center);
                continue;
            }
            c.WriteField(4, y, 24, T[labels[i]], th.Fg, th.Bg);
            c.WriteField(30, y, c.Width - 34, (i is 1 or 2 or 4 or 5 ? "◄ " : "") + values[i] + (i is 1 or 2 or 4 or 5 ? " ►" : ""),
                sel ? th.SelFg : th.Fg, sel ? th.SelBg : th.Bg);
        }

        var infoY = 4 + Rows * 2 + 2;
        c.Write(4, infoY, T["newGame.clubInfo"], th.Accent, th.Bg);
        c.Write(4, infoY + 1, $"{club.City} · {club.Stadium} ({Fmt.Int(club.Capacity)} {T["unit.seats"]})", th.Fg, th.Bg);
        c.Write(4, infoY + 2, T.Format("newGame.expectation", T[ExpectationKey(club, division)]), th.Fg, th.Bg);
        c.Write(4, infoY + 4, T[Backgrounds[_background].key + ".info"], th.Dim, th.Bg, c.Width - 8);

        Draw.KeyHelp(c, c.Height - 1, th, T["help.newGame"]);
    }

    private static string ExpectationKey(ClubDefinition club, DivisionConfig? division) => club.Reputation switch
    {
        >= 17 => "expectation.title",
        >= 14 => "expectation.europe",
        >= 11 => "expectation.midtable",
        >= 9 when division?.Promotion > 0 => "expectation.promotion",
        _ => "expectation.survival",
    };

    public override void Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape: App.Pop(); return;
            case ConsoleKey.UpArrow: _row = (_row + Rows - 1) % Rows; return;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab: _row = (_row + 1) % Rows; return;
            case ConsoleKey.LeftArrow: Change(-1); return;
            case ConsoleKey.RightArrow: Change(+1); return;
            case ConsoleKey.Enter:
                if (_row == 2) PickClub();
                else if (_row == Rows - 1) Start();
                else _row++;
                return;
        }
        if (_row == 0) _name.Handle(key);
        if (_row == 3 && (char.IsDigit(key.KeyChar) || key.Key == ConsoleKey.Backspace)) _seed.Handle(key);
    }

    private void Change(int d)
    {
        switch (_row)
        {
            case 1: _background = (_background + Backgrounds.Length + d) % Backgrounds.Length; break;
            case 2: _club = (_club + _clubs.Count + d) % _clubs.Count; break;
            case 4: _injuries = Math.Clamp(_injuries + d, 0, InjuryLevels.Length - 1); break;
            case 5: _ironman = !_ironman; break;
        }
    }

    private void PickClub()
    {
        var items = _clubs.Select(c =>
        {
            var div = App.Data.Countries.SelectMany(x => x.Divisions).FirstOrDefault(d => d.Id == c.Division)?.Name ?? c.Division;
            return $"{c.Name,-28} {div,-8} {T["newGame.reputation"]} {c.Reputation,2}";
        }).ToList();
        App.Push(new ListDialog(App, T["newGame.club"], items, i => _club = i, _club));
    }

    private void Start()
    {
        var name = string.IsNullOrWhiteSpace(_name.Value) ? "Manager" : _name.Value.Trim();
        var seed = ulong.TryParse(_seed.Value, out var s) ? s : 1UL;
        var world = WorldGenerator.Create(App.Data, new NewGameOptions
        {
            Seed = seed,
            ManagerName = name,
            ClubKey = _clubs[_club].Key,
            Rules = new WorldRules { InjuryFrequency = InjuryLevels[_injuries], Ironman = _ironman },
        });
        world.Managers[0].Reputation = Backgrounds[_background].reputation;
        App.Session = new GameSession(world, App.Data, App.Engine);
        App.Reset(new GameView(App));
    }
}
