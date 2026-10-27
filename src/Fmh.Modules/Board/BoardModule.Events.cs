using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

public sealed partial class BoardModule
{
    /// <summary>Nach jedem Spiel des Menschen: Vertrauen, Fans, Ultimatum, Karriere (kann zur Entlassung führen).</summary>
    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e =>
        {
            if (e.HumanInvolved) AfterHumanMatch(e.Fixture);
        });
    }
}
