using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Desktop.Services;
using Fmh.MatchEngine;
using Fmh.SceneTree;

namespace Fmh.Tests;

/// <summary>Import des FM2002-Szenenbaums, Kindauswahl und Rückschreiben eines einzelnen Spiels.</summary>
public class SceneTreeTests
{
    private static SceneTreeAsset Tree() => TextmImporter.Load(Path.Combine(TestData.RepoRoot(), "data", "textmode"));

    [Fact]
    public void ImportHasTheKnownShape()
    {
        var tree = Tree();
        Assert.Equal(SceneTreeAsset.ExpectedNodes, tree.Nodes.Count);
        Assert.Equal(SceneTreeAsset.ExpectedCategories, tree.Categories.Count);
        Assert.Equal(SceneTreeAsset.ExpectedLines + 1, tree.Lines.Length);
        Assert.Equal("CE5D50CF878FAA94", tree.Magic);
        Assert.Equal(2u, tree.HeaderVersion);
        Assert.Equal(29, tree.Nodes[0].Children.Count);
        Assert.Equal("FM2002", tree.Text(tree.Nodes[0]));
        Assert.Equal("Elfmeter", tree.Category(1).Name);
        Assert.Equal("Konter über mehrere Stationen", tree.Category(10).Name);
        Assert.Equal("Angriffe über links", tree.Category(17).Name);
        Assert.Equal("Verletzungen", tree.Category(22).Name);
        Assert.Equal("Gelbe Karten für Spielaktionen", tree.Category(23).Name);
        Assert.Equal("Sonstige Texte", tree.Category(29).Name);
        Assert.All(tree.Nodes, n => Assert.Equal(SceneNode.ParameterLength, n.Parameters.Length));
        Assert.False(string.IsNullOrEmpty(tree.Line(1)));
        Assert.False(string.IsNullOrEmpty(tree.Line(13432)));

        var shot = tree.Nodes[2];
        Assert.StartsWith("Schütze", tree.Text(shot));
        Assert.Equal(18, shot.Actor.Code);
        Assert.True(shot.Actor.Attacking);

        var goal = tree.Nodes[4];
        Assert.Equal("Tor!", tree.Text(goal));
        Assert.True(goal.IsLeaf);
        Assert.True(goal.Result.Goal);
        Assert.Equal(11, goal.BaseWeight);
        Assert.Equal(30, goal.ModAAttr);

        var leaf = tree.Nodes[tree.Category(1).NodeIndex];
        var guard = 0;
        while (leaf.Children.Count > 0 && guard++ < 30)
            leaf = tree.Nodes[leaf.Children[0]];
        Assert.True(leaf.Result.Goal);
        Assert.Contains("Tor", tree.Text(leaf));
        Assert.Contains(tree.Nodes, n => (n.Conditions & SceneCondition.Wet) != 0);
        Assert.Contains(tree.Nodes, n => (n.Conditions & SceneCondition.Icy) != 0);
        Assert.Contains(tree.Nodes, n => (n.Conditions & SceneCondition.Floodlit) != 0);
    }

    [Fact]
    public void CommittedAssetMatchesTheImporter()
    {
        var dir = Path.Combine(TestData.RepoRoot(), "data", "textmode");
        var imported = TextmImporter.Load(dir);
        var json = Path.Combine(dir, "scene-tree.json");
        if (Environment.GetEnvironmentVariable("FMH_SCENE_WRITE") == "1")
        {
            SceneAssetStore.Write(imported, json);
            SceneTreeAsset.ClearCache();
        }
        Assert.True(File.Exists(json), "scene-tree.json fehlt. Einmal mit FMH_SCENE_WRITE=1 erzeugen.");
        var loaded = SceneAssetStore.Read(json);
        Assert.Equal(imported.Nodes.Count, loaded.Nodes.Count);
        Assert.Equal(imported.Categories.Select(c => c.Name), loaded.Categories.Select(c => c.Name));
        for (var i = 0; i < imported.Nodes.Count; i += 50)
        {
            Assert.Equal(imported.Nodes[i].Id, loaded.Nodes[i].Id);
            Assert.True(imported.Nodes[i].Parameters.AsSpan().SequenceEqual(loaded.Nodes[i].Parameters));
            Assert.Equal(imported.Nodes[i].ChildCount, loaded.Nodes[i].Children.Count);
        }
        Assert.Equal("Tor!", loaded.Line(loaded.Nodes[4].LineRefs[0]));
    }

