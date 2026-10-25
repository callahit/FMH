using Fmh.Core.Game;
using Fmh.Core.Persistence;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

public sealed class MainMenuView : View
{
    private static readonly string[] Logo =
    {
        " ███████╗███╗   ███╗██╗  ██╗",
        " ██╔════╝████╗ ████║██║  ██║",
        " █████╗  ██╔████╔██║███████║",
        " ██╔══╝  ██║╚██╔╝██║██╔══██║",
        " ██║     ██║ ╚═╝ ██║██║  ██║",
        " ╚═╝     ╚═╝     ╚═╝╚═╝  ╚═╝",
    };

    private readonly ListState _list = new();
    private readonly string[] _keys = { "menu.newGame", "menu.load", "menu.quit" };

    public MainMenuView(App app) : base(app) { }

    public override void Render(Canvas c)
    {
        var th = Theme.Classic;
        c.Clear(th.Fg, th.Bg);
        var y = Math.Max(1, c.Height / 2 - 10);
        foreach (var line in Logo)
        {
            c.Write((c.Width - 29) / 2, y++, line, th.Accent, th.Bg);
        }
        y++;
        var subtitle = T["app.subtitle"];
        c.Write((c.Width - subtitle.Length) / 2, y++, subtitle, th.Fg, th.Bg);
        var tagline = T["app.tagline"];
        c.Write((c.Width - tagline.Length) / 2, y++, tagline, th.Dim, th.Bg);
        y += 2;

        for (var i = 0; i < _keys.Length; i++)
        {
            var text = T[_keys[i]];
            var sel = i == _list.Index;
            c.WriteField((c.Width - 30) / 2, y + i * 2, 30, text, sel ? th.SelFg : th.Fg, sel ? th.SelBg : th.Bg, Align.Center);
        }
        if (App.StatusMessage is { } msg)
            c.Write((c.Width - msg.Length) / 2, c.Height - 3, msg, th.Warn, th.Bg);
        Draw.KeyHelp(c, c.Height - 1, th, T["help.mainMenu"]);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key, _keys.Length)) return;
        if (key.Key == ConsoleKey.Escape) { App.Quit(); return; }
        if (key.Key != ConsoleKey.Enter) return;
        switch (_list.Index)
        {
            case 0: App.Push(new NewGameView(App)); break;
            case 1: OpenLoad(); break;
            default: App.Quit(); break;
        }
    }

    private void OpenLoad()
    {
        var saves = SaveGame.List(App.SaveDirectory);
        if (saves.Count == 0)
        {
            App.Push(new MessageDialog(App, T["menu.load"], new[] { T["load.none"] }));
            return;
        }
        var items = saves.Select(s => $"{s.Name,-32} {s.Modified:dd.MM.yyyy HH:mm}").ToList();
        App.Push(new ListDialog(App, T["menu.load"], items, i =>
        {
            try
            {
                var world = SaveGame.Load(saves[i].Path);
                App.Session = new GameSession(world, App.Data, App.Engine);
                App.Reset(new GameView(App));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                App.Push(new MessageDialog(App, T["load.error"], new[] { ex.Message }));
            }
        }));
    }
}
