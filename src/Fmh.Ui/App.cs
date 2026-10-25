using System.Diagnostics;
using System.Text;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Match;
using Fmh.Core.Persistence;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui;

/// <summary>Ein Bildschirm der Anwendung. Views liegen auf einem Stapel (Dialoge oben).</summary>
public abstract class View
{
    protected View(App app) => App = app;

    protected App App { get; }
    protected Texts T => App.Data.Texts;
    protected Theme Th => App.Theme;

    /// <summary>Soll der darunterliegende View mitgezeichnet werden (Dialog)?</summary>
    public virtual bool IsOverlay => false;

    public abstract void Render(Canvas c);
    public abstract void Handle(ConsoleKeyInfo key);

    /// <summary>Wird regelmäßig aufgerufen (z. B. für den Live-Ticker).</summary>
    public virtual void Tick(TimeSpan elapsed) { }

    /// <summary>Wird aufgerufen, wenn der View wieder oben auf dem Stapel liegt.</summary>
    public virtual void OnResume() { }
}

/// <summary>Anwendungsrahmen: View-Stapel, Hauptschleife, Spielstand, Thema.</summary>
public sealed class App
{
    private readonly List<View> _views = new();

    public App(GameData data, IMatchEngine engine, string saveDirectory)
    {
        Data = data;
        Engine = engine;
        SaveDirectory = saveDirectory;
    }

    public GameData Data { get; }
    public IMatchEngine Engine { get; }
    public string SaveDirectory { get; }
    public GameSession? Session { get; set; }
    public bool Running { get; private set; } = true;
    public string? StatusMessage { get; set; }

    public Theme Theme => Theme.ByName(Session?.HumanManager.Profile.Theme ?? "Klassik");

    public View? Top => _views.Count == 0 ? null : _views[^1];
    public IReadOnlyList<View> Views => _views;

    public void Push(View v) => _views.Add(v);

    public void Pop()
    {
        if (_views.Count > 0) _views.RemoveAt(_views.Count - 1);
        Top?.OnResume();
    }

    public void Replace(View v)
    {
        if (_views.Count > 0) _views.RemoveAt(_views.Count - 1);
        _views.Add(v);
    }

    /// <summary>Leert den Stapel bis auf einen neuen Wurzel-View.</summary>
    public void Reset(View v)
    {
        _views.Clear();
        _views.Add(v);
    }

    public void Quit() => Running = false;

    public void Handle(ConsoleKeyInfo key)
    {
        StatusMessage = null;
        Top?.Handle(key);
    }

    public void Tick(TimeSpan elapsed) => Top?.Tick(elapsed);

    public void Render(Canvas c)
    {
        c.Clear(Theme.Fg, Theme.Bg);
        // Ab dem obersten Nicht-Overlay zeichnen, damit Dialoge über ihrem Hintergrund liegen.
        var start = _views.Count - 1;
        while (start > 0 && _views[start].IsOverlay) start--;
        for (var i = Math.Max(0, start); i < _views.Count; i++) _views[i].Render(c);
    }

    public string SaveCurrent(string? name = null)
    {
        var session = Session ?? throw new InvalidOperationException("Kein Spiel geladen.");
        session.SyncRng();
        if (session.World.Rules.Ironman) return SaveGame.IronmanSave(session.World, SaveDirectory);
        name ??= session.HumanClub.Key + "-" + session.HumanManager.Name.ToLowerInvariant().Replace(' ', '_');
        var path = Path.Combine(SaveDirectory, name + SaveGame.Extension);
        SaveGame.Save(session.World, path);
        return path;
    }

    public string Autosave()
    {
        var session = Session ?? throw new InvalidOperationException("Kein Spiel geladen.");
        session.SyncRng();
        return SaveGame.Autosave(session.World, SaveDirectory);
    }

    /// <summary>Hauptschleife für die echte Konsole.</summary>
    public void RunConsole()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Write("\u001b[?1049h\u001b[?25l");
        try
        {
            var watch = Stopwatch.StartNew();
            var last = watch.Elapsed;
            string? lastFrame = null;
            int lastW = 0, lastH = 0;
            while (Running && Top is not null)
            {
                while (Console.KeyAvailable)
                {
                    Handle(Console.ReadKey(intercept: true));
                    if (!Running) break;
                }
                var now = watch.Elapsed;
                Tick(now - last);
                last = now;

                var w = Math.Max(20, Console.WindowWidth);
                var h = Math.Max(10, Console.WindowHeight);
                var canvas = new Canvas(w, h);
                if (w < Layout.MinWidth || h < Layout.MinHeight)
                {
                    canvas.Clear(ConsoleColor.White, ConsoleColor.Black);
                    canvas.Write(1, 1, Data.Texts.Format("ui.tooSmall", Layout.MinWidth, Layout.MinHeight, w, h), ConsoleColor.Yellow);
                }
                else Render(canvas);

                var frame = canvas.ToAnsi();
                if (frame != lastFrame || w != lastW || h != lastH)
                {
                    if (w != lastW || h != lastH) Console.Write("\u001b[2J");
                    Console.Write(frame);
                    lastFrame = frame;
                    lastW = w;
                    lastH = h;
                }
                Thread.Sleep(16);
            }
        }
        finally
        {
            Console.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
        }
    }
}

public static class Layout
{
    public const int MinWidth = 100;
    public const int MinHeight = 30;
}
