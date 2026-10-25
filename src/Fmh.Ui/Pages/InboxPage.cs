using Fmh.Core;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>Posteingang: Nachrichtenliste oben, gewählte Nachricht unten.</summary>
public sealed class InboxPage : Page
{
    private readonly ListState _list = new();

    public InboxPage(GameView shell) : base(shell) { }

    public override string TitleKey => "menu.inbox";
    public override string HelpKey => "help.page.inbox";

    public override void OnShow()
    {
        _list.Index = 0;
        MarkRead();
    }

    private void MarkRead()
    {
        var messages = S.Inbox.ToList();
        _list.Clamp(messages.Count);
        if (messages.Count > 0) messages[_list.Index].Read = true;
    }

    public override void Render(Canvas c, Rect area)
    {
        var messages = S.Inbox.ToList();
        Title(c, area, T["menu.inbox"], T.Format("inbox.count", messages.Count(m => !m.Read), messages.Count));
        if (messages.Count == 0)
        {
            c.Write(area.X, area.Y + 2, T["inbox.empty"], Th.Dim, Th.Bg);
            return;
        }

        var listHeight = Math.Max(3, area.H / 2 - 2);
        var start = _list.VisibleStart(listHeight, messages.Count);
        for (var i = 0; i < listHeight && start + i < messages.Count; i++)
        {
            var m = messages[start + i];
            var y = area.Y + 2 + i;
            var sel = start + i == _list.Index;
            var fg = sel ? Th.SelFg : m.Read ? Th.Dim : Th.Fg;
            var bg = sel ? Th.SelBg : Th.Bg;
            c.Fill(area.X, y, area.W, 1, ' ', fg, bg);
            c.Write(area.X, y, m.Read ? " " : "●", sel ? fg : Th.Accent, bg);
            c.Write(area.X + 2, y, Fmt.ShortDate(m.Date), fg, bg);
            c.WriteField(area.X + 9, y, 18, m.Sender, fg, bg);
            c.WriteField(area.X + 28, y, area.W - 28, m.Subject, fg, bg);
        }

        var current = messages[_list.Index];
        var by = area.Y + 3 + listHeight;
        c.HLine(area.X, by, area.W, Th.Border, Th.Bg);
        c.Write(area.X, by + 1, current.Subject, Th.Accent, Th.Bg, area.W);
        c.Write(area.X, by + 2, $"{T["inbox.from"]} {current.Sender} · {Fmt.Date(current.Date)}", Th.Dim, Th.Bg, area.W);
        var y2 = by + 4;
        foreach (var line in Wrap(current.Body, area.W))
        {
            if (y2 >= area.Bottom) break;
            c.Write(area.X, y2++, line, Th.Fg, Th.Bg);
        }
    }

    public override bool Handle(ConsoleKeyInfo key)
    {
        if (!_list.HandleKey(key, S.Inbox.Count())) return false;
        MarkRead();
        return true;
    }

    public static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length + word.Length + 1 > width && line.Length > 0)
                {
                    yield return line;
                    line = "";
                }
                line = line.Length == 0 ? word : line + " " + word;
            }
            yield return line;
        }
    }
}
