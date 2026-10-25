using Fmh.Ui.Pages;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

/// <summary>Zeigt eine Seite des Hauptfensters bildschirmfüllend, z. B. die Taktik vor dem Anpfiff.</summary>
public sealed class PageHostView : View
{
    private readonly Page _page;
    private readonly string _title;

    public PageHostView(App app, Page page, string title) : base(app)
    {
        _page = page;
        _title = title;
        _page.OnShow();
    }

    public override void Render(Canvas c)
    {
        c.Fill(0, 0, c.Width, 1, ' ', Th.MenuFg, Th.MenuBg);
        c.Write(1, 0, _title, Th.MenuFg, Th.MenuBg);
        _page.Render(c, new Rect(2, 2, c.Width - 4, c.Height - 4));
        if (App.StatusMessage is { } msg) c.Write(2, c.Height - 2, msg, Th.Warn, Th.Bg, c.Width - 4);
        Draw.KeyHelp(c, c.Height - 1, Th, T[_page.HelpKey] + "  " + T["help.back"]);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (_page.Handle(key)) return;
        if (key.Key == ConsoleKey.Escape) App.Pop();
    }
}