    [Fact]
    public void WeightIsGroundPlusBasePlusAttributes()
    {
        var bytes = new byte[SceneNode.ParameterLength];
        bytes[32] = 11;
        var plain = new SceneNode(0, 1, [], bytes, 0);
        var mood = new SceneMood(5, 5, PassingLength.Mixed, 10);
        var options = new SceneWeightOptions { Ground = 1, BaseScale = 1, AttributeScale = 0.5, ModifierFloor = 1, MoodSlope = 0 };
        Assert.Equal(12, SceneWeights.Compute(plain, options, (_, _) => 10, mood));

        var modified = (byte[])bytes.Clone();
        modified[0] = 145;
        modified[2] = 4;
        modified[32] = 0;
        var node = new SceneNode(1, 2, [], modified, 0);
        var weight = SceneWeights.Compute(node, options, (_, index) => index == 4 ? 16 : 10, mood);
        Assert.Equal(1 + 0.5 * 16, weight, 3);

        var inverted = (byte[])modified.Clone();
        inverted[2] = 54;
        var inverse = SceneWeights.Compute(WithBytes(inverted), options, (_, index) => index == 4 ? 16 : 10, mood);
        Assert.Equal(1 + 0.5 * (21 - 16), inverse, 3);
    }

    [Fact]
    public void HardFiltersDropWeatherAndEmptyRoles()
    {
        var bytes = new byte[SceneNode.ParameterLength];
        bytes[27] = (byte)SceneCondition.Wet;
        bytes[33] = 145;
        var node = new SceneNode(0, 1, [], bytes, 0);
        var sunny = new SceneGate { Weather = Weather.Sunny, Occupied = _ => true };
        var rain = new SceneGate { Weather = Weather.Rain, Occupied = role => role.Code != (int)FmRole.St };
        var rainWithStriker = new SceneGate { Weather = Weather.Rain, Occupied = _ => true };
        Assert.False(SceneFilters.Allows(node, sunny));
        Assert.False(SceneFilters.Allows(node, rain));
        Assert.True(SceneFilters.Allows(node, rainWithStriker));

        bytes[27] = (byte)SceneCondition.Floodlit;
        var night = new SceneNode(1, 2, [], bytes, 0);
        Assert.False(SceneFilters.Allows(night, new SceneGate { Weather = Weather.Sunny, Occupied = _ => true }));
        Assert.True(SceneFilters.Allows(night, new SceneGate { Weather = Weather.Sunny, Floodlit = true, Occupied = _ => true }));
    }

    [Fact]
    public void PlaceholdersUsePlayersAndTheReferee()
    {
        var text = SceneNarrative.Apply("1-ST gegen 2-GK, gepfiffen von REF.", new SceneNarrator
        {
            Find = (_, _, _) => null,
            Referee = "Meyer",
        });
        Assert.Equal("der Stürmer gegen der Torwart, gepfiffen von Meyer.", text);
        Assert.Equal(17, SceneNarrative.Code("ST"));
        Assert.Equal(1, SceneNarrative.Code("GK"));
    }

    [Fact]
    public void SameSeedReplaysTheSameTextMatch()
    {
        var session = TestData.NewSession(3);
        var fixture = session.World.AllFixtures.First(f => f.Result is null);
        var setup = session.StartMatch(fixture, humanControlled: false).Setup;
        var first = new SceneMatchEngine().Create(setup);
        var second = new SceneMatchEngine().Create(setup);
        first.RunToEnd();
        second.RunToEnd();
        Assert.True(first.IsFinished);
        Assert.Equal(first.Home.Goals, second.Home.Goals);
        Assert.Equal(first.Away.Goals, second.Away.Goals);
        Assert.Equal(first.Events.Count, second.Events.Count);
        Assert.Equal(first.Events.Select(e => e.Narrative), second.Events.Select(e => e.Narrative));
        Assert.Contains(first.Events, e => e.Type == MatchEventType.HalfTime);
        Assert.Contains(first.Events, e => e.Type == MatchEventType.FullTime);
        Assert.Contains(first.Events, e => !string.IsNullOrEmpty(e.Narrative));
    }

