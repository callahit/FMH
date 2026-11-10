using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Bericht des Co-Trainers: Befunde (Textverweise) und ein angepasster Wochenplan.</summary>
public sealed class TrainingReport
{
    public List<TextRef> Findings { get; } = new();
    public TrainingPlan Suggested { get; init; } = new();
    /// <summary>Weicht der Vorschlag vom aktuellen Plan ab?</summary>
    public bool Changes { get; init; }
}

/// <summary>
/// Co-Trainer (AP 11): liest Kondition und Spielpraxis der Stammelf, Verletzte, Vertrautheit mit dem Spielsystem und
/// die Belastung der kommenden Woche und schlägt Änderungen am Wochenplan vor. Schwellen in <c>rules/assistant.json</c>.
/// </summary>
public static class TrainingAdvisor
{
    public static TrainingReport Advise(World world, GameData data, Club club, double familiarity)
    {
        var rules = data.Assistant;
        var eleven = club.Tactic.Lineup.Where(id => id is not null && world.Players.ContainsKey(id.Value)).Select(id => world.Players[id!.Value]).ToList();
        if (eleven.Count == 0) eleven = world.SquadOf(club).Where(p => !p.IsYouth).Take(11).ToList();
        var condition = eleven.Count == 0 ? 100 : eleven.Average(p => p.State.Condition);
        var practice = eleven.Count == 0 ? 100 : eleven.Average(p => p.State.MatchPractice);
        var injured = world.SquadOf(club).Count(p => p.State.Injury is not null);
        var nextWeek = world.AllFixtures.Count(f => f.Involves(club.Id) && f.Date > world.Date && f.Date <= world.Date.AddDays(7));

        var plan = club.Training.Clone();
        var findings = new List<TextRef>();
        var lighter = false;
        if (condition < rules.LowCondition)
        {
            findings.Add(TextRef.Of("assistant.lowCondition", Math.Round(condition)));
            lighter = true;
            Swap(plan, TrainingBlock.Fitness, TrainingBlock.Recovery);
        }
        if (injured >= rules.ManyInjured)
        {
            findings.Add(TextRef.Of("assistant.manyInjured", injured));
            lighter = true;
        }
        if (nextWeek >= 2)
        {
            findings.Add(TextRef.Of("assistant.busyWeek", nextWeek));
            lighter = true;
        }
        if (familiarity < rules.LowFamiliarity)
        {
            findings.Add(TextRef.Of("assistant.lowFamiliarity", club.Tactic.FormationId, Math.Round(familiarity)));
            if (!Swap(plan, TrainingBlock.Technique, TrainingBlock.Tactics)) Swap(plan, TrainingBlock.Finishing, TrainingBlock.Tactics);
        }
        if (practice < rules.LowMatchPractice && !plan.Sessions.Contains(TrainingBlock.PracticeMatch))
        {
            findings.Add(TextRef.Of("assistant.lowPractice", Math.Round(practice)));
            if (!Swap(plan, TrainingBlock.Technique, TrainingBlock.PracticeMatch)) Swap(plan, TrainingBlock.SetPieces, TrainingBlock.PracticeMatch);
        }
        else if (practice < rules.LowMatchPractice)
            findings.Add(TextRef.Of("assistant.lowPracticeFriendly", Math.Round(practice)));
        if (lighter && plan.Intensity != TrainingIntensity.Light) plan.Intensity = plan.Intensity == TrainingIntensity.Hard ? TrainingIntensity.Normal : TrainingIntensity.Light;
        if (findings.Count == 0) findings.Add(TextRef.Of("assistant.allGood", Math.Round(condition), Math.Round(familiarity)));

        var report = new TrainingReport
        {
            Suggested = plan,
            Changes = plan.Intensity != club.Training.Intensity || !plan.Sessions.SequenceEqual(club.Training.Sessions),
        };
        report.Findings.AddRange(findings);
        return report;
    }

    /// <summary>Ersetzt die erste Einheit <paramref name="from"/> der Woche durch <paramref name="to"/>.</summary>
    private static bool Swap(TrainingPlan plan, TrainingBlock from, TrainingBlock to)
    {
        var i = plan.Sessions.IndexOf(from);
        if (i < 0) return false;
        plan.Sessions[i] = to;
        return true;
    }
}
