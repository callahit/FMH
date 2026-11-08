using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>G-33: PathGraph, Migration, Platzierung und Wirkungsformel des Vereinsgeländes.</summary>
public class GroundsPathTests
{
    [Fact]
    public void PathGraph_BfsFromGatesMarksDistances()
    {
        var g = new Grounds
        {
            Width = 6,
            Height = 4,
            MapVersion = 1,
            StadiumX = 2,
            StadiumY = 0,
            StadiumWidth = 2,
            StadiumHeight = 1,
            Gates = new List<GridPos> { new(2, 1) },
            Paths = new List<GridPos> { new(2, 1), new(2, 2), new(2, 3), new(3, 3), new(4, 3) },
        };
        var graph = PathGraph.Build(g, 6);
        Assert.Equal(0, graph.DistanceTo(2, 1));
        Assert.Equal(2, graph.DistanceTo(2, 3));
        Assert.Equal(4, graph.DistanceTo(4, 3));
        Assert.Equal(PathGraph.Unreachable, graph.DistanceTo(0, 0));
    }

    [Fact]
    public void LageBonus_UsesAnstossFormula()
    {
        Assert.Equal(1.5, PathGraph.LageBonus(0), 3);
        Assert.Equal(1.0, PathGraph.LageBonus(5), 3);
        Assert.Equal(1.0, PathGraph.LageBonus(9), 3);
        Assert.Equal(1.2, PathGraph.LageBonus(3), 3);
        Assert.Equal(5, PathGraph.Manhattan(1, 2, 4, 4));
    }

    [Fact]
    public void DrawPathL_FillsHorizontalThenVertical()
    {
        var cells = PathGraph.DrawPathL(0, 0, 2, 2, (_, _) => true);
        Assert.Contains(new GridPos(0, 0), cells);
        Assert.Contains(new GridPos(2, 0), cells);
        Assert.Contains(new GridPos(2, 2), cells);
        Assert.Equal(5, cells.Count); // (0,0)(1,0)(2,0)(2,1)(2,2)
    }

    [Fact]
    public void BuildingFootprint_SwapsOnRotation()
    {
        var def = new Fmh.Core.Data.BuildingDef { Width = 3, Height = 1 };
        Assert.Equal((3, 1), BuildingFootprint.Size(def, 0));
        Assert.Equal((1, 3), BuildingFootprint.Size(def, 90));
        Assert.Equal((3, 1), BuildingFootprint.Size(def, 180));
        Assert.Equal(90, BuildingFootprint.NormalizeRotation(450));
    }

    [Fact]
    public void Grounds_EnsureCurrent_MigratesLegacySaves()
    {
        var g = new Grounds
        {
            MapVersion = 0,
            Width = 8,
            Height = 6,
            StadiumX = 2,
            StadiumY = 0,
            Buildings = new List<Building> { new() { Id = 1, Type = "office", X = 5, Y = 3, Rotation = 15 } },
        };
        g.EnsureCurrent();
        Assert.Equal(1, g.MapVersion);
        Assert.NotEmpty(g.Gates);
        Assert.NotEmpty(g.Paths);
        Assert.Equal(0, g.Buildings[0].Rotation);
    }

    [Fact]
    public void NewGame_HasPathsGatesAndMapVersion()
    {
        var s = TestData.NewSession();
        var g = s.HumanClub.Grounds;
        Assert.Equal(1, g.MapVersion);
        Assert.NotEmpty(g.Gates);
        Assert.NotEmpty(g.Paths);
        Assert.True(s.PathGraphOf(s.HumanClub).Distances.Count > 0);
        var connected = g.Buildings.Count(b => s.IsBuildingConnected(s.HumanClub, b));
        Assert.True(connected >= g.Buildings.Count * 0.8, $"angeschlossen {connected}/{g.Buildings.Count}");
    }

    [Fact]
    public void DisconnectedBuilding_HalvesEffect()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        // Einzelnes Gebäude mit bekannter Wirkung isolieren
        var def = s.Data.Buildings.First(d => d.Effects.Count > 0 && !d.Land);
        var key = def.Effects.Keys.First();
        club.Grounds.Buildings.Clear();
        club.Grounds.Paths.Clear();
        club.Grounds.Gates = new List<GridPos> { new(club.Grounds.EntranceX, Math.Min(club.Grounds.Height - 1, club.Grounds.EntranceY)) };
        var building = new Building { Id = 99, Type = def.Id, X = 0, Y = club.Grounds.Height - 1, Level = 1, Condition = 100 };
        club.Grounds.Buildings.Add(building);
        // Weg vom Gate zum Gebäude
        Assert.Null(s.DrawPath(club.Grounds.Gates[0].X, club.Grounds.Gates[0].Y, building.X, building.Y, free: true));
        s.InvalidateEffects(club);
        Assert.True(s.IsBuildingConnected(club, building));
        var before = s.Effect(club, key);
        Assert.True(before > 0);
        club.Grounds.Paths.Clear();
        club.Grounds.Gates.Clear();
        s.InvalidateEffects(club);
        Assert.False(s.IsBuildingConnected(club, building));
        var after = s.Effect(club, key);
        Assert.InRange(after / before, 0.45, 0.55);
    }

    [Fact]
    public void DrawPath_ChargesAndConnects()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        club.Budget.Balance += 1_000_000;
        club.Grounds.Paths.Clear();
        club.Grounds.Gates = new List<GridPos> { new(club.Grounds.EntranceX, Math.Min(club.Grounds.Height - 1, club.Grounds.EntranceY)) };
        s.InvalidateEffects(club);
        var balance = club.Budget.Balance;
        var gate = club.Grounds.Gates[0];
        Assert.Null(s.DrawPath(gate.X, gate.Y, gate.X, Math.Min(club.Grounds.Height - 1, gate.Y + 2)));
        Assert.True(club.Budget.Balance < balance);
        Assert.NotEmpty(club.Grounds.Paths);
    }

    [Fact]
    public void Demolish_RefundsQuarter()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        var building = club.Grounds.Buildings.First(b => s.Data.Building(b.Type) is { Land: false });
        var def = s.Data.Building(building.Type)!;
        var balance = club.Budget.Balance;
        Assert.Null(s.Demolish(building));
        Assert.Equal(balance + MarketValue.Round(ClubRules.LevelCost(def, Math.Max(1, building.Level)) * 0.25), club.Budget.Balance);
    }

    [Fact]
    public void SaveLoad_PreservesPathsAndRotation()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        club.Grounds.Buildings[0].Rotation = 90;
        var pathCount = club.Grounds.Paths.Count;
        var path = Path.Combine(Path.GetTempPath(), "fmh-grounds-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            SqliteStore.Save(s.World, path);
            var w = SqliteStore.Load(path);
            var c = w.Clubs[club.Id];
            Assert.Equal(1, c.Grounds.MapVersion);
            Assert.Equal(pathCount, c.Grounds.Paths.Count);
            Assert.Equal(90, c.Grounds.Buildings[0].Rotation);
            _ = new GameSession(w, TestData.Data, new TextMatchEngine());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
