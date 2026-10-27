namespace Fmh.Core.Simulation;

/// <summary>Schmale Dienst-Schnittstellen der Module (z. B. <c>IFinanceService</c>); je Schnittstelle genau eine Umsetzung.</summary>
public sealed class ServiceRegistry
{
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>Meldet ein Modul unter allen Dienst-Schnittstellen an, die es umsetzt.</summary>
    public void Add(object implementation)
    {
        foreach (var i in implementation.GetType().GetInterfaces().Where(i => i.Namespace == typeof(ServiceRegistry).Namespace && i.Name.EndsWith("Service")))
        {
            if (!_services.TryAdd(i, implementation))
                throw new InvalidOperationException($"Dienst {i.Name} ist bereits angemeldet.");
        }
    }

    public T Get<T>() where T : class =>
        _services.TryGetValue(typeof(T), out var s) ? (T)s : throw new InvalidOperationException($"Dienst {typeof(T).Name} ist nicht angemeldet.");
}
