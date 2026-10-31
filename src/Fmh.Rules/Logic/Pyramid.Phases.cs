using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Saisonphasen: Nach dem Grunddurchgang werden die Folgephasen nacheinander angesetzt.</summary>
public static partial class Pyramid
{
    /// <summary>
    /// Setzt die nächste noch fehlende Folgephase jeder Liga an, sobald alle bisherigen Spiele ausgewertet sind.
    /// true, wenn etwas angesetzt wurde.
    /// </summary>
    public static bool SchedulePhases(World world, Rng rng)
    {
        var any = false;
        foreach (var league in world.Leagues.Where(l => l.PhasesScheduled < l.Format.Phases.Count))
        {
            if (league.Fixtures.Count == 0 || league.Fixtures.Any(f => f.Result is null)) continue;
            var index = league.PhasesScheduled;
            var country = world.Country(league.CountryCode);
            var phase = league.Format.Phases[index];
            var table = LeagueTable.Compute(league);
            var last = league.Fixtures.Max(f => f.Date);
            var start = (world.Date > last ? world.Date : last).AddDays(3);
            var firstMatchday = league.MatchdayCount;
            foreach (var group in phase.Groups)
            {
                var ids = group.PositionList().Where(p => p <= table.Count).Select(p => table[p - 1].ClubId).ToList();
                var days = ScheduleGenerator.RoundRobin(ids, phase.Rounds, rng);
                var dates = ScheduleGenerator.WeeklyDates(country.Season, world.SeasonYear, start, days.Count);
                for (var d = 0; d < days.Count; d++)
                    foreach (var (home, away) in days[d])
                        league.Fixtures.Add(new Fixture
                        {
                            Id = world.NextFixtureId++,
                            CompetitionId = league.Id,
                            Kind = CompetitionKind.League,
                            Matchday = firstMatchday + d + 1,
                            Phase = index + 1,
                            Date = dates[d],
                            HomeId = home,
                            AwayId = away,
                            RefereeId = world.Referees.Count == 0 ? 0 : world.Referees[rng.Next(world.Referees.Count)].Id,
                        });
            }
            any = true;
        }
        return any;
    }
}
