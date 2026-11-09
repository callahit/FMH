using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

/// <summary>
/// Feste Schreibweise für Pakete aus dem Editor (Umsetzungskonzept 7.4): Schlüssel ordinal sortiert, zwei Leerzeichen
/// Einrückung, Zeilenende LF, UTF-8 ohne BOM, abschließender Zeilenumbruch, Umlaute unmaskiert. Gleicher Inhalt
/// ergibt damit byte-identische Dateien, und Git-Diffs bleiben lesbar.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static JsonNode? Parse(string text) => JsonNode.Parse(text, documentOptions: ReadOptions);

    public static JsonNode? Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Kanonischer Text eines JSON-Knotens.</summary>
    public static string Serialize(JsonNode? node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            Write(writer, node);
        var text = Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n");
        return text + "\n";
    }

    /// <summary>Schreibt die Datei nur, wenn sich der Inhalt geändert hat (Zeitstempel bleiben sonst unberührt).</summary>
    public static bool Save(string path, JsonNode? node)
    {
        var text = Serialize(node);
        if (File.Exists(path) && File.ReadAllText(path) == text) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static void Write(Utf8JsonWriter w, JsonNode? node)
    {
        switch (node)
        {
            case null:
                w.WriteNullValue();
                break;
            case JsonObject obj:
                w.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    w.WritePropertyName(key);
                    Write(w, value);
                }
                w.WriteEndObject();
                break;
            case JsonArray arr:
                w.WriteStartArray();
                foreach (var item in arr) Write(w, item);
                w.WriteEndArray();
                break;
            default:
                node.WriteTo(w);
                break;
        }
    }

    /// <summary>Inhaltlicher Vergleich ohne Rücksicht auf Schlüsselreihenfolge und Formatierung.</summary>
    public static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
}
