using System.Globalization;
using System.Text.Json.Nodes;
using Fmh.Core.Data.Editing;

namespace Fmh.Core.Data.Schema;

/// <summary>
/// Schema-Hilfen für den Editor: Teilschema zu einem Pfad, Beschreibung, bekannte Felder und Werte im richtigen Typ.
/// </summary>
public static class SchemaNavigator
{
    /// <summary>Teilschema an <paramref name="path"/> (Verweise aufgelöst); null, wenn das Schema den Pfad nicht kennt.</summary>
    public static JsonObject? At(JsonObject root, JsonPath path)
    {
        JsonObject? schema = Deref(root, root);
        foreach (var segment in path.Segments)
        {
            if (schema is null) return null;
            JsonNode? next = segment switch
            {
                int => schema["items"],
                string key => (schema["properties"] as JsonObject)?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value
                              ?? schema["additionalProperties"] as JsonObject,
                _ => null,
            };
            schema = Deref(root, next as JsonObject);
        }
        return schema;
    }

    public static string? Description(JsonObject root, JsonPath path) => At(root, path)?["description"]?.GetValue<string>();

    /// <summary>Felder, die ein Objekt laut Schema haben kann (Vorschläge für „+ Feld“).</summary>
    public static IReadOnlyList<string> Properties(JsonObject? schema) =>
        (schema?["properties"] as JsonObject)?.Select(p => p.Key).Order(StringComparer.Ordinal).ToList() ?? new List<string>();

    /// <summary>Erlaubte Werte eines Enum-Feldes (leer, wenn das Feld frei ist).</summary>
    public static IReadOnlyList<string> EnumValues(JsonObject? schema) =>
        (schema?["enum"] as JsonArray)?.OfType<JsonValue>().Select(v => v.ToString()).ToList() ?? new List<string>();

    /// <summary>
    /// Text in den Typ des Feldes umwandeln: ganze Zahl, Zahl, Wahrheitswert, Enum (Schreibweise wie im Schema) oder Text.
    /// Leerer Text ergibt null (Feld entfernen). false bei ungültiger Eingabe.
    /// </summary>
    public static bool TryParse(JsonObject? schema, string text, out JsonNode? value)
    {
        value = null;
        text = text.Trim();
        if (text.Length == 0) return true;
        var types = schema?["type"] switch
        {
            JsonArray a => a.Select(t => t!.GetValue<string>()).ToList(),
            JsonValue v => new List<string> { v.GetValue<string>() },
            _ => new List<string>(),
        };
        var enums = EnumValues(schema);
        if (enums.Count > 0)
        {
            var match = enums.FirstOrDefault(e => string.Equals(e, text, StringComparison.OrdinalIgnoreCase));
            value = match is null ? null : JsonValue.Create(match);
            return match is not null;
        }
        if (types.Contains("integer"))
        {
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return false;
            value = JsonValue.Create(n);
            return true;
        }
        if (types.Contains("number"))
        {
            if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
            value = d == Math.Floor(d) && !text.Contains('.') && !text.Contains(',') ? JsonValue.Create((long)d) : JsonValue.Create(d);
            return true;
        }
        if (types.Contains("boolean"))
        {
            if (!bool.TryParse(text, out var b)) return false;
            value = JsonValue.Create(b);
            return true;
        }
        if (types.Contains("array") && text == "[]") { value = new JsonArray(); return true; }
        if (types.Contains("object") && text == "{}") { value = new JsonObject(); return true; }
        value = JsonValue.Create(text);
        return true;
    }

    private static JsonObject? Deref(JsonObject root, JsonObject? schema)
    {
        for (var guard = 0; schema is not null && guard < 32; guard++)
        {
            if (schema["$ref"]?.GetValue<string>() is { } reference) schema = Pointer(root, reference);
            else if (schema["anyOf"] is JsonArray any) schema = any.OfType<JsonObject>().FirstOrDefault(o => o["type"]?.ToString() != "null");
            else return schema;
        }
        return schema;
    }

    private static JsonObject? Pointer(JsonObject root, string reference)
    {
        JsonNode? node = root;
        foreach (var part in reference.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
            node = node is JsonObject o ? o[part.Replace("~1", "/").Replace("~0", "~")] : node is JsonArray a && int.TryParse(part, out var i) && i < a.Count ? a[i] : null;
        return node as JsonObject;
    }
}
