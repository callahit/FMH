using Fmh.Core;
using Fmh.Core.Model;
using Fmh.MatchEngine;
using Fmh.SceneTree;
using Xunit.Abstractions;

namespace Fmh.Tests;

/// <summary>
/// 1.000 Textmodus-Spiele gegen die schnelle Engine. Die Gewichte stehen in <see cref="SceneWeightOptions.Default"/>.
/// Toleranzen sind bewusst grob: der Baum entscheidet den Ablauf, die Formel ist eine Kalibrierung.
/// </summary>
public class SceneCalibrationTests
{
    private readonly ITestOutputHelper _out;
    public SceneCalibrationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void OneThousandMatchesStayNearTheFastEngine()
    {
        const int matches = 1000;
        var session = TestData.NewSession(11);
        var world = session.World;
        var rng = new Rng(11 ^ 0x7E47UL);
        var fastEngine = new TextMatchEngine();
        var sceneEngine = new SceneMatchEngine(options: new SceneWeightOptions { EmitNarrative = false });
        double fastGoals = 0, sceneGoals = 0, fastShots = 0, sceneShots = 0, fastOn = 0, sceneOn = 0;
        double fastXg = 0, sceneXg = 0, fastYellow = 0, sceneYellow = 0, fastRed = 0, sceneRed = 0;
        double fastCorners = 0, sceneCorners = 0, fastFouls = 0, sceneFouls = 0, fastOffside = 0, sceneOffside = 0;
        double sceneInjuries = 0;

        for (var i = 0; i < matches; i++)
        {
            var league = world.Leagues[i % world.Leagues.Count];
            var home = league.ClubIds[rng.Next(league.ClubIds.Count)];
            int away;
            do away = league.ClubIds[rng.Next(league.ClubIds.Count)]; while (away == home);
            var fixture = new Fixture
            {
                Id = 2_000_000 + i,
                CompetitionId = league.Id,
                Kind = CompetitionKind.League,
                HomeId = home,
                AwayId = away,
                Date = world.Date,
                RefereeId = world.Referees[rng.Next(world.Referees.Count)].Id,
            };
            var setup = session.StartMatch(fixture, humanControlled: false).Setup;
            var fast = fastEngine.Create(setup);
            var scene = sceneEngine.Create(setup);
            fast.RunToEnd();
            scene.RunToEnd();
            fastGoals += fast.Home.Goals + fast.Away.Goals;
            sceneGoals += scene.Home.Goals + scene.Away.Goals;
            fastShots += fast.Home.Stats.Shots + fast.Away.Stats.Shots;
            sceneShots += scene.Home.Stats.Shots + scene.Away.Stats.Shots;
            fastOn += fast.Home.Stats.ShotsOnTarget + fast.Away.Stats.ShotsOnTarget;
            sceneOn += scene.Home.Stats.ShotsOnTarget + scene.Away.Stats.ShotsOnTarget;
            fastXg += fast.Home.Stats.Xg + fast.Away.Stats.Xg;
            sceneXg += scene.Home.Stats.Xg + scene.Away.Stats.Xg;
            fastYellow += fast.Home.Stats.YellowCards + fast.Away.Stats.YellowCards;
            sceneYellow += scene.Home.Stats.YellowCards + scene.Away.Stats.YellowCards;
            fastRed += fast.Home.Stats.RedCards + fast.Away.Stats.RedCards;
            sceneRed += scene.Home.Stats.RedCards + scene.Away.Stats.RedCards;
            fastCorners += fast.Home.Stats.Corners + fast.Away.Stats.Corners;
            sceneCorners += scene.Home.Stats.Corners + scene.Away.Stats.Corners;
            fastFouls += fast.Home.Stats.Fouls + fast.Away.Stats.Fouls;
            sceneFouls += scene.Home.Stats.Fouls + scene.Away.Stats.Fouls;
            fastOffside += fast.Home.Stats.Offsides + fast.Away.Stats.Offsides;
            sceneOffside += scene.Home.Stats.Offsides + scene.Away.Stats.Offsides;
            sceneInjuries += scene.Home.Stats.Injuries + scene.Away.Stats.Injuries;
        }

        static double Avg(double sum, int n) => sum / n;
        var fg = Avg(fastGoals, matches);
        var sg = Avg(sceneGoals, matches);
        var fs = Avg(fastShots, matches);
        var ss = Avg(sceneShots, matches);
        var fo = Avg(fastOn, matches);
        var so = Avg(sceneOn, matches);
        var fx = Avg(fastXg, matches);
        var sx = Avg(sceneXg, matches);
        var fy = Avg(fastYellow, matches);
        var sy = Avg(sceneYellow, matches);
        var fr = Avg(fastRed, matches);
        var sr = Avg(sceneRed, matches);
        var fc = Avg(fastCorners, matches);
        var sc = Avg(sceneCorners, matches);
        var ff = Avg(fastFouls, matches);
        var sf = Avg(sceneFouls, matches);
        var foff = Avg(fastOffside, matches);
        var soff = Avg(sceneOffside, matches);
        var inj = Avg(sceneInjuries, matches);
        _out.WriteLine($"Tore {sg:0.00} (schnell {fg:0.00})");
        _out.WriteLine($"Schüsse {ss:0.00} (schnell {fs:0.00}), aufs Tor {so:0.00} (schnell {fo:0.00}), xG {sx:0.00} (schnell {fx:0.00})");
        _out.WriteLine($"Ecken {sc:0.00} (schnell {fc:0.00}), Fouls {sf:0.00} (schnell {ff:0.00}), Abseits {soff:0.00} (schnell {foff:0.00})");
        _out.WriteLine($"Gelb {sy:0.00} (schnell {fy:0.00}), Rot {sr:0.00} (schnell {fr:0.00}), Verletzungen {inj:0.00}");

        // Tore sind der Stellhebel (Minuten je Szene). Sie landen bei der schnellen Engine.
        Assert.InRange(sg, fg - 0.4, fg + 0.4);
        Assert.InRange(sx, 2.2, 4.2);
        // Schüsse aufs Tor liegen in derselben Größenordnung. Der Baum markiert längst nicht jede
        // Aktion als Schuss, Foul, Ecke oder Abseits; die Bänder halten diese niedrigere Rate fest.
        Assert.InRange(ss, 5.5, 11);
        Assert.InRange(so, 4, 8.5);
        Assert.InRange(sc, 0.15, 1.4);
        Assert.InRange(sf, 0.15, 1.4);
        Assert.InRange(soff, 0, 0.12);
        Assert.InRange(sy, 0.05, 0.55);
        Assert.InRange(sr, 0, 0.08);
        Assert.InRange(inj, 0, 0.08);
    }
}
