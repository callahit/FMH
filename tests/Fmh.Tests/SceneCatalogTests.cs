using System.Text.RegularExpressions;
using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>Ausbau des Textmodus: Umfang des Katalogs, Ausgang der gegnerischen Aktion, keine Wiederholungen, Spielstand.</summary>
public sealed partial class SceneCatalogTests
{
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
    public void AtLeast2500DistinctSentencesPerLanguageAllInUseWithMatchingPlaceholders()
    {
        var data = Fresh();
        var de = data.Languages["de"].Commentary.Where(kv => kv.Key.StartsWith("scene.", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var en = data.Languages["en"].Commentary;
        Assert.True(de.Values.SelectMany(v => v).Distinct().Count() >= 2500, $"{de.Values.SelectMany(v => v).Distinct().Count()} deutsche Sätze");
        Assert.True(de.Keys.SelectMany(k => en[k]).Distinct().Count() >= 2500, "englische Sätze");

        var used = data.Scenes.Scenes.SelectMany(s => s.Steps).Concat(data.Scenes.Rare.SelectMany(r => r.Steps)).Select(s => s.Text).ToHashSet();
        foreach (var (key, variants) in de)
        {
            Assert.Contains(key, used);
            Assert.Equal(variants.Count, en[key].Count);
            for (var i = 0; i < variants.Count; i++)
                Assert.Equal(Placeholder().Matches(variants[i]).Select(m => m.Value).Order(), Placeholder().Matches(en[key][i]).Select(m => m.Value).Order());
        }
        Assert.Empty(SceneValidator.Validate(data));
    }

    /// <summary>Der Ausgang einer Aktion wird erst beim nächsten Ereignis des Gegners erzählt, aus Sicht der Aktion.</summary>
    [Fact]
    public void OutcomeOfTheOpponentsActionIsToldFromTheirSide()
    {
        var data = Fresh();
        var (sim, events) = PlayMatch(41);
        data.Scenes.Rare.Clear();
        data.Scenes.AttackShare = 1;
        data.Scenes.Scenes.RemoveAll(s => s.On.Contains("counter") || s.On.Contains("attack"));
        data.Commentary["scene.test.prev"] = new() { "PREV {t}" };
        data.Commentary["scene.test.own"] = new() { "OWN {t}" };
        data.Scenes.Scenes.Add(new SceneDef
        {
            Id = "test.after", On = new() { "attack", "counter" }, When = new SceneCondition { AfterOpponent = new() { "attack", "counter", "cross", "corner", "dribble" } },
            Steps = new() { new SceneStep { Text = "scene.test.prev", Previous = true }, new SceneStep { Text = "scene.test.own" } },
        });
        var narrator = new SceneCommentary(data, sim);
        var found = 0;
        foreach (var e in events)
        {
            var lines = narrator.Narrate(e).Select(l => l.Text).ToList();
            if (lines.FirstOrDefault()?.StartsWith("PREV ", StringComparison.Ordinal) != true) continue;
            found++;
            Assert.Equal("PREV " + sim.Team(e.Side.Other()).Club.Name, lines[0]);
            Assert.Equal("OWN " + sim.Team(e.Side).Club.Name, lines[1]);
        }
        Assert.True(found > 0, "Kein Gegenangriff nach einer Aktion des Gegners.");
    }

    /// <summary>Innerhalb eines Spiels wiederholt sich eine Variante erst, wenn alle Varianten des Bausteins vorkamen.</summary>
    [Fact]
    public void VariantsDoNotRepeatBeforeTheBuildingBlockIsUsedUp()
    {
        var data = Fresh();
        var (sim, events) = PlayMatch(42);
        data.Scenes.Rare.Clear();
        data.Scenes.AttackShare = 1;
        data.Scenes.Scenes.RemoveAll(s => s.On.Contains("attack"));
        data.Commentary["scene.test.cycle"] = new() { "A", "B", "C", "D" };
        data.Scenes.Scenes.Add(new SceneDef { Id = "test.cycle", On = new() { "attack" }, Steps = new() { new SceneStep { Text = "scene.test.cycle" } } });
        var narrator = new SceneCommentary(data, sim);
        var told = events.SelectMany(e => narrator.Narrate(e)).Select(l => l.Text).Where(t => t.Length == 1).ToList();
        Assert.True(told.Count >= 8, $"{told.Count} Angriffe");
        for (var i = 0; i + 4 <= told.Count; i += 4)
            Assert.Equal(4, told.Skip(i).Take(4).Distinct().Count());
    }

    [Fact]
    public void GoalKickNamesTheOwnKeeperAndStandingAndMarginFollowTheScore()
    {
        var data = Fresh();
        var (sim, events) = PlayMatch(43);
        data.Scenes.Rare.Clear();
        data.Scenes.Scenes.RemoveAll(s => s.On.Contains("goalKick"));
        data.Commentary["scene.test.keeper"] = new() { "K {k} G {g}" };
        data.Scenes.Scenes.Add(new SceneDef { Id = "test.keeper", On = new() { "goalKick" }, Steps = new() { new SceneStep { Text = "scene.test.keeper" } } });
        var narrator = new SceneCommentary(data, sim);
        var kick = events.First(e => e.Type == MatchEventType.GoalKick);
        var own = sim.Team(kick.Side).OnPitch.First(p => p.Position == Position.GK).Player.ShortName;
        var other = sim.Team(kick.Side.Other()).OnPitch.First(p => p.Position == Position.GK).Player.ShortName;
        foreach (var e in events.TakeWhile(x => x != kick)) narrator.Narrate(e);
        Assert.Equal($"K {own} G {other}", Assert.Single(narrator.Narrate(kick)).Text);

        var full = new MatchEvent { Type = MatchEventType.FullTime, Side = Side.Away, Minute = 90, HomeScore = 2, AwayScore = 1 };
        Assert.True(narrator.Holds(new SceneCondition { Standing = SceneStanding.Home }, full));
        Assert.False(narrator.Holds(new SceneCondition { Standing = SceneStanding.Level }, full));
        // Tor zum 3:1 aus Sicht des Heimteams: vorher +1
        var third = new MatchEvent { Type = MatchEventType.Goal, Side = Side.Home, Minute = 70, HomeScore = 3, AwayScore = 1 };
        Assert.True(narrator.Holds(new SceneCondition { MinMargin = 1, MaxMargin = 1 }, third));
        Assert.False(narrator.Holds(new SceneCondition { MinMargin = 2 }, third));
        var consolation = new MatchEvent { Type = MatchEventType.Goal, Side = Side.Away, Minute = 80, HomeScore = 3, AwayScore = 1 };
        Assert.True(narrator.Holds(new SceneCondition { MaxMargin = -2 }, consolation));
    }
}
