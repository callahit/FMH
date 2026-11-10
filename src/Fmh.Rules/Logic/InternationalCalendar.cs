using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Länderspielfenster (AP 8): Termine je Saison aus den Daten. In einem Fenster ruht der Vereinsbetrieb; Liga- und
/// Pokaltermine weichen aus.
/// </summary>
public static class InternationalCalendar
{
    /// <summary>Fenster der Saison <paramref name="seasonYear"/>: Beginn am ersten Montag ab dem Starttag.</summary>
    public static List<InternationalWindowDates> Windows(InternationalRules rules, int seasonYear)
    {
        var result = new List<InternationalWindowDates>();
        foreach (var w in rules.Windows)
        {
            var start = ScheduleGenerator.ParseDate(w.Start, seasonYear, 7);
            while (start.DayOfWeek != DayOfWeek.Monday) start = start.AddDays(1);
            var end = start.AddDays(w.Days - 1);
            result.Add(new InternationalWindowDates
            {
                Id = w.Id,
                Start = start,
                End = end,
                MatchDates = w.MatchOffsets.Select(o => start.AddDays(o)).Where(d => d <= end).Distinct().OrderBy(d => d).ToList(),
            });
        }
        return result.OrderBy(w => w.Start).ToList();
    }

    /// <summary>Fenster der laufenden Saison festlegen (vor dem Ansetzen der Liga- und Pokalspiele).</summary>
    public static void Plan(World world, GameData data)
    {
        world.InternationalWindows = Windows(data.Internationals, world.SeasonYear);
        world.InternationalFixtures.Clear();
    }

    /// <summary>Verschiebt einen Vereinstermin aus einem Fenster um ganze Wochen nach hinten (gleicher Wochentag).</summary>
    public static DateOnly Avoid(World world, DateOnly date)
    {
        var guard = 0;
        while (world.InInternationalWindow(date) && guard++ < 10) date = date.AddDays(7);
        return date;
    }
}
