using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Taktikwahl der KI-Vereine: Formation passend zum Kader, Mentalität passend zum Gegner.</summary>
public static class AiTactics
{
    public static Formation BestFormation(Club club, World world, GameData data) =>
        BestFormation(club, world, data, new SlotScores(data.PositionProfiles), null);

    private static Formation BestFormation(Club club, World world, GameData data, SlotScores scores, Func<Player, bool>? eligible)
    {
        var available = world.SquadOf(club).Where(p => p.IsAvailable && (eligible is null || eligible(p))).ToList();
        Formation? best = null;
        var bestScore = double.MinValue;
        foreach (var f in data.Formations)
        {
            var lineup = LineupSelector.Assign(f, available, data.PositionProfiles, scores);
            var score = 0.0;
            for (var i = 0; i < f.Slots.Count; i++)
            {
                if (lineup[i] is not { } id) { score -= 20; continue; }
                score += scores.Get(world.Players[id], f.Slots[i].Position);
            }
            if (score > bestScore) { bestScore = score; best = f; }
        }
        return best ?? data.Formations[0];
    }

    /// <summary>Formation, Aufstellung und Grundanweisungen für einen KI-Verein vor einem Spiel.</summary>
    public static void Prepare(Club club, Club opponent, bool home, World world, GameData data, int benchSize)
    {
        var scores = new SlotScores(data.PositionProfiles);
        var eligible = MatchEligibility(club, world, data);
        var formation = BestFormation(club, world, data, scores, eligible);
        club.Tactic.FormationId = formation.Id;
        LineupSelector.PickBest(club, world, data, benchSize, scores, eligible);

        var own = TeamStrength(club, world, data);
        var opp = TeamStrength(opponent, world, data);
        var diff = own - opp + (home ? 0.6 : -0.4);
        club.Tactic.Mentality = (Mentality)Math.Clamp((int)Math.Round(diff * 0.7), -2, 2);

        var ins = club.Tactic.Instructions;
        var eleven = club.Tactic.Lineup.Where(id => id.HasValue).Select(id => world.Players[id!.Value]).ToList();
        if (eleven.Count == 0) return;
        double Avg(Attr a) => eleven.Average(p => p.Attributes[a]);
        ins.Counter = diff < -0.5 && Avg(Attr.Pace) >= 11;
        ins.PassingLength = Avg(Attr.Passing) >= 12.5 ? PassingLength.Short : Avg(Attr.Passing) < 9.5 ? PassingLength.Long : PassingLength.Mixed;
        ins.Pressing = Avg(Attr.WorkRate) + Avg(Attr.Stamina) >= 25 ? PressingHeight.High : diff < -1 ? PressingHeight.Low : PressingHeight.Mid;
        ins.Line = ins.Pressing == PressingHeight.High ? DefensiveLine.High : ins.Pressing == PressingHeight.Low ? DefensiveLine.Deep : DefensiveLine.Normal;
        ins.Tempo = diff > 1 ? Tempo.High : Tempo.Normal;
    }

    /// <summary>
    /// Spielrecht zweiter Mannschaften: Spieler bis zur Altersgrenze (Alter am Saisonstart) und die besten
    /// Älteren bis zur erlaubten Zahl. Null für erste Mannschaften.
    /// </summary>
    public static Func<Player, bool>? ReserveEligibility(Club club, World world, GameData data)
    {
        if (club.ReserveOf is null) return null;
        var country = world.Country(club.CountryCode);
        var rules = country.Reserves;
        var start = new DateOnly(world.SeasonYear, country.Season.StartMonth, 1);
        var older = world.SquadOf(club).Where(p => p.IsAvailable && p.Age(start) > rules.AgeLimit)
            .OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, data.PositionProfiles)).ThenBy(p => p.Id)
            .Take(rules.OverAgePlayers).Select(p => p.Id).ToHashSet();
        return p => p.Age(start) <= rules.AgeLimit || older.Contains(p.Id);
    }

    /// <summary>Spielberechtigung im Pflichtspiel: Meldeliste (oder Ausnahme) und bei zweiten Mannschaften das Spielrecht.</summary>
    public static Func<Player, bool> MatchEligibility(Club club, World world, GameData data)
    {
        var reserve = ReserveEligibility(club, world, data);
        return p => Registration.IsEligible(p, world) && (reserve is null || reserve(p));
    }

    /// <summary>
    /// Mindestzahl junger Spieler im Spieltagskader (z. B. vier U23 in der 3. Liga): Fehlende ersetzen die schwächsten älteren
    /// Bankspieler, soweit junge, spielberechtigte Spieler verfügbar sind.
    /// </summary>
    public static void EnsureYoungPlayers(Club club, World world, GameData data)
    {
        if (Registration.RulesFor(world, club) is not { MatchdayMinYoung: > 0 } r) return;
        bool Young(Player p) => Registration.SeasonAge(p, world) <= r.MatchdayYoungAge;
        var eligible = MatchEligibility(club, world, data);
        var inSquad = club.Tactic.Lineup.OfType<int>().Concat(club.Tactic.Bench).ToHashSet();
        var missing = r.MatchdayMinYoung - inSquad.Count(id => Young(world.Players[id]));
        if (missing <= 0) return;
        double Rating(Player p) => Ratings.PositionRating(p, p.MainPosition, data.PositionProfiles);
        var extra = world.SquadOf(club).Where(p => p.IsAvailable && eligible(p) && Young(p) && !inSquad.Contains(p.Id))
            .OrderByDescending(Rating).ThenBy(p => p.Id).Take(missing).ToList();
        var outgoing = club.Tactic.Bench.Select(id => world.Players[id]).Where(p => !Young(p) && p.MainPosition != Position.GK)
            .OrderBy(Rating).ThenBy(p => p.Id).Take(extra.Count).ToList();
        for (var i = 0; i < Math.Min(extra.Count, outgoing.Count); i++)
            club.Tactic.Bench[club.Tactic.Bench.IndexOf(outgoing[i].Id)] = extra[i].Id;
    }

    public static double TeamStrength(Club club, World world, GameData data)
    {
        var top = world.SquadOf(club)
            .Where(p => p.IsAvailable)
            .Select(p => Ratings.PositionRating(p, p.MainPosition, data.PositionProfiles))
            .OrderByDescending(r => r)
            .Take(14)
            .ToList();
        return top.Count == 0 ? 0 : top.Average();
    }
}
