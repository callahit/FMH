using Fmh.Core.Simulation;

namespace Fmh.Core.Game;

/// <summary>Modul-Registry: feste Ausführungsreihenfolge aus <see cref="IGameModule.DependsOn"/>, bei Gleichstand die Anmeldereihenfolge.</summary>
public static class ModuleRegistry
{
    public static IReadOnlyList<IGameModule> Order(IReadOnlyList<IGameModule> modules)
    {
        var ids = modules.Select(m => m.Id).ToList();
        if (ids.Distinct().Count() != ids.Count) throw new InvalidOperationException("Modulkennungen sind nicht eindeutig.");
        foreach (var m in modules)
            foreach (var d in m.DependsOn.Where(d => !ids.Contains(d)))
                throw new InvalidOperationException($"Modul {m.Id} hängt von unbekanntem Modul {d} ab.");
        var done = new List<IGameModule>();
        var open = modules.ToList();
        while (open.Count > 0)
        {
            var next = open.FirstOrDefault(m => m.DependsOn.All(d => done.Any(x => x.Id == d)))
                ?? throw new InvalidOperationException($"Zyklische Modulabhängigkeit: {string.Join(", ", open.Select(m => m.Id))}");
            done.Add(next);
            open.Remove(next);
        }
        return done;
    }
}
