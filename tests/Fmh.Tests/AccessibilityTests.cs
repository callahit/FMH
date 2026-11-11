using Avalonia.Input;
using Fmh.Core.Data;
using Fmh.Desktop.Services;

namespace Fmh.Tests;

/// <summary>AP 12b: Tastenbelegung als Daten, Tausch bei Doppelbelegung, Kontrastthema und Tutorial-Daten.</summary>
public sealed class AccessibilityTests
{
    private static readonly UiRules Rules = TestData.Data.Ui;

    [Fact]
    public void DefaultKeyBindingsAreUniqueAndCoverEveryPage()
    {
        var map = KeyMap.Build(Rules, new AppSettings());
        Assert.All(Rules.KeyBindings, r => Assert.NotNull(KeyMap.Parse(r.Gesture)));
        var gestures = map.Actions.Select(a => map.GestureOf(a)!.ToString()).ToList();
        Assert.Equal(gestures.Count, gestures.Distinct().Count());
        foreach (var page in new[] { "inbox", "squad", "tactics", "training", "fixtures", "transfers", "finances", "stadium", "club", "league", "national", "history" })
            Assert.Contains("page." + page, map.Actions);
        Assert.Equal("page.inbox", map.ActionFor(Key.F1, KeyModifiers.None));
        Assert.Equal("save", map.ActionFor(Key.S, KeyModifiers.Control));
        Assert.Null(map.ActionFor(Key.S, KeyModifiers.None));
        Assert.Equal("continue", map.ActionFor(Key.Space, KeyModifiers.None));
        // Jede Aktion hat einen Text in jeder Sprache.
        var data = TestData.LoadAll();
        foreach (var lang in data.Languages.Keys)
        {
            data.UseLanguage(lang);
            Assert.All(map.Actions, a => Assert.True(data.Texts.Has("keys." + a), lang + ": keys." + a));
        }
    }

    [Fact]
    public void RebindingSwapsTakenKeysAndStoresOnlyDifferences()
    {
        var settings = new AppSettings();
        // Freie Taste: nur die eine Aktion ändert sich.
        Assert.Null(KeyMap.Rebind(Rules, settings, "continue", new KeyGesture(Key.Enter, KeyModifiers.Control)));
        Assert.Equal("continue", KeyMap.Build(Rules, settings).ActionFor(Key.Enter, KeyModifiers.Control));
        Assert.Null(KeyMap.Build(Rules, settings).ActionFor(Key.Space, KeyModifiers.None));
        Assert.Single(settings.KeyBindings);

        // Vergebene Taste: Speichern bekommt F1, die Posteingang-Seite übernimmt Strg+S.
        Assert.Equal("page.inbox", KeyMap.Rebind(Rules, settings, "save", new KeyGesture(Key.F1)));
        var map = KeyMap.Build(Rules, settings);
        Assert.Equal("save", map.ActionFor(Key.F1, KeyModifiers.None));
        Assert.Equal("page.inbox", map.ActionFor(Key.S, KeyModifiers.Control));

        // Zurücktauschen auf den Standard entfernt die Abweichungen wieder.
        KeyMap.Rebind(Rules, settings, "save", new KeyGesture(Key.S, KeyModifiers.Control));
        KeyMap.Rebind(Rules, settings, "continue", new KeyGesture(Key.Space));
        Assert.Empty(settings.KeyBindings);

        // Kaputte Einträge in den Einstellungen fallen auf den Standard zurück.
        settings.KeyBindings["save"] = "Strg+Nichts";
        Assert.Equal("save", KeyMap.Build(Rules, settings).ActionFor(Key.S, KeyModifiers.Control));
        Assert.Throws<ArgumentException>(() => KeyMap.Rebind(Rules, settings, "unknown", new KeyGesture(Key.A)));

        // Enter, Tab und Pfeile ohne Zusatztaste bleiben Eingabefeldern und Seiten vorbehalten, auch aus der Datei.
        Assert.Throws<ArgumentException>(() => KeyMap.Rebind(Rules, settings, "continue", new KeyGesture(Key.Enter)));
        settings.KeyBindings["continue"] = "Left";
        Assert.Equal("continue", KeyMap.Build(Rules, settings).ActionFor(Key.Space, KeyModifiers.None));
        Assert.All(Rules.KeyBindings, r => Assert.False(KeyMap.IsReserved(KeyMap.Parse(r.Gesture)!), r.Action));
    }

    [Fact]
    public void SettingsKeepAccessibilityChoices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fmh-settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings { FontScale = 1.25, Tutorial = false };
            s.TutorialSeen.Add("squad");
            s.KeyBindings["continue"] = "Enter";
            s.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal((1.25, false, "squad", "Enter"), (loaded.FontScale, loaded.Tutorial, loaded.TutorialSeen.Single(), loaded.KeyBindings["continue"]));
            // Ältere Einstellungsdatei nur mit Sprache: Standardwerte.
            File.WriteAllText(path, "{\"language\":\"de\"}");
            var old = AppSettings.Load(path);
            Assert.Equal((1.0, true, 0, 0), (old.FontScale, old.Tutorial, old.TutorialSeen.Count, old.KeyBindings.Count));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
        Assert.Contains(1.0, Rules.FontScales);
        Assert.All(Rules.FontScales, f => Assert.InRange(f, 1.0, 1.5));
    }

    [Fact]
    public void HighContrastThemeMeetsWcagAaa()
    {
        var p = Palette.HighContrast;
        Assert.Contains(p, Palette.All);
        foreach (var bg in new[] { p.Bg, p.Surface, p.Surface2 })
            foreach (var fg in new[] { p.Text, p.Muted, p.Accent, p.Gold, p.Good, p.Warn, p.Bad })
                Assert.True(Palette.ContrastRatio(fg, bg) >= 7, $"{fg} auf {bg}: {Palette.ContrastRatio(fg, bg):0.0}");
        // Der Kontrast ist höher als im Standardthema.
        Assert.True(Palette.ContrastRatio(p.Muted, p.Surface) > Palette.ContrastRatio(Palette.Classic.Muted, Palette.Classic.Surface));
        Assert.Equal(21, Palette.ContrastRatio(Avalonia.Media.Colors.White, Avalonia.Media.Colors.Black), 1);
    }

    [Fact]
    public void TutorialStepsHaveTextsAndKnownPages()
    {
        var pages = Rules.KeyBindings.Where(k => k.Action.StartsWith("page.")).Select(k => k.Action["page.".Length..]).ToHashSet();
        Assert.NotEmpty(Rules.Tutorial);
        Assert.Equal(Rules.Tutorial.Count, Rules.Tutorial.Select(t => t.Id).Distinct().Count());
        foreach (var step in Rules.Tutorial)
        {
            Assert.Contains(step.Page, pages);
            Assert.True(TestData.Data.Texts.Has("tutorial." + step.Id + ".title"), step.Id);
            Assert.True(TestData.Data.Texts.Has("tutorial." + step.Id + ".text"), step.Id);
        }
    }
}
