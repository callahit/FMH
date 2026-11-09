using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

public enum DiffKind { Added, Removed, Changed }

/// <summary>Ein Unterschied zwischen Abhängigkeit und eigenem Paket.</summary>
public sealed record DiffEntry(string Path, DiffKind Kind, string? Old, string? New);

/// <summary>
/// Strukturvergleich zweier JSON-Inhalte für die Unterschiedsansicht. Listen aus Objekten mit Schlüssel
/// („key“, „id“ oder „code“) werden über diesen Schlüssel verglichen, nicht über die Position.
/// </summary>
public static class JsonDiff
{
    private static readonly string[] KeyFields = { "key", "id", "code" };

    public static List<DiffEntry> Compare(JsonNode? before, JsonNode? after)
    {
        var result = new List<DiffEntry>();
        Walk("", before, after, result);
        return result;
    }

    private static string Show(JsonNode? n) => n is null ? "null" : n is JsonValue ? n.ToJsonString() : n is JsonArray a ? $"[{a.Count} Einträge]" : "{…}";

    private static string Join(string path, string segment) => path.Length == 0 ? segment : segment.StartsWith('[') ? path + segment : path + "." + segment;

    private static void Walk(string path, JsonNode? a, JsonNode? b, List<DiffEntry> result)
    {
        if (CanonicalJson.Same(a, b)) return;
        switch (a, b)
        {
            case (null, not null):
                result.Add(new DiffEntry(path, DiffKind.Added, null, Show(b)));
                return;
            case (not null, null):
                result.Add(new DiffEntry(path, DiffKind.Removed, Show(a), null));
                return;
            case (JsonObject oa, JsonObject ob):
                foreach (var key in oa.Select(kv => kv.Key).Union(ob.Select(kv => kv.Key)).OrderBy(k => k, StringComparer.Ordinal))
                {
                    var inA = oa.TryGetPropertyValue(key, out var va);
                    var inB = ob.TryGetPropertyValue(key, out var vb);
                    var p = Join(path, key);
                    if (inA && !inB) result.Add(new DiffEntry(p, DiffKind.Removed, Show(va), null));
                    else if (!inA && inB) result.Add(new DiffEntry(p, DiffKind.Added, null, Show(vb)));
                    else Walk(p, va, vb, result);
                }
                return;
            case (JsonArray la, JsonArray lb):
                if (KeyOf(la) is { } field && KeyOf(lb) == field)
                {
                    var byA = la.OfType<JsonObject>().ToDictionary(o => o[field]!.ToString());
                    var byB = lb.OfType<JsonObject>().ToDictionary(o => o[field]!.ToString());
                    foreach (var k in byA.Keys.Union(byB.Keys).OrderBy(k => k, StringComparer.Ordinal))
                    {
                        var p = Join(path, $"[{field}={k}]");
                        if (!byB.ContainsKey(k)) result.Add(new DiffEntry(p, DiffKind.Removed, k, null));
                        else if (!byA.ContainsKey(k)) result.Add(new DiffEntry(p, DiffKind.Added, null, k));
                        else Walk(p, byA[k], byB[k], result);
                    }
                    return;
                }
                for (var i = 0; i < Math.Max(la.Count, lb.Count); i++)
                {
                    var p = Join(path, $"[{i}]");
                    if (i >= lb.Count) result.Add(new DiffEntry(p, DiffKind.Removed, Show(la[i]), null));
                    else if (i >= la.Count) result.Add(new DiffEntry(p, DiffKind.Added, null, Show(lb[i])));
                    else Walk(p, la[i], lb[i], result);
                }
                return;
            default:
                result.Add(new DiffEntry(path, DiffKind.Changed, Show(a), Show(b)));
                return;
        }
    }

    /// <summary>Schlüsselfeld einer Liste, wenn alle Einträge Objekte mit eindeutigem Wert darin sind.</summary>
    private static string? KeyOf(JsonArray list)
    {
        if (list.Count == 0 || list.Any(n => n is not JsonObject)) return null;
        foreach (var field in KeyFields)
        {
            var values = list.Select(n => (n as JsonObject)![field]?.ToString()).ToList();
            if (values.All(v => !string.IsNullOrEmpty(v)) && values.Distinct().Count() == values.Count) return field;
        }
        return null;
    }
}
