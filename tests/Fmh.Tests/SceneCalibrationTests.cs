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
        double sceneInjuries = 0, fastInjuries = 0;

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
            fastInjuries += fast.Home.Stats.Injuries + fast.Away.Stats.Injuries;
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
        var finj = Avg(fastInjuries, matches);
        _out.WriteLine($"Tore {sg:0.00} (schnell {fg:0.00})");
        _out.WriteLine($"Schüsse {ss:0.00} (schnell {fs:0.00}), aufs Tor {so:0.00} (schnell {fo:0.00}), xG {sx:0.00} (schnell {fx:0.00})");
        _out.WriteLine($"Ecken {sc:0.00} (schnell {fc:0.00}), Fouls {sf:0.00} (schnell {ff:0.00}), Abseits {soff:0.00} (schnell {foff:0.00})");
        _out.WriteLine($"Gelb {sy:0.00} (schnell {fy:0.00}), Rot {sr:0.00} (schnell {fr:0.00}), Verletzungen {inj:0.00} (schnell {finj:0.00})");

        // Etwa ±13 % der schnellen Engine. Seltene Ereignisse (Rot, Verletzung) etwas weiter, weil die Stichprobe klein ist.
        Near(sg, fg, 0.13);
        Near(ss, fs, 0.13);
        Near(so, fo, 0.13);
        Near(sx, fx, 0.13);
        Near(sc, fc, 0.13);
        Near(sf, ff, 0.13);
        Near(soff, foff, 0.13);
        Near(sy, fy, 0.13);
        Near(sr, fr, 0.20);
        Near(inj, finj, 0.20);
    }

    private static void Near(double actual, double expected, double tolerance)
    {
        if (expected < 0.05) expected = Math.Max(expected, 0.05);
        Assert.InRange(actual, expected * (1 - tolerance), expected * (1 + tolerance));
    }
}
