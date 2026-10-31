using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public static partial class EuropeLogic
{
    public static void ScheduleLeague(World world, GameData data, Cup cup, CupRound round, List<int> teams, Rng rng)
    {
        var target = data.Europe?.Competition(cup.Id)?.TargetLeagueSize ?? 8;
        if (target % 2 == 1) target--;
        var pool = teams.Distinct().ToList();
        if (pool.Count > target)
            pool = pool
                .OrderByDescending(id => world.ClubCoefficients.GetValueOrDefault(id, world.Clubs[id].Reputation))
                .ThenBy(id => id)
                .Take(target)
                .ToList();
        if (pool.Count % 2 == 1)
            pool = pool
                .OrderByDescending(id => world.ClubCoefficients.GetValueOrDefault(id, world.Clubs[id].Reputation))
                .ThenBy(id => id)
                .Take(pool.Count - 1)
                .ToList();
        if (pool.Count < 2) return;

        round.Entrants = pool;
        round.Teams = pool.Count;
        foreach (var id in pool) TouchEntry(world, cup.Id, id, round.Number - 1, eliminated: false);

        var days = ScheduleGenerator.RoundRobin(pool, rounds: 1, rng);
        var dates = LeagueDates(round, days.Count, world.SeasonYear);
        for (var d = 0; d < days.Count; d++)
        {
            var date = dates[Math.Min(d, dates.Count - 1)];
            foreach (var (home, away) in days[d])
            {
                var when = AvoidLeagueClash(world, date, home, away);
                cup.Fixtures.Add(new Fixture
                {
                    Id = world.NextFixtureId++,
                    CompetitionId = cup.Id,
                    Kind = CompetitionKind.Europe,
                    Matchday = round.Number * 100 + d + 1,
                    Date = when,
                    HomeId = home,
                    AwayId = away,
                    RefereeId = world.Referees.Count == 0 ? 0 : world.Referees[(home * 13 + away + d) % world.Referees.Count].Id,
                });
            }
        }
    }

    private static List<DateOnly> LeagueDates(CupRound round, int count, int seasonYear)
    {
        var dates = new List<DateOnly>();
        if (round.Dates.Count == 0)
        {
            var start = ParseEuropeDate("09-17", seasonYear);
            for (var i = 0; i < count; i++) dates.Add(CupLogic.ToWeekday(start.AddDays(i * 14), DayOfWeek.Wednesday));
            return dates;
        }
        for (var i = 0; i < count; i++)
            dates.Add(i < round.Dates.Count ? round.Dates[i] : round.Dates[^1].AddDays(7 * (i - round.Dates.Count + 1)));
        return dates;
    }

    /// <summary>Tabelle der Ligaphase (nur Spiele dieser Runde).</summary>
    public static List<TableRow> LeagueTableOf(Cup cup, CupRound round)
    {
        var fixtures = cup.Fixtures.Where(f => f.TieId is null && f.Matchday / 100 == round.Number && f.Result is not null).ToList();
        var ids = round.Entrants.Count > 0
            ? round.Entrants
            : fixtures.SelectMany(f => new[] { f.HomeId, f.AwayId }).Distinct().ToList();
        var rows = ids.ToDictionary(id => id, id => new TableRow { ClubId = id });
        foreach (var f in fixtures)
        {
            var r = f.Result!;
            rows[f.HomeId].Played++;
            rows[f.AwayId].Played++;
            rows[f.HomeId].GoalsFor += r.HomeGoals;
            rows[f.HomeId].GoalsAgainst += r.AwayGoals;
            rows[f.AwayId].GoalsFor += r.AwayGoals;
            rows[f.AwayId].GoalsAgainst += r.HomeGoals;
            if (r.HomeGoals > r.AwayGoals) { rows[f.HomeId].Won++; rows[f.HomeId].Points += 3; rows[f.AwayId].Lost++; }
            else if (r.HomeGoals < r.AwayGoals) { rows[f.AwayId].Won++; rows[f.AwayId].Points += 3; rows[f.HomeId].Lost++; }
            else { rows[f.HomeId].Drawn++; rows[f.AwayId].Drawn++; rows[f.HomeId].Points++; rows[f.AwayId].Points++; }
        }
        return rows.Values
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.GoalDifference)
            .ThenByDescending(r => r.GoalsFor)
            .ThenBy(r => r.ClubId)
            .Select((r, i) => { r.Position = i + 1; return r; })
            .ToList();
    }

    public static bool LeagueRoundComplete(Cup cup, CupRound round) =>
        cup.Fixtures.Any(f => f.TieId is null && f.Matchday / 100 == round.Number)
        && cup.Fixtures.Where(f => f.TieId is null && f.Matchday / 100 == round.Number).All(f => f.Result is not null);

    /// <summary>Ligaphase beendet: Top-N in die nächste K.-o.-Runde.</summary>
    public static EuropeAdvanceResult AdvanceAfterLeague(World world, GameData data, Cup cup, CupRound round, Rng rng)
    {
        var result = new EuropeAdvanceResult();
        if (!LeagueRoundComplete(cup, round)) return result;
        round.Ties.Clear();
        foreach (var id in round.Entrants)
            round.Ties.Add(new CupTie { Id = world.NextTieId++, HomeId = id, AwayId = id, Legs = 0, WinnerId = id, Decision = "League" });

        var table = LeagueTableOf(cup, round);
        var advance = data.Europe?.Competition(cup.Id)?.KnockoutFrom ?? 4;
        while (advance > table.Count && advance > 1) advance /= 2;
        if (advance < 2) advance = Math.Min(2, table.Count);
        var qualifiers = table.Take(advance).Select(r => r.ClubId).ToList();
        result.PrizeClubs.AddRange(qualifiers);
        result.PrizeAmount = round.Prize;
        foreach (var row in table)
            TouchEntry(world, cup.Id, row.ClubId, round.Number - 1, eliminated: row.Position > advance);

        if (round.Number >= cup.Rounds.Count)
        {
            cup.WinnerId = qualifiers[0];
            result.ChampionId = qualifiers[0];
            return result;
        }

        var next = cup.Rounds[round.Number];
        var pool = qualifiers.Concat(next.Entrants).Distinct().ToList();
        if (next.StageType == "league") ScheduleLeague(world, data, cup, next, pool, rng);
        else DrawKnockout(world, cup, next, pool, rng);
        result.NextDrawn = true;
        result.LeagueQualifiers.AddRange(qualifiers);
        if (cup.WinnerId is { } champ) result.ChampionId = champ;
        var flushed = FlushByeRounds(world, data, cup, rng);
        if (flushed.ChampionId is not null) result.ChampionId = flushed.ChampionId;
        result.PrizeClubs.AddRange(flushed.PrizeClubs);
        if (flushed.PrizeAmount > result.PrizeAmount) result.PrizeAmount = flushed.PrizeAmount;
        return result;
    }
}
