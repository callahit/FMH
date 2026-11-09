using Avalonia.Headless.XUnit;
using Fmh.Desktop.Controls;

namespace Fmh.Tests;

/// <summary>Pixel-Art-Atlas der Vereinsgelände-Karte: lädt, und jedes Gebäude, jede Tribüne und jeder Mast hat ein Sprite.</summary>
public class IsoSpritesTests
{
    [AvaloniaFact]
    public void AtlasLoadsFromTheAppResources()
    {
        var atlas = IsoAtlas.Shared;
        Assert.NotNull(atlas);
        Assert.True(atlas!.Count > 150);
        foreach (var n in new[] { "grass_0", "path_0", "gate", "tree_oak_0", "stadium_base" })
            Assert.True(atlas.Has(n), n);
    }

    [AvaloniaFact]
    public void EveryBuildingHasASpriteInEverySizeAndLevel()
    {
        var atlas = IsoAtlas.Shared!;
        var missing = new List<string>();
        foreach (var def in TestData.Data.Buildings)
            foreach (var (w, h) in new[] { (def.Width, def.Height), (def.Height, def.Width) }) // gedreht
                for (var level = 1; level <= Math.Max(1, def.MaxLevel); level++)
                    foreach (var building in new[] { false, true })
                    {
                        var b = new IsoBuildingVisual { Type = def.Id, Category = def.Category, Width = w, Height = h, Level = level, UnderConstruction = building };
                        var name = IsoPixelArt.SpriteFor(b, atlas);
                        if (!atlas.Has(name)) missing.Add($"{def.Id} {w}x{h} L{level} -> {name}");
                        // Jedes Gebäude hat je Grundfläche und Stufe ein eigenes Sprite, die Baustelle bleibt allgemein
                        if (!building && name != $"b_{def.Id}_{w}x{h}_{level}") missing.Add($"{def.Id} {w}x{h} L{level}: eigenes Sprite fehlt ({name})");
                    }
        Assert.True(missing.Count == 0, "Fehlende Sprites: " + string.Join(", ", missing.Distinct().Take(20)));
    }

    [AvaloniaFact]
    public void StadiumHasStandsForEverySideLevelAndRoofAndAllMasts()
    {
        var atlas = IsoAtlas.Shared!;
        foreach (var side in new[] { "North", "South", "West", "East" })
            for (var level = 1; level <= 3; level++)
                foreach (var roof in new[] { 0, 1 })
                    Assert.True(atlas.Has($"stand_{side}_{level}_{roof}"), $"{side} {level} {roof}");
        for (var level = 1; level <= 4; level++) Assert.True(atlas.Has($"mast_{level}"));
    }

    [AvaloniaFact]
    public void StartingGroundsGetGateAndPathFurnitureButNothingBesideTheGate()
    {
        var g = TestData.NewSession().HumanClub.Grounds;
        var state = new IsoMapState { Width = g.Width, Height = g.Height, Paths = g.Paths.ToList(), Gates = g.Gates.ToList() };
        var decos = IsoPixelArt.Decorations(state);
        var atlas = IsoAtlas.Shared!;
        Assert.Equal(g.Gates.Count, decos.Count(d => d.Name == "gate"));
        Assert.Contains(decos, d => d.Name == "deco_lamp");
        Assert.All(decos, d => Assert.True(atlas.Has(d.Name), d.Name));
        foreach (var d in decos.Where(d => d.Name != "gate"))
        {
            var (x, y) = ((int)Math.Floor(d.X), (int)Math.Floor(d.Y));
            Assert.Contains(g.Paths, p => p.X == x && p.Y == y);
            Assert.DoesNotContain(g.Gates, gt => Math.Abs(gt.X - x) <= 1 && Math.Abs(gt.Y - y) <= 1);
        }
        Assert.Equal(decos.OrderBy(d => d.Key).Select(d => d.Key), decos.Select(d => d.Key));
        // Ohne Wegenetz-Anzeige keine Ausstattung
        state.ShowPaths = false;
        Assert.Empty(IsoPixelArt.Decorations(state));
    }

    [Fact]
    public void PlazaNeedsPathOnAllEightNeighbours()
    {
        var block = new HashSet<(int, int)>();
        for (var x = 0; x < 3; x++) for (var y = 0; y < 3; y++) block.Add((x, y));
        Assert.True(IsoPixelArt.IsPlaza(block, 1, 1));
        block.Remove((2, 2));
        Assert.False(IsoPixelArt.IsPlaza(block, 1, 1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2500, 1)]
    [InlineData(4000, 1)]
    [InlineData(8000, 2)]
    [InlineData(12001, 3)]
    public void StandSizeFollowsCapacity(int capacity, int level) => Assert.Equal(level, IsoPixelArt.StandLevel(capacity));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void HouseFloorsFollowLevel(int level, int floors) => Assert.Equal(floors, IsoPixelArt.Floors(level));
}
