using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Saisonphasen: Nach dem Grunddurchgang werden die Gruppen der zweiten Phase angesetzt.</summary>
public static partial class Pyramid
{
    /// <summary>Setzt die zweite Phase jeder Liga an, deren Grunddurchgang gerade beendet ist. true, wenn etwas angesetzt wurde.</summary>
    public static bool SchedulePhases(World world, Rng rng)
    {
        var any = false;
        foreach (var league in world.Leagues.Where(l => l.Format.Phases.Count > 0 && !l.PhaseScheduled))
        {
            if (league.Fixtures.Count == 0 || league.Fixtures.Any(f => f.Result is null)) continue;
            var country = world.Country(league.CountryCode);
            var phase = league.Format.Phases[0];
            var table = LeagueTable.Compute(league);
            var last = league.Fixtures.Max(f => f.Date);
            var start = (world.Date > last ? world.Date : last).AddDays(3);
            while (start.DayOfWeek != country.Season.MatchDay) start = start.AddDays(1);
            foreach (var group in phase.Groups)
            {
                var ids = group.PositionList().Where(p => p <= table.Count).Select(p => table[p - 1].ClubId).ToList();
                var days = ScheduleGenerator.RoundRobin(ids, phase.Rounds, rng);
                for (var d = 0; d < days.Count; d++)
                    foreach (var (home, away) in days[d])
                        league.Fixtures.Add(new Fixture
                        {
                            Id = world.NextFixtureId++,
                            CompetitionId = league.Id,
                            Kind = CompetitionKind.League,
                            Matchday = league.RegularMatchdays + d + 1,
                            Date = start.AddDays(7 * d),
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
