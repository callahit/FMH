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
                        var name = IsoPixelArt.SpriteFor(b);
                        if (!atlas.Has(name)) missing.Add($"{def.Id} {w}x{h} L{level} -> {name}");
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
