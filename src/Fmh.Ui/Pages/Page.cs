using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>Ein Hauptbereich im Spiel (F1–F10).</summary>
public abstract class Page
{
    protected Page(GameView shell) => Shell = shell;

    protected GameView Shell { get; }
    protected App App => Shell.Application;
    protected GameSession S => Shell.Session;
    protected Texts T => App.Data.Texts;
    protected Theme Th => App.Theme;

    public abstract string TitleKey { get; }
    public virtual string HelpKey => "help.page.default";

    public abstract void Render(Canvas c, Rect area);

    /// <summary>Gibt true zurück, wenn die Taste verbraucht wurde.</summary>
    public virtual bool Handle(ConsoleKeyInfo key) => false;

    public virtual void OnShow() { }

    protected void Title(Canvas c, Rect area, string text, string? right = null)
    {
        c.Write(area.X, area.Y, text, Th.Accent, Th.Bg, area.W);
        if (right is not null) c.Write(area.Right - right.Length, area.Y, right, Th.Dim, Th.Bg);
    }
}

/// <summary>Bereiche, die laut Roadmap erst in späteren Phasen kommen.</summary>
public sealed class PlaceholderPage : Page
{
    private readonly string _title;
    private readonly string _text;

    public PlaceholderPage(GameView shell, string titleKey, string textKey) : base(shell)
    {
        _title = titleKey;
        _text = textKey;
    }

    public override string TitleKey => _title;

    public override void Render(Canvas c, Rect area)
    {
        Title(c, area, T[_title]);
        c.Write(area.X, area.Y + 2, T[_text], Th.Fg, Th.Bg, area.W);
        c.Write(area.X, area.Y + 4, T["placeholder.roadmap"], Th.Dim, Th.Bg, area.W);
    }
}