    [Fact]
    public void SceneMatchWritesBackGoalsShotsAndCards()
    {
        var session = TestData.NewSession(9);
        var fixture = session.World.AllFixtures.First(f => f.Result is null && f.Involves(session.HumanClub.Id));
        var setup = session.StartMatch(fixture, humanControlled: true).Setup;
        var scene = new SceneMatchEngine().Create(setup);
        scene.RunToEnd();
        var homeGoals = scene.Home.Goals;
        var awayGoals = scene.Away.Goals;
        session.FinishMatch(fixture, scene);

        var result = fixture.Result;
        Assert.NotNull(result);
        Assert.Equal(homeGoals, result.HomeGoals);
        Assert.Equal(awayGoals, result.AwayGoals);
        Assert.Equal(scene.Home.Stats.Shots, result.HomeShots);
        Assert.Equal(scene.Away.Stats.Shots, result.AwayShots);
        Assert.Equal(Math.Round(scene.Home.Stats.Xg, 2), result.HomeXg);
        Assert.Equal(scene.Events.Count(e => e.Type == MatchEventType.Goal && e.Side == Side.Home), homeGoals);
        Assert.Equal(scene.Events.Count(e => e.Type is MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard), result.Cards.Count);
        Assert.Equal(homeGoals + awayGoals, scene.Home.Participants.Sum(p => p.Goals) + scene.Away.Participants.Sum(p => p.Goals)
            + scene.Events.Count(e => e.Type == MatchEventType.Goal && e.Value == 1));
        Assert.All(scene.Home.Participants.Concat(scene.Away.Participants).Where(p => p.Started), p => Assert.True(p.MinutesPlayed(scene.Minute) > 0));
    }

    [Fact]
    public void KnockoutMatchFinishesWithPenaltiesIfStillLevel()
    {
        var session = TestData.NewSession(4);
        var fixture = session.World.AllFixtures.First(f => f.Kind != CompetitionKind.League && f.Result is null);
        var scene = new SceneMatchEngine().Create(session.StartMatch(fixture, humanControlled: false).Setup);
        scene.RunToEnd();
        Assert.True(scene.IsFinished);
        Assert.InRange(scene.Events.Count, 10, 4000);
        Assert.Contains(scene.Events, e => e.Type == MatchEventType.FullTime);
        if (scene.Setup.Knockout is not null && scene.Home.Goals == scene.Away.Goals)
        {
            Assert.NotNull(scene.PenaltiesHome);
            Assert.NotNull(scene.PenaltiesAway);
            Assert.NotEqual(scene.PenaltiesHome, scene.PenaltiesAway);
        }
    }

    [Fact]
    public void RoutingKeepsTheFastEngineUnlessTheWatchedMatchAsks()
    {
        var session = new GameSession(WorldGenerator.Create(TestData.Data, new NewGameOptions { Seed = 5, ClubKey = "nordheide", ManagerName = "Test" }),
            TestData.Data, new SceneRoutingEngine(new TextMatchEngine(), new SceneMatchEngine()));
        var fixture = session.World.AllFixtures.First(f => f.Result is null);
        var fast = session.StartMatch(fixture, humanControlled: false, sceneText: false);
        fast.RunToEnd();
        var scene = session.StartMatch(fixture, humanControlled: false, sceneText: true);
        scene.RunToEnd();
        Assert.DoesNotContain(fast.Events, e => e.Narrative is not null);
        Assert.Contains(scene.Events, e => e.Narrative is not null);
        Assert.True(fast.Home.Goals + fast.Away.Goals >= 0);
    }

    private static SceneNode WithBytes(byte[] bytes) => new(2, 3, [], bytes, 0);
}
