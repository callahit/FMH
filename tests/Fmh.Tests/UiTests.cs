using Fmh.Core.Game;
using Fmh.Core.Model;
using Fmh.MatchEngine;
using Fmh.Ui;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Tests;

/// <summary>Bedient die Oberfläche ohne echte Konsole: Tasten rein, Bild als Text raus.</summary>
public class UiTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k, char ch = '\0', bool shift = false, bool ctrl = false) => new(ch, k, shift, false, ctrl);

    private static (App app, string saves) NewApp()
    {
        var saves = Path.Combine(Path.GetTempPath(), "fmh-ui-" + Guid.NewGuid());
        var app = new App(TestData.Data, new TextMatchEngine(), saves);
        app.Push(new MainMenuView(app));
        return (app, saves);
    }

    private static string Screen(App app, int w = 120, int h = 36)
    {
        var c = new Canvas(w, h);
        app.Render(c);
        var text = c.ToPlainText();
        Assert.DoesNotMatch(@"\[[a-z]+\.[A-Za-z.0-9]+\]", text); // fehlender Sprachschlüssel
        return text;
    }

    private static void StartNewGame(App app)
    {
        app.Handle(Key(ConsoleKey.Enter)); // Neues Spiel
        Assert.IsType<NewGameView>(app.Top);
        for (var i = 0; i < 6; i++) app.Handle(Key(ConsoleKey.DownArrow));
        app.Handle(Key(ConsoleKey.Enter)); // Spiel starten
        Assert.IsType<GameView>(app.Top);
    }

    [Fact]
    public void MainMenuAndNewGameRender()
    {
        var (app, _) = NewApp();
        Assert.Contains("Neues Spiel", Screen(app));
        app.Handle(Key(ConsoleKey.Enter));
        var screen = Screen(app);
        Assert.Contains("SV Nordheide", screen);
        Assert.Contains("Werdegang", screen);
    }

    [Theory]
    [InlineData(100, 30)]
    [InlineData(160, 45)]
    public void AllGamePagesRenderAtMinimumAndLargeSize(int w, int h)
    {
        var (app, _) = NewApp();
        StartNewGame(app);
        var titles = new[] { "Posteingang", "Kader", "Taktik", "Training", "Spielplan", "Transfers", "Finanzen", "Stadion", "Verein", "Liga" };
        for (var i = 0; i < 10; i++)
        {
            app.Handle(Key(ConsoleKey.F1 + i));
            var screen = Screen(app, w, h);
            Assert.Contains(titles[i], screen);
            Assert.Contains("SV NORDHEIDE", screen);
        }
        app.Handle(Key(ConsoleKey.F2));
        app.Handle(Key(ConsoleKey.S));
        app.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<PlayerView>(app.Top);
        Assert.Contains("Gesamtstärke", Screen(app, w, h));
        app.Handle(Key(ConsoleKey.RightArrow));
        Screen(app, w, h);
        app.Handle(Key(ConsoleKey.Escape));
        Assert.IsType<GameView>(app.Top);
    }

    [Fact]
    public void AttributeDisplayModesRender()
    {
        var (app, _) = NewApp();
        StartNewGame(app);
        foreach (var mode in Enum.GetValues<AttributeDisplay>())
        {
            app.Session!.HumanManager.Profile.AttributeDisplay = mode;
            app.Handle(Key(ConsoleKey.F2));
            app.Handle(Key(ConsoleKey.Enter));
            Screen(app);
            app.Handle(Key(ConsoleKey.Escape));
        }
    }

    [Fact]
    public void TacticsPageChangesFormationAndInstructions()
    {
        var (app, _) = NewApp();
        StartNewGame(app);
        var club = app.Session!.HumanClub;
        app.Handle(Key(ConsoleKey.F3));
        app.Handle(Key(ConsoleKey.E)); // Einstellungen
        var before = club.Tactic.FormationId;
        app.Handle(Key(ConsoleKey.RightArrow));
        Assert.NotEqual(before, club.Tactic.FormationId);
        Assert.Equal(11, club.Tactic.Lineup.Count(id => id.HasValue));
        app.Handle(Key(ConsoleKey.DownArrow));
        app.Handle(Key(ConsoleKey.RightArrow));
        Assert.Equal(Mentality.Positive, club.Tactic.Mentality);
        Screen(app);

        // Aufstellung: ersten Platz neu besetzen über den Auswahldialog.
        app.Handle(Key(ConsoleKey.E));
        app.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<ListDialog>(app.Top);
        Screen(app);
        app.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<GameView>(app.Top);
        Assert.Equal(club.Tactic.Lineup.Count(id => id.HasValue), club.Tactic.Lineup.Where(id => id.HasValue).Distinct().Count());
    }

    [Fact]
    public void PlayMatchdayThroughUi()
    {
        var (app, saves) = NewApp();
        try
        {
            StartNewGame(app);
            var session = app.Session!;
            app.Handle(Key(ConsoleKey.Spacebar)); // bis zum Spieltag
            var fixture = session.PendingHumanFixture;
            Assert.NotNull(fixture);
            app.Handle(Key(ConsoleKey.Spacebar)); // Vorbericht öffnen (mit Autosave)
            Assert.IsType<MatchView>(app.Top);
            Assert.Contains("Vorbericht", Screen(app));
            Assert.Single(Directory.GetFiles(saves, "autosave*"));

            app.Handle(Key(ConsoleKey.Enter)); // Anpfiff -> Kabinenansprache
            Screen(app);
            app.Handle(Key(ConsoleKey.D6, '6')); // motivierend
            for (var i = 0; i < 30; i++) app.Tick(TimeSpan.FromSeconds(1));
            var live = Screen(app);
            Assert.Contains("1'", live); // Kommentarzeilen mit Minute
            Assert.Contains("Ballbesitz", live);

            // Bis zur Halbzeit laufen lassen (Pausen bei Toren/Verletzungen jeweils auflösen).
            for (var i = 0; i < 2000 && fixture!.Result is null; i++)
            {
                app.Tick(TimeSpan.FromSeconds(5));
                var s = Screen(app);
                if (s.Contains("PAUSE")) app.Handle(Key(ConsoleKey.Spacebar));
                if (s.Contains("Halbzeit – Kabinenansprache"))
                {
                    app.Handle(Key(ConsoleKey.D1, '1'));
                    app.Handle(Key(ConsoleKey.Spacebar));
                }
                if (i == 50)
                {
                    app.Handle(Key(ConsoleKey.W)); // Wechsel
                    Assert.IsType<ListDialog>(app.Top);
                    app.Handle(Key(ConsoleKey.DownArrow));
                    app.Handle(Key(ConsoleKey.Enter));
                    app.Handle(Key(ConsoleKey.Enter));
                    Assert.IsType<MatchView>(app.Top);
                    app.Handle(Key(ConsoleKey.Spacebar));
                }
            }
            Assert.NotNull(fixture!.Result);
            var post = Screen(app);
            Assert.Contains("Spieler des Spiels", post);
            app.Handle(Key(ConsoleKey.RightArrow));
            Assert.Contains("Heatmap", Screen(app));
            app.Handle(Key(ConsoleKey.Enter));
            Assert.IsType<GameView>(app.Top);
            Assert.True(session.World.LeagueOf(session.HumanClub).Fixtures.Where(f => f.Matchday == fixture.Matchday).All(f => f.Result is not null));
            Assert.Equal(11, session.HumanClub.Tactic.Lineup.Count);
        }
        finally
        {
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    [Fact]
    public void InstantModeAndSaveLoadThroughMenu()
    {
        var (app, saves) = NewApp();
        try
        {
            StartNewGame(app);
            var session = app.Session!;
            session.HumanManager.Profile.MatchDisplay = MatchDisplayMode.Instant;
            app.Handle(Key(ConsoleKey.Spacebar));
            app.Handle(Key(ConsoleKey.Spacebar));
            app.Handle(Key(ConsoleKey.Enter)); // Sofortergebnis
            Assert.Contains("Spieler des Spiels", Screen(app));
            app.Handle(Key(ConsoleKey.Enter));

            app.Handle(Key(ConsoleKey.S, 's', ctrl: true));
            Assert.Contains("Gespeichert", Screen(app));
            var date = session.World.Date;

            // Hauptmenü -> Laden
            app.Handle(Key(ConsoleKey.Escape));
            app.Handle(Key(ConsoleKey.DownArrow));
            app.Handle(Key(ConsoleKey.DownArrow));
            app.Handle(Key(ConsoleKey.DownArrow));
            app.Handle(Key(ConsoleKey.Enter));
            Assert.IsType<MainMenuView>(app.Top);
            app.Handle(Key(ConsoleKey.DownArrow));
            app.Handle(Key(ConsoleKey.Enter));
            Assert.IsType<ListDialog>(app.Top);
            Screen(app);
            // Neuester Spielstand steht oben (manuell gespeichert nach dem Autosave).
            app.Handle(Key(ConsoleKey.Enter));
            Assert.IsType<GameView>(app.Top);
            Assert.Equal(date, app.Session!.World.Date);
        }
        finally
        {
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    [Fact]
    public void OptionsChangeProfileAndTheme()
    {
        var (app, _) = NewApp();
        StartNewGame(app);
        app.Handle(Key(ConsoleKey.Escape));
        app.Handle(Key(ConsoleKey.DownArrow));
        app.Handle(Key(ConsoleKey.DownArrow));
        app.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<OptionsView>(app.Top);
        var profile = app.Session!.HumanManager.Profile;
        app.Handle(Key(ConsoleKey.RightArrow));
        Assert.Equal(WageDisplay.Year, profile.WageDisplay);
        for (var i = 0; i < 6; i++) app.Handle(Key(ConsoleKey.DownArrow));
        app.Handle(Key(ConsoleKey.RightArrow));
        Assert.Equal("Hell", profile.Theme);
        Assert.Equal(Theme.Light, app.Theme);
        Screen(app);
        app.Handle(Key(ConsoleKey.Escape));
        app.Handle(Key(ConsoleKey.F2));
        Assert.Contains("/Jahr", Screen(app, 160, 40));
    }
}
