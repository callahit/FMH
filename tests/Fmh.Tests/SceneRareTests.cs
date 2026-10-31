using Fmh.Core.Model;
using Fmh.SceneTree;
using Xunit.Abstractions;

namespace Fmh.Tests;

public class SceneRareTests
{
    private readonly ITestOutputHelper _out;
    public SceneRareTests(ITestOutputHelper output) => _out = output;
    [Fact]
    public void ConditionsKeepWeatherAndFloodlightTextsApart()
    {
        Assert.False(SceneRare.Allows(RareKind.Floodlight, Weather.Sunny, floodlit: false, easterEggs: false));
        Assert.True(SceneRare.Allows(RareKind.Floodlight, Weather.Sunny, floodlit: true, easterEggs: false));
        Assert.False(SceneRare.Allows(RareKind.Thunder, Weather.Sunny, floodlit: false, easterEggs: false));
        Assert.True(SceneRare.Allows(RareKind.Thunder, Weather.Rain, floodlit: false, easterEggs: false));
        Assert.False(SceneRare.Allows(RareKind.Wet, Weather.Sunny, floodlit: false, easterEggs: false));
        Assert.True(SceneRare.Allows(RareKind.Wet, Weather.Snow, floodlit: false, easterEggs: false));
        Assert.False(SceneRare.Allows(RareKind.Easter, Weather.Rain, floodlit: true, easterEggs: false));
        Assert.True(SceneRare.Allows(RareKind.Easter, Weather.Rain, floodlit: true, easterEggs: true));
        Assert.True(SceneRare.Allows(RareKind.Crowd, Weather.Sunny, floodlit: false, easterEggs: false));
    }

    [Fact]
    public void OneSceneMatchStaysShort()
    {
        var session = TestData.NewSession(19);
        var fixture = session.World.AllFixtures.First(f => f.Result is null);
        var scene = (SceneSimulation)new SceneMatchEngine(options: new SceneWeightOptions { EmitNarrative = false }).Create(session.StartMatch(fixture, false).Setup);
        scene.RunToEnd();
        Assert.InRange(scene.Events.Count, 20, 2500);
    }

    [Fact]
    public void EveryOrdinaryRareTextAppearsAndEasterEggsStayOff()
    {
        const int matches = 1000;
        var session = TestData.NewSession(19);
        var world = session.World;
        var seen = new HashSet<string>();
        var total = 0;
        for (var i = 0; i < matches; i++)
        {
            var league = world.Leagues.First(l => l.ClubIds.Count >= 2);
            var home = league.ClubIds[i % league.ClubIds.Count];
            var away = league.ClubIds[(i + 1) % league.ClubIds.Count];
            if (away == home) away = league.ClubIds[(i + 2) % league.ClubIds.Count];
            var month = (i % 12) + 1;
            var fixture = new Fixture
            {
                Id = 3_000_000 + i,
                CompetitionId = league.Id,
                Kind = CompetitionKind.League,
                HomeId = home,
                AwayId = away,
                Date = new DateOnly(world.SeasonYear, month, 10 + (i % 15)),
                RefereeId = world.Referees[i % world.Referees.Count].Id,
            };
            var scene = (SceneSimulation)new SceneMatchEngine(options: new SceneWeightOptions { EmitNarrative = false, EasterEggs = false })
                .Create(session.StartMatch(fixture, humanControlled: false).Setup);
            scene.RunToEnd();
            foreach (var tag in scene.RareTags)
            {
                seen.Add(tag);
                total++;
            }
            Assert.DoesNotContain(scene.RareTags, tag => tag is "karim" or "marcel" or "easter");
        }
        var rate = total / (double)matches;
        _out.WriteLine($"Sonstige Texte {rate:0.00} je Spiel, {seen.Count} verschiedene");
        Assert.InRange(rate, 0.18, 0.45);
        var missing = SceneRare.RequiredTags.Where(tag => !seen.Contains(tag)).ToList();
        Assert.True(missing.Count == 0, "Fehlt: " + string.Join(", ", missing));
    }
}
