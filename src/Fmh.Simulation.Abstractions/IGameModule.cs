namespace Fmh.Core.Simulation;

/// <summary>
/// Spielmodul (Umsetzungskonzept 4.2): besitzt ein Fachgebiet, meldet seine Dienste an, abonniert Ereignisse
/// und rechnet einmal am Tag. Module kennen einander nur über Ereignisse und Dienst-Schnittstellen.
/// </summary>
public interface IGameModule
{
    /// <summary>Kennung des Moduls, z. B. "finance".</summary>
    string Id { get; }

    /// <summary>Module, deren Tagesschritt vorher laufen muss; bestimmt die feste Ausführungsreihenfolge.</summary>
    IReadOnlyList<string> DependsOn { get; }

    /// <summary>Dienste anmelden, Ereignisse abonnieren.</summary>
    void Register(ModuleContext ctx);

    /// <summary>Täglicher Schritt, deterministisch.</summary>
    void OnDay(DayContext day);
}

/// <summary>Was ein Modul bei der Registrierung bekommt: gemeinsamer Spielzustand, Dienste und Ereignisbus.</summary>
public sealed class ModuleContext
{
    public ModuleContext(GameKernel kernel) => Kernel = kernel;

    public GameKernel Kernel { get; }
    public ServiceRegistry Services => Kernel.Services;
    public EventBus Bus => Kernel.Bus;
}

/// <summary>Ein Kalendertag: heutige Spiele und beteiligte Vereine; ein Modul kann den Ablauf anhalten.</summary>
public sealed class DayContext
{
    public DayContext(DateOnly date, IReadOnlyList<Fmh.Core.Model.Fixture> fixtures)
    {
        Date = date;
        Fixtures = fixtures;
        Playing = fixtures.SelectMany(f => new[] { f.HomeId, f.AwayId }).ToHashSet();
    }

    public DateOnly Date { get; }
    public IReadOnlyList<Fmh.Core.Model.Fixture> Fixtures { get; }
    public IReadOnlySet<int> Playing { get; }

    /// <summary>Grund, an dem "Weiter" nach diesem Tag anhält (z. B. Saisonende).</summary>
    public Fmh.Core.Game.StopReason? Stop { get; set; }
}
