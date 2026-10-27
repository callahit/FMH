using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Tagesschritt der Saison: Wettbewerbe abschließen; zu Tagesbeginn Saisonwechsel und feste Termine.</summary>
public sealed partial class SeasonModule
{
    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<DayStarted>(e =>
        {
            if (StartNewSeasonIfDue()) e.Day.Stop = StopReason.NewSeason;
            else CalendarEvents();
        });
    }

    public override void OnDay(DayContext day)
    {
        if (World.SeasonFinished || !Cups.CompetitionsDaily()) return;
        FinishSeason();
        day.Stop = StopReason.SeasonEnd;
    }
}
