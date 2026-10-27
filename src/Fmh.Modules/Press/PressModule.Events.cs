using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

public sealed partial class PressModule
{
    /// <summary>Nach jedem Spiel des Menschen: Schlagzeile und Pressekonferenz, außer er wurde gerade entlassen.</summary>
    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e =>
        {
            if (!e.HumanInvolved || Board.AwaitingNewJob) return;
            MatchHeadline(e.Fixture);
            SchedulePress(e.Fixture, after: true);
        });
    }
}
