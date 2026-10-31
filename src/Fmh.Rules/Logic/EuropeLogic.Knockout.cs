using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public static partial class EuropeLogic
{
    public static void DrawKnockout(World world, Cup cup, CupRound round, List<int> teams, Rng rng)
    {
        var pool = teams.Distinct().ToList();
        // Ein Team allein: bei der letzten Runde ist es Sieger, sonst Freilos in die nächste Runde.
        if (pool.Count <= 1)
        {
            if (pool.Count == 1)
            {
                var sole = pool[0];
                round.Ties.Add(new CupTie { Id = world.NextTieId++, HomeId = sole, AwayId = sole, Legs = 0, WinnerId = sole, Decision = "Bye" });
                round.Teams = 1;
                if (round.Number == cup.Rounds.Count) cup.WinnerId = sole;
            }
            return;
        }
        // Ungerade Zahl: schwächstes Team (niedrigster Vereinskoeffizient) erhält Freilos.
        if (pool.Count % 2 == 1)
        {
            var bye = pool.OrderBy(id => world.ClubCoefficients.GetValueOrDefault(id, world.Clubs[id].Reputation))
                .ThenBy(id => id).First();
            pool.Remove(bye);
            round.Ties.Add(new CupTie { Id = world.NextTieId++, HomeId = bye, AwayId = bye, Legs = 0, WinnerId = bye, Decision = "Bye" });
        }
        round.Teams = pool.Count + round.Ties.Count(t => t.Decision == "Bye");
        // Gesetzt: stärkste Hälfte gegen schwächste; Rückspiel beim Stärkeren.
        var ranked = pool
            .OrderByDescending(id => world.ClubCoefficients.GetValueOrDefault(id, world.Clubs[id].Reputation))
            .ThenBy(id => id).ToList();
        var half = ranked.Count / 2;
        var strong = ranked.Take(half).ToList();
        var weak = ranked.Skip(half).ToList();
        rng.Shuffle(weak);
        for (var i = 0; i < strong.Count; i++)
            AddKnockoutTie(world, cup, round, weak[i], strong[i]);
    }

    /// <summary>Runden, die nur aus Freilosen bestehen, sofort weiterziehen bis ein echtes Spiel oder der Sieger feststeht.</summary>
    public static EuropeAdvanceResult FlushByeRounds(World world, GameData data, Cup cup, Rng rng)
    {
        var result = new EuropeAdvanceResult();
        while (cup.WinnerId is null)
        {
            var round = cup.Rounds.FirstOrDefault(r => r.Drawn && !r.Complete) ?? cup.Rounds.FirstOrDefault(r => r.Complete && r.Number < cup.Rounds.Count && !cup.Rounds[r.Number].Drawn);
            if (round is null) break;
            if (!round.Complete)
            {
                if (round.Ties.Count == 0 || round.Ties.Any(t => t.WinnerId is null)) break;
            }
            if (!round.Complete) break;
            var winners = round.Ties.Select(t => t.WinnerId!.Value).ToList();
            if (round.Number == cup.Rounds.Count)
            {
                cup.WinnerId = winners[0];
                result.ChampionId = winners[0];
                TouchEntry(world, cup.Id, winners[0], cup.Rounds.Count - 1, eliminated: true);
                break;
            }
            var next = cup.Rounds[round.Number];
            if (next.Drawn) break;
            var pool = winners.Concat(next.Entrants).Distinct().ToList();
            result.PrizeClubs.AddRange(winners);
            result.PrizeAmount = Math.Max(result.PrizeAmount, round.Prize);
            if (next.StageType == "league") ScheduleLeague(world, data, cup, next, pool, rng);
            else DrawKnockout(world, cup, next, pool, rng);
            result.NextDrawn = true;
            // Nur Freilos-Runden weiter spulen.
            if (!(next.Drawn && next.Complete && next.Ties.All(t => t.Decision == "Bye"))) break;
        }
        return result;
    }

    private static void AddKnockoutTie(World world, Cup cup, CupRound round, int homeFirstLeg, int awayFirstLeg)
    {
        var legs = Math.Max(1, round.Legs > 0 ? round.Legs : cup.Config.Legs);
        if (round.Number == cup.Rounds.Count) legs = cup.Config.FinalLegs ?? 1;
        var tie = new CupTie { Id = world.NextTieId++, HomeId = homeFirstLeg, AwayId = awayFirstLeg, Legs = legs };
        for (var leg = 1; leg <= legs; leg++)
        {
            var date = round.Dates.Count >= leg ? round.Dates[leg - 1] : round.Dates[0].AddDays(7 * (leg - 1));
            // Keine Kollision mit Ligaspielen: auf den nächsten freien Midweek-Tag schieben.
            date = AvoidLeagueClash(world, date, leg == 1 ? homeFirstLeg : awayFirstLeg, leg == 1 ? awayFirstLeg : homeFirstLeg);
            var f = new Fixture
            {
                Id = world.NextFixtureId++,
                CompetitionId = cup.Id,
                Kind = CompetitionKind.Europe,
                TieId = tie.Id,
                Leg = leg,
                Matchday = round.Number,
                Date = date,
                HomeId = leg == 1 ? homeFirstLeg : awayFirstLeg,
                AwayId = leg == 1 ? awayFirstLeg : homeFirstLeg,
                RefereeId = world.Referees.Count == 0 ? 0 : world.Referees[(tie.Id * 7 + leg) % world.Referees.Count].Id,
            };
            tie.FixtureIds.Add(f.Id);
            cup.Fixtures.Add(f);
        }
        round.Ties.Add(tie);
    }

    /// <summary>Verschiebt den Europapokaltermin, wenn einer der Vereine am selben Tag ein Ligaspiel hat.</summary>
    public static DateOnly AvoidLeagueClash(World world, DateOnly date, int homeId, int awayId)
    {
        for (var guard = 0; guard < 14; guard++)
        {
            var clash = world.Leagues.SelectMany(l => l.Fixtures)
                .Any(f => f.Date == date && f.Result is null && (f.Involves(homeId) || f.Involves(awayId)));
            if (!clash) return date;
            date = date.AddDays(1);
        }
        return date;
    }

    /// <summary>Nach entschiedenem K.-o.-Duell: Einträge und ggf. nächste Runde bzw. Abstieg in den tieferen Wettbewerb.</summary>
    public static EuropeAdvanceResult AdvanceAfterKnockout(World world, GameData data, Cup cup, CupRound round, CupTie tie, Rng rng)
    {
        var result = new EuropeAdvanceResult();
        var winner = tie.WinnerId!.Value;
        var loser = tie.HomeId == winner ? tie.AwayId : tie.HomeId;
        if (tie.Decision == "Bye") loser = -1;

        TouchEntry(world, cup.Id, winner, round.Number - 1, eliminated: false);
        if (loser >= 0)
        {
            TouchEntry(world, cup.Id, loser, round.Number - 1, eliminated: true);
            if (round.StageType == "knockout" && round.StageId is "q1" or "q2" or "q3" or "po"
                && data.Europe?.Competition(cup.Id) is { DropTo: { Length: > 0 } drop })
                result.Drops.Add((loser, drop));
        }

        if (!round.Complete) return result;
        result.PrizeClubs.AddRange(round.Ties.Where(t => t.WinnerId is not null).Select(t => t.WinnerId!.Value));
        result.PrizeAmount = round.Prize;

        if (round.Number == cup.Rounds.Count)
        {
            cup.WinnerId = winner;
            result.ChampionId = winner;
            TouchEntry(world, cup.Id, winner, cup.Rounds.Count - 1, eliminated: true);
            return result;
        }

        var winners = round.Ties.Select(t => t.WinnerId!.Value).ToList();
        var next = cup.Rounds[round.Number];
        var pool = winners.Concat(next.Entrants).Distinct().ToList();
        if (next.StageType == "league") ScheduleLeague(world, data, cup, next, pool, rng);
        else DrawKnockout(world, cup, next, pool, rng);
        result.NextDrawn = true;
        if (cup.WinnerId is { } champ) result.ChampionId = champ;
        var flushed = FlushByeRounds(world, data, cup, rng);
        if (flushed.ChampionId is not null) result.ChampionId = flushed.ChampionId;
        result.PrizeClubs.AddRange(flushed.PrizeClubs);
        if (flushed.PrizeAmount > result.PrizeAmount) result.PrizeAmount = flushed.PrizeAmount;
        return result;
    }

    internal static void TouchEntry(World world, string competitionId, int clubId, int stage, bool eliminated)
    {
        var e = world.EuropeanEntries.FirstOrDefault(x =>
            x.SeasonYear == world.SeasonYear && x.ClubId == clubId && x.CompetitionId == competitionId);
        if (e is null)
        {
            if (eliminated) return;
            world.EuropeanEntries.Add(new EuropeanEntry
            {
                ClubId = clubId, CompetitionId = competitionId, SeasonYear = world.SeasonYear, Stage = stage,
            });
            return;
        }
        e.Stage = stage;
        e.Eliminated = eliminated;
    }
}

/// <summary>Ergebnis nach einem abgeschlossenen Europapokal-Schritt oder -Duell.</summary>
public sealed class EuropeAdvanceResult
{
    public bool NextDrawn { get; set; }
    public int? ChampionId { get; set; }
    public long PrizeAmount { get; set; }
    public List<int> PrizeClubs { get; } = new();
    public List<int> LeagueQualifiers { get; } = new();
    public List<(int ClubId, string CompetitionId)> Drops { get; } = new();
}
