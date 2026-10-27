using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Simulation;

/// <summary>Gemeinsamer Zustand aller Module: Spielstand, Datenpakete, der eine Zufallsgenerator, Engine, Dienste und Ereignisbus.</summary>
public sealed class GameKernel
{
    public GameKernel(World world, GameData data, IMatchEngine engine, Rng rng)
    {
        World = world;
        Data = data;
        Engine = engine;
        Rng = rng;
    }

    public World World { get; }
    public GameData Data { get; }
    public IMatchEngine Engine { get; }

    /// <summary>Einziger Zufallsgenerator der Simulation; die Reihenfolge der Ziehungen ist Teil des Golden Master.</summary>
    public Rng Rng { get; }

    public ServiceRegistry Services { get; } = new();
    public EventBus Bus { get; } = new();
}
