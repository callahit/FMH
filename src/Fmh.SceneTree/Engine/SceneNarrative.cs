using System.Text.RegularExpressions;
using Fmh.Core.Match;

namespace Fmh.SceneTree;

public sealed class SceneNarrator
{
    public required Func<bool, int, int, MatchPlayer?> Find { get; init; }
    public string Referee { get; init; } = "der Schiedsrichter";
    public string Offender { get; init; } = "der Spieler";
    public string Victim { get; init; } = "der Spieler";
    public string Reason { get; init; } = "Foul";
    public string Injury { get; init; } = "Verletzung";
}

/// <summary>Ersetzt 1-ST, 2-GK1, REF, OFFENDER, VICTIM, REASON und INJURY. Varianten zieht die Engine gleichverteilt.</summary>
public static partial class SceneNarrative
{
    private static readonly Regex Roles = RolePattern();
    private static readonly Regex Words = WordPattern();

    [GeneratedRegex(@"\b([12])-([A-Z]{2,4})([0-3]?)\b")]
    private static partial Regex RolePattern();

    [GeneratedRegex(@"\b(REF|OFFENDER|VICTIM|REASON|INJURY)\b")]
    private static partial Regex WordPattern();

    public static string Apply(string text, SceneNarrator narrator)
    {
        if (text.Length == 0) return text;
        text = Roles.Replace(text, match =>
        {
            var code = Code(match.Groups[2].Value);
            if (code == 0) return match.Value;
            var index = match.Groups[3].Length == 0 ? 0 : match.Groups[3].Value[0] - '0';
            var player = narrator.Find(match.Groups[1].Value == "1", code, index);
            return player?.Player.LastName ?? RoleBook.FallbackName(code);
        });
        return Words.Replace(text, match => match.Value switch
        {
            "REF" => narrator.Referee,
            "OFFENDER" => narrator.Offender,
            "VICTIM" => narrator.Victim,
            "REASON" => narrator.Reason,
            "INJURY" => narrator.Injury,
            _ => match.Value,
        });
    }

    public static int Code(string name) => name switch
    {
        "GK" => 1, "RB" => 2, "LB" => 3, "CD" => 4, "SW" => 5, "RWB" => 6, "LWB" => 7, "ANC" => 8,
        "DM" => 9, "RM" => 10, "LM" => 11, "CM" => 12, "RW" => 13, "LW" => 14, "AM" => 15, "CF" => 16,
        "ST" => 17, "PEN" => 18, "FKC" => 19, "FKR" => 20, "FKL" => 21, "CKR" => 22, "CKL" => 23,
        _ => 0,
    };
}
