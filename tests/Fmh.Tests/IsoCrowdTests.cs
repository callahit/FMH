using Avalonia.Headless.XUnit;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;

namespace Fmh.Tests;

/// <summary>Leben auf dem Vereinsgelände: Besucher bleiben auf Wegen, Spieler auf ihrem Platz, alles reproduzierbar.</summary>
public class IsoCrowdTests
{
    private static IsoMapState State(bool underConstruction = false)
    {
        var paths = new List<GridPos>();
        for (var x = 0; x < 10; x++) paths.Add(new GridPos(x, 4));
        for (var y = 0; y < 4; y++) paths.Add(new GridPos(5, y));
        return new IsoMapState
        {
            Width = 12, Height = 9, Paths = paths, Gates = new List<GridPos> { new(5, 5) },
            Buildings = new List<IsoBuildingVisual>
            {
                new() { Id = 1, Type = "trainingPitch", X = 0, Y = 6, Width = 3, Height = 2, Level = 3, UnderConstruction = underConstruction },
                new() { Id = 2, Type = "gym", X = 7, Y = 6, Width = 1, Height = 1, Level = 1 },
            },
        };
    }

    [Fact]
    public void VisitorsStayOnPathsAndPlayersOnTheirPitch()
    {
        var s = State();
        var crowd = new IsoCrowd();
        crowd.Reset(s);
        var walkable = s.Paths.Select(p => (p.X, p.Y)).Concat(s.Gates.Select(g => (g.X, g.Y))).ToHashSet();
        Assert.Contains(crowd.Positions, p => !p.player);
        Assert.Equal(4 + 2 * 3, crowd.Positions.Count(p => p.player));
        for (var i = 0; i < 600; i++)
        {
            crowd.Step(0.1);
            foreach (var (x, y, player) in crowd.Positions)
            {
                if (player) Assert.True(x >= 0.3 && x <= 2.7 && y >= 6.3 && y <= 7.7, $"Spieler bei {x:F2},{y:F2}");
                else Assert.Contains(((int)Math.Floor(x), (int)Math.Floor(y)), walkable);
            }
        }
    }

    [Fact]
    public void SameSeedGivesSameMovement()
    {
        var a = new IsoCrowd();
        var b = new IsoCrowd();
        a.Reset(State(), 3);
        b.Reset(State(), 3);
        for (var i = 0; i < 50; i++) { a.Step(0.1); b.Step(0.1); }
        Assert.Equal(a.Positions, b.Positions);
    }

    [Fact]
    public void NoPlayersOnPitchUnderConstructionAndNoVisitorsWithoutPaths()
    {
        var crowd = new IsoCrowd();
        crowd.Reset(State(underConstruction: true));
        Assert.DoesNotContain(crowd.Positions, p => p.player);
        crowd.Reset(new IsoMapState { Width = 5, Height = 5 });
        Assert.Equal(0, crowd.Count);
        crowd.Reset(null);
        Assert.Equal(0, crowd.Count);
    }

    [AvaloniaFact]
    public void EveryFigureFrameExistsInTheAtlas()
    {
        var crowd = new IsoCrowd();
        crowd.Reset(State());
        var atlas = IsoAtlas.Shared!;
        for (var i = 0; i < 40; i++)
        {
            crowd.Step(0.1);
            Assert.All(crowd.Sprites(), d => Assert.True(atlas.Has(d.Name), d.Name));
        }
        // Spieler werden über ihrem Platz gezeichnet (Schlüssel = vordere Ecke des Platzes)
        Assert.All(crowd.Sprites().Where(d => d.Name.Contains("player")), d => Assert.Equal(0 + 3 + 6 + 2, d.Key));
    }
}
