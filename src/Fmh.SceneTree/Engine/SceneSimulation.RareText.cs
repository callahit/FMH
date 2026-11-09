using Fmh.Core.Match;

namespace Fmh.SceneTree;

public sealed partial class SceneSimulation
{
    /// <summary>
    /// Sonstiger Text inklusive Fortsetzung. Geburtstagszeilen hinter Megaphon und Banner nur mit Easter Eggs.
    /// Ohne Fortsetzung wird ein hängender Doppelpunkt zu einem vollständigen Satz.
    /// </summary>
    private void EmitRare(PlannedIncident item)
    {
        if (item.RareTag is { } tag) _rareTags.Add(tag);
        if (item.Node < 0) return;
        EmitRareNode(item.Node, root: true);
    }

    private void EmitRareNode(int index, bool root)
    {
        var node = _tree.Nodes[index];
        if (!root && !_opt.EasterEggs && node.SpecialId is 2 or 4) return;
        var (att, def) = Books();
        var text = Narrative(node, att, def);
        var children = new List<int>();
        foreach (var child in node.Children)
        {
            var next = _tree.Nodes[child];
            var blocked = !_opt.EasterEggs && (node.SpecialId is 2 or 4 || next.SpecialId is 2 or 4);
            if (!blocked) children.Add(child);
        }
        if (children.Count == 0 && text is not null)
            text = CompleteDangling(text);
        if (!string.IsNullOrWhiteSpace(text))
            Emit(MatchEventType.Attack, _attack, Actor(node, att, def), narrative: text);
        foreach (var child in children)
            EmitRareNode(child, root: false);
    }

    private static string CompleteDangling(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.EndsWith(" und singt:", StringComparison.Ordinal))
            return trimmed[..^" und singt:".Length] + ".";
        if (trimmed.EndsWith(':'))
            return trimmed.TrimEnd(':').TrimEnd() + ".";
        return text;
    }
}
