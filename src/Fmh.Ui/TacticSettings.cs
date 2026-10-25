using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Ui;

/// <summary>Eine verstellbare Taktikeinstellung (mit ← → änderbar). Wird auf der Taktikseite und im Spiel genutzt.</summary>
public sealed record TacticSetting(string LabelKey, Func<Tactic, string> Value, Action<Tactic, int> Change, bool FormationChange = false);

public static class TacticSettings
{
    private static T Cycle<T>(T value, int delta) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var i = Array.IndexOf(values, value);
        return values[(i + values.Length + delta) % values.Length];
    }

    private static T Step<T>(T value, int delta) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var i = Array.IndexOf(values, value);
        return values[Math.Clamp(i + delta, 0, values.Length - 1)];
    }

    public static List<TacticSetting> Rows(GameData data)
    {
        var t = data.Texts;
        string YesNo(bool b) => b ? t["ui.yes"] : t["ui.no"];
        return new List<TacticSetting>
        {
            new("tactic.formation", x => data.Formation(x.FormationId).Name, (x, d) =>
            {
                var i = data.Formations.FindIndex(f => f.Id == x.FormationId);
                x.FormationId = data.Formations[(i + data.Formations.Count + d) % data.Formations.Count].Id;
            }, FormationChange: true),
            new("tactic.mentality", x => t["mentality." + x.Mentality], (x, d) => x.Mentality = Step(x.Mentality, d)),
            new("tactic.passing", x => t["passing." + x.Instructions.PassingLength], (x, d) => x.Instructions.PassingLength = Step(x.Instructions.PassingLength, d)),
            new("tactic.tempo", x => t["tempo." + x.Instructions.Tempo], (x, d) => x.Instructions.Tempo = Step(x.Instructions.Tempo, d)),
            new("tactic.width", x => t["width." + x.Instructions.Width], (x, d) => x.Instructions.Width = Step(x.Instructions.Width, d)),
            new("tactic.focus", x => t["focus." + x.Instructions.Focus], (x, d) => x.Instructions.Focus = Cycle(x.Instructions.Focus, d)),
            new("tactic.counter", x => YesNo(x.Instructions.Counter), (x, _) => x.Instructions.Counter = !x.Instructions.Counter),
            new("tactic.pressing", x => t["pressing." + x.Instructions.Pressing], (x, d) => x.Instructions.Pressing = Step(x.Instructions.Pressing, d)),
            new("tactic.line", x => t["line." + x.Instructions.Line], (x, d) => x.Instructions.Line = Step(x.Instructions.Line, d)),
            new("tactic.offsideTrap", x => YesNo(x.Instructions.OffsideTrap), (x, _) => x.Instructions.OffsideTrap = !x.Instructions.OffsideTrap),
            new("tactic.tackling", x => t["tackling." + x.Instructions.Tackling], (x, d) => x.Instructions.Tackling = Step(x.Instructions.Tackling, d)),
            new("tactic.gegenpressing", x => YesNo(x.Instructions.Gegenpressing), (x, _) => x.Instructions.Gegenpressing = !x.Instructions.Gegenpressing),
            new("tactic.distribution", x => t["distribution." + x.Instructions.GoalkeeperDistribution], (x, d) => x.Instructions.GoalkeeperDistribution = Cycle(x.Instructions.GoalkeeperDistribution, d)),
        };
    }
}
