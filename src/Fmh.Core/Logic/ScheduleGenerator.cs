using System.Globalization;
using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Spielplan nach dem Kreisverfahren. Jede Runde spiegelt die vorherige mit getauschtem Heimrecht.</summary>
public static class ScheduleGenerator
{
    /// <summary>Paarungen je Spieltag (Heim, Gast). Bei ungerader Teamzahl hat pro Spieltag ein Team spielfrei.</summary>
    public static List<List<(int home, int away)>> RoundRobin(IReadOnlyList<int> clubIds, int rounds, Rng rng)
    {
        var teams = clubIds.ToList();
        rng.Shuffle(teams);
        const int bye = -1;
        if (teams.Count % 2 == 1) teams.Add(bye);
        var n = teams.Count;
        var firstRound = new List<List<(int, int)>>();

        for (var day = 0; day < n - 1; day++)
        {
            var pairs = new List<(int, int)>();
            for (var i = 0; i < n / 2; i++)
            {
                var a = teams[i];
                var b = teams[n - 1 - i];
                if (a == bye || b == bye) continue;
                // Heimrecht wechseln, damit niemand lange Heim- oder Auswärtsserien hat.
                var swap = day % 2 == 1;
                pairs.Add(swap ? (b, a) : (a, b));
            }
            firstRound.Add(pairs);
            // Rotation: erstes Team bleibt fest, alle anderen rücken weiter.
            var last = teams[n - 1];
            teams.RemoveAt(n - 1);
            teams.Insert(1, last);
        }

        var result = new List<List<(int, int)>>();
        for (var r = 0; r < rounds; r++)
        {
            foreach (var day in firstRound)
                result.Add(day.Select(p => r % 2 == 0 ? p : (p.Item2, p.Item1)).ToList());
        }
        return result;
    }

    /// <summary>Termine der Spieltage: wöchentlich am konfigurierten Spieltag, ohne Winterpause.</summary>
    public static List<DateOnly> MatchdayDates(SeasonConfig cfg, int seasonYear, int count)
    {
        var first = ParseDate(cfg.FirstMatchday, seasonYear, cfg.StartMonth);
        while (first.DayOfWeek != cfg.MatchDay) first = first.AddDays(1);
        var breakStart = ParseDate(cfg.WinterBreakStart, seasonYear, cfg.StartMonth);
        var breakEnd = ParseDate(cfg.WinterBreakEnd, seasonYear, cfg.StartMonth);

        var dates = new List<DateOnly>();
        var d = first;
        while (dates.Count < count)
        {
            if (breakStart < breakEnd && d >= breakStart && d <= breakEnd)
            {
                d = d.AddDays(7);
                continue;
            }
            dates.Add(d);
            d = d.AddDays(7);
        }
        return dates;
    }

    /// <summary>Datum MM-TT innerhalb der Saison: Monate vor dem Startmonat liegen im Folgejahr.</summary>
    public static DateOnly ParseDate(string monthDay, int seasonYear, int startMonth)
    {
        var parts = monthDay.Split('-');
        var month = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var day = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var year = month < startMonth ? seasonYear + 1 : seasonYear;
        return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    public static void CreateFixtures(World world, League league, CountryConfig country, Rng rng)
    {
        var days = RoundRobin(league.ClubIds, league.Format.Rounds, rng);
        var dates = MatchdayDates(country.Season, world.SeasonYear, days.Count);
        league.Fixtures.Clear();
        for (var i = 0; i < days.Count; i++)
        {
            foreach (var (home, away) in days[i])
            {
                league.Fixtures.Add(new Fixture
                {
                    Id = world.NextFixtureId++,
                    LeagueId = league.Id,
                    Matchday = i + 1,
                    Date = dates[i],
                    HomeId = home,
                    AwayId = away,
                    RefereeId = world.Referees.Count == 0 ? 0 : world.Referees[rng.Next(world.Referees.Count)].Id,
                });
            }
        }
    }
}
