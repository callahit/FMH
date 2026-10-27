using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Taktikwahl der KI-Vereine: Formation passend zum Kader, Mentalität passend zum Gegner.</summary>
public static class AiTactics
{
    public static Formation BestFormation(Club club, World world, GameData data) =>
        BestFormation(club, world, data, new SlotScores(data.PositionProfiles));

    private static Formation BestFormation(Club club, World world, GameData data, SlotScores scores)
    {
        var available = world.SquadOf(club).Where(p => p.IsAvailable).ToList();
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
        var formation = BestFormation(club, world, data, scores);
        club.Tactic.FormationId = formation.Id;
        LineupSelector.PickBest(club, world, data, benchSize, scores);

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
