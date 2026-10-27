using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

public sealed partial class CupsModule
{
    /// <summary>Nach jedem Pokal- oder Relegationsspiel: Duell auswerten, nächste Runde auslosen.</summary>
    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e =>
        {
            if (e.Fixture.Kind != CompetitionKind.League && World.FindCup(e.Fixture.CompetitionId) is { } cup)
                CupMatchPlayed(cup, e.Fixture);
        });
    }
}
