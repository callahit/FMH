using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Schema;

/// <summary>Abweichung vom Schema an einer Stelle (Pfad wie im Editor, z. B. „levels[0].divisions[1].teams“).</summary>
public sealed record SchemaError(string Path, string Message)
{
    public override string ToString() => Path.Length == 0 ? Message : $"{Path}: {Message}";
}

/// <summary>
/// Prüft JSON gegen die Schemas aus <see cref="SchemaCatalog"/>. Unterstützt den Teil von JSON Schema 2020-12, den der
/// Exporter erzeugt: type, enum, properties, additionalProperties, propertyNames, items, required, minimum, maximum,
/// format „date“, anyOf und Verweise ($ref) innerhalb der Datei. Feldnamen und Enum-Werte vergleicht der Prüfer wie der
/// Paketlader ohne Groß- und Kleinschreibung.
/// </summary>
public static class SchemaValidator
{
    public static readonly JsonDocumentOptions ParseOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static List<SchemaError> Validate(JsonNode? instance, JsonObject schema)
    {
        var errors = new List<SchemaError>();
        Check(instance, schema, schema, "", errors);
        return errors;
    }

    private static void Check(JsonNode? node, JsonObject schema, JsonObject root, string path, List<SchemaError> errors)
    {
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            if (Resolve(root, reference) is { } target) Check(node, target, root, path, errors);
            return;
        }
        if (schema["anyOf"] is JsonArray any)
        {
            var options = any.OfType<JsonObject>().ToList();
            if (options.Count > 0 && !options.Any(o => { var tmp = new List<SchemaError>(); Check(node, o, root, path, tmp); return tmp.Count == 0; }))
                errors.Add(new SchemaError(path, "passt zu keiner erlaubten Form."));
            return;
        }

        var kind = Kind(node);
        if (schema["type"] is { } typeNode)
        {
            var allowed = typeNode is JsonArray arr ? arr.Select(t => t!.GetValue<string>()).ToList() : new List<string> { typeNode.GetValue<string>() };
            var fits = allowed.Contains(kind) || (kind == "integer" && allowed.Contains("number"));
            if (!fits)
            {
                errors.Add(new SchemaError(path, $"erwartet {string.Join(" oder ", allowed.Select(Word))}, gefunden {Word(kind)}."));
                return;
            }
        }
        if (schema["enum"] is JsonArray values && !EnumContains(values, node))
        {
            var list = string.Join(", ", values.Take(12).Select(v => v?.ToString() ?? "null")) + (values.Count > 12 ? ", …" : "");
            errors.Add(new SchemaError(path, $"„{node}“ ist nicht erlaubt (erlaubt: {list})."));
        }

        switch (node)
        {
            case JsonObject obj:
                CheckObject(obj, schema, root, path, errors);
                break;
            case JsonArray array when schema["items"] is JsonObject items:
                for (var i = 0; i < array.Count; i++) Check(array[i], items, root, $"{path}[{i}]", errors);
                break;
            case JsonValue value:
                CheckValue(value, schema, path, errors);
                break;
        }
    }

    private static void CheckObject(JsonObject obj, JsonObject schema, JsonObject root, string path, List<SchemaError> errors)
    {
        var properties = schema["properties"] as JsonObject;
        foreach (var required in (schema["required"] as JsonArray ?? new JsonArray()).Select(r => r!.GetValue<string>()))
            if (!obj.Any(kv => string.Equals(kv.Key, required, StringComparison.OrdinalIgnoreCase)))
                errors.Add(new SchemaError(path, $"Pflichtfeld „{required}“ fehlt."));
        foreach (var (key, value) in obj)
        {
            var childPath = path.Length == 0 ? key : $"{path}.{key}";
            if (schema["propertyNames"]?["enum"] is JsonArray names && !EnumContains(names, JsonValue.Create(key)))
                errors.Add(new SchemaError(childPath, $"„{key}“ ist als Schlüssel nicht erlaubt."));
            var declared = properties?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
            if (declared is not null) Check(value, declared, root, childPath, errors);
            else if (schema["additionalProperties"] is JsonObject extra) Check(value, extra, root, childPath, errors);
            else if (schema["additionalProperties"] is JsonValue v && v.GetValue<bool>() == false)
                errors.Add(new SchemaError(childPath, $"unbekanntes Feld „{key}“."));
        }
    }

    private static void CheckValue(JsonValue value, JsonObject schema, string path, List<SchemaError> errors)
    {
        if (value.TryGetValue<double>(out var number) || (value.GetValueKind() == JsonValueKind.Number && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)))
        {
            if (schema["minimum"] is JsonValue min && number < min.GetValue<double>())
                errors.Add(new SchemaError(path, $"{Format(number)} ist kleiner als {Format(min.GetValue<double>())}."));
            if (schema["maximum"] is JsonValue max && number > max.GetValue<double>())
                errors.Add(new SchemaError(path, $"{Format(number)} ist größer als {Format(max.GetValue<double>())}."));
        }
        if (schema["format"]?.GetValue<string>() == "date" && value.GetValueKind() == JsonValueKind.String
            && !DateOnly.TryParseExact(value.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add(new SchemaError(path, $"„{value}“ ist kein Datum im Format JJJJ-MM-TT."));
    }

    /// <summary>Enum-Werte ohne Groß- und Kleinschreibung; Zahlen für Enums wie beim JsonStringEnumConverter.</summary>
    private static bool EnumContains(JsonArray values, JsonNode? node)
    {
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            return values.Any(x => x is JsonValue xv && xv.GetValueKind() == JsonValueKind.String
                                   && string.Equals(xv.GetValue<string>(), v.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
        if (node is JsonValue n && n.GetValueKind() == JsonValueKind.Number && values.All(x => x?.GetValueKind() is JsonValueKind.String or JsonValueKind.Null))
            return true;
        return values.Any(x => JsonNode.DeepEquals(x, node));
    }

    private static string Kind(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && !v.ToJsonString().Contains('.') ? "integer" : "number",
            _ => "null",
        },
        _ => "null",
    };

    private static string Word(string kind) => kind switch
    {
        "object" => "Objekt",
        "array" => "Liste",
        "string" => "Text",
        "integer" => "ganze Zahl",
        "number" => "Zahl",
        "boolean" => "true/false",
        _ => "null",
    };

    private static string Format(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>JSON-Pointer innerhalb der Datei („#“, „#/items/properties/x“).</summary>
    private static JsonObject? Resolve(JsonObject root, string reference)
    {
        if (!reference.StartsWith('#')) return null;
        JsonNode? node = root;
        foreach (var raw in reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Replace("~1", "/").Replace("~0", "~");
            node = node switch
            {
                JsonObject o => o[part],
                JsonArray a when int.TryParse(part, out var i) && i < a.Count => a[i],
                _ => null,
            };
        }
        return node as JsonObject;
    }
}
