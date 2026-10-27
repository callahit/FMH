namespace Fmh.Core.Simulation;

/// <summary>
/// Ereignisbus: stellt Ereignisse synchron in der Reihenfolge der Anmeldung zu, nie parallel. Da die Module in
/// fester Reihenfolge registriert werden, bleibt der Ablauf deterministisch.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) _handlers[typeof(T)] = list = new List<Delegate>();
        list.Add(handler);
    }

    public void Publish<T>(T e) where T : IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) return;
        foreach (var h in list.ToList()) ((Action<T>)h)(e);
    }
}

/// <summary>Merkmal aller Spielereignisse (unveränderliche Records).</summary>
public interface IGameEvent;
