using System.Text.RegularExpressions;
using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>Neuer Textmodus: Szenenkatalog als Daten, mehrsprachig, über der schnellen Engine.</summary>
public sealed partial class SceneNarratorTests
{
    private static readonly string[] Placeholders =
        { "p", "o", "g", "mate", "def", "ref", "t", "other", "home", "away", "score", "side", "pens", "n", "m", "min" };

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    private static GameData Fresh() => PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base" });

    private static (IMatchSimulation Sim, List<MatchEvent> Events) PlayMatch(ulong seed)
    {
        var session = TestData.NewSession(seed);
        var fixture = session.World.AllFixtures.First(f => f.Result is null && f.Kind == CompetitionKind.League);
        var sim = session.StartMatch(fixture, humanControlled: false, sceneText: true);
        sim.RunToEnd();
        return (sim, sim.Events.ToList());
    }

    [Fact]
    public void CatalogIsCompleteInGermanAndEnglish()
    {
        var data = Fresh();
        var rules = data.Scenes;
        Assert.NotEmpty(rules.Scenes);
        var keys = rules.Scenes.SelectMany(s => s.Steps).Concat(rules.Rare.SelectMany(r => r.Steps)).Select(s => s.Text).Distinct().ToList();
        Assert.Equal(rules.Scenes.Count, rules.Scenes.Select(s => s.Id).Distinct().Count());
        foreach (var lang in new[] { "de", "en" })
        {
            data.UseLanguage(lang);
            foreach (var key in keys)
            {
                Assert.True(data.Commentary.TryGetValue(key, out var variants) && variants.Count > 0, $"{lang}: {key}");
                foreach (var v in variants)
                    foreach (Match m in Placeholder().Matches(v))
                        Assert.Contains(m.Groups[1].Value, Placeholders);
            }
            Assert.True(data.Texts.Has("scene.someone") && data.Texts.Has("scene.referee"), lang);
        }
        // Jede Ereignisart, die der Kommentar kennt, hat mindestens eine Szene.
        var commentaryKeys = data.Languages["de"].Commentary.Keys.Where(k => !k.StartsWith("scene.", StringComparison.Ordinal));
        foreach (var key in commentaryKeys)
            Assert.True(rules.Scenes.Any(s => s.On.Contains(key)), $"Keine Szene für {key}");
    }

    [Fact]
    public void WholeMatchIsToldInScenesWithoutTouchingTheResult()
    {
        var (sim, events) = PlayMatch(31);
        var (again, _) = PlayMatch(31);
        Assert.Equal((sim.Home.Goals, sim.Away.Goals), (again.Home.Goals, again.Away.Goals));

        var data = TestData.Data;
        var narrator = new SceneNarrator(data, sim);
        var lines = events.Select(e => (e, narrator.Narrate(e))).ToList();
        var all = lines.SelectMany(l => l.Item2).ToList();
        Assert.True(all.Count > events.Count(e => e.Key), "Szenen sollen mehr erzählen als die Schlüsselszenen.");
        Assert.All(all, l => Assert.DoesNotMatch(Placeholder(), l.Text));
        foreach (var (e, scene) in lines.Where(l => l.e.Type == MatchEventType.Goal))
        {
            Assert.True(scene.Count >= 3, $"Tor in Minute {e.Minute} mit {scene.Count} Zeilen");
            Assert.Contains(scene, l => l.Highlight);
        }
        // Gleiche Seeds, gleiche Erzählung
        var second = new SceneNarrator(data, again);
        Assert.Equal(all.Select(l => l.Text), again.Events.SelectMany(e => second.Narrate(e)).Select(l => l.Text));
        // Statistik und Spielstand sind die der schnellen Engine
        Assert.Equal(sim.Home.Stats.Shots, again.Home.Stats.Shots);
    }

    [Fact]
    public void LanguageSwitchTellsTheSameMatchInEnglish()
    {
        var data = Fresh();
        var (sim, events) = PlayMatch(32);
        data.UseLanguage("de");
        var german = new SceneNarrator(data, sim);
        var de = events.SelectMany(e => german.Narrate(e)).Select(l => l.Text).ToList();
        data.UseLanguage("en");
        var english = new SceneNarrator(data, sim);
        var en = events.SelectMany(e => english.Narrate(e)).Select(l => l.Text).ToList();
        Assert.NotEmpty(en);
        // Bis auf sprachneutrale Zeilen wie „+3!“ ist alles übersetzt.
        Assert.True(en.Count(de.Contains) < en.Count / 10, $"{en.Count(de.Contains)} von {en.Count} Zeilen gleich");
        Assert.Contains(en, l => l.Contains(" the ", StringComparison.Ordinal));
    }

    [Fact]
    public void ConditionsFollowWeatherScoreAndKeeper()
    {
        var (sim, events) = PlayMatch(33);
        var narrator = new SceneNarrator(TestData.Data, sim);
        var kick = events.First(e => e.Type == MatchEventType.KickOff);
        Assert.Equal(sim.Setup.Weather == Weather.Rain, narrator.Holds(new SceneCondition { Weather = new() { Weather.Rain } }, kick));
        Assert.Equal(sim.Setup.Floodlit, narrator.Holds(new SceneCondition { Floodlit = true }, kick));

        // Vor dem ersten Tor steht es unentschieden: Führungstreffer
        var first = new MatchEvent { Type = MatchEventType.Goal, Side = Side.Home, Minute = 12, HomeScore = 1, AwayScore = 0 };
        Assert.True(narrator.Holds(new SceneCondition { Score = SceneScore.Level }, first));
        var comeback = new MatchEvent { Type = MatchEventType.Goal, Side = Side.Away, Minute = 88, HomeScore = 2, AwayScore = 1 };
        Assert.True(narrator.Holds(new SceneCondition { Score = SceneScore.Trailing, MinMinute = 85 }, comeback));
        Assert.False(narrator.Holds(new SceneCondition { MaxMinute = 80 }, comeback));

        var keeper = sim.Away.OnPitch.First(p => p.Position == Position.GK).Player;
        var shot = new MatchEvent { Type = MatchEventType.Shot, Side = Side.Home, Minute = 20, Outcome = ShotOutcome.Saved };
        var reflexes = keeper.Attributes[Attr.Reflexes];
        Assert.True(narrator.Holds(new SceneCondition { KeeperAttrs = new() { new SceneAttr { Attr = Attr.Reflexes, Min = reflexes } } }, shot));
        Assert.False(narrator.Holds(new SceneCondition { KeeperAttrs = new() { new SceneAttr { Attr = Attr.Reflexes, Min = reflexes + 1 } } }, shot));
    }

    [Fact]
    public void ModdedScenesWinAndMissingOnesFallBackToCommentary()
    {
        var data = Fresh();
        var (sim, events) = PlayMatch(34);
        data.Scenes.Rare.Clear();
        data.Scenes.Scenes.RemoveAll(s => s.On.Contains("corner"));
        data.Commentary["scene.test.corner"] = new() { "Testecke {p}" };
        var narrator = new SceneNarrator(data, sim);
        var corner = events.First(e => e.Type == MatchEventType.Corner);
        var plain = narrator.Narrate(corner);
        Assert.Single(plain);
        Assert.DoesNotContain("Testecke", plain[0].Text);

        data.Scenes.Scenes.Add(new SceneDef { Id = "test.corner", On = new() { "corner" }, Steps = new() { new SceneStep { Text = "scene.test.corner" } } });
        var modded = new SceneNarrator(data, sim).Narrate(corner);
        Assert.StartsWith("Testecke ", Assert.Single(modded).Text);
    }
}
