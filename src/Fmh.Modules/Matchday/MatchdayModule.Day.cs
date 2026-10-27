using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

public sealed partial class MatchdayModule
{
    /// <summary>Alle heutigen Spiele ohne menschliche Beteiligung berechnen, danach Erholung und Heilung.</summary>
    public override void OnDay(DayContext day)
    {
        foreach (var f in day.Fixtures.Where(f => f.Result is null))
        {
            if (HumanPlays(f)) continue;
            PlayInstant(f);
        }
        DailyUpdate();
    }
}
