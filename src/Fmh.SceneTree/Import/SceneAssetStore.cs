using System.Text.Encodings.Web;
using System.Text.Json;

namespace Fmh.SceneTree;

/// <summary>
/// Eigenes Asset (Format 1): JSON mit Zeilen und Knoten als <c>[id, kinder, parameter-base64, zeilennummern]</c>.
/// Die Bedeutung der Bytes steht in <see cref="SceneNode"/>, nicht noch einmal im JSON.
/// </summary>
public static class SceneAssetStore
{
    public static void Write(SceneTreeAsset asset, string path)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        writer.WriteStartObject();
        writer.WriteNumber("version", SceneTreeAsset.FormatVersion);
        writer.WriteString("magic", asset.Magic);
        writer.WriteNumber("headerVersion", asset.HeaderVersion);
        writer.WriteNumber("headerFlags", asset.HeaderFlags);
        writer.WriteNumber("nodes", asset.Nodes.Count);
        writer.WriteNumber("categories", asset.Categories.Count);
        writer.WritePropertyName("lines");
        writer.WriteStartArray();
        for (var i = 1; i < asset.Lines.Length; i++) writer.WriteStringValue(asset.Lines[i] ?? "");
        writer.WriteEndArray();
        writer.WritePropertyName("tree");
        writer.WriteStartArray();
        foreach (var node in asset.Nodes)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(node.Id);
            writer.WriteNumberValue(node.ChildCount);
            writer.WriteStringValue(Convert.ToBase64String(node.Parameters));
            writer.WriteStartArray();
            foreach (var line in node.LineRefs) writer.WriteNumberValue(line);
            writer.WriteEndArray();
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    public static SceneTreeAsset Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var version = root.GetProperty("version").GetInt32();
        if (version != SceneTreeAsset.FormatVersion)
            throw new InvalidDataException($"Szenenbaum-Asset Version {version} wird nicht gelesen.");
        var lineArray = root.GetProperty("lines");
        var lines = new string[lineArray.GetArrayLength() + 1];
        var n = 1;
        foreach (var line in lineArray.EnumerateArray()) lines[n++] = line.GetString() ?? "";
        var nodes = new List<SceneNode>();
        foreach (var item in root.GetProperty("tree").EnumerateArray())
        {
            var id = item[0].GetInt32();
            var children = item[1].GetInt32();
            var parameters = Convert.FromBase64String(item[2].GetString() ?? "");
            var refs = item[3].EnumerateArray().Select(x => x.GetInt32()).ToArray();
            nodes.Add(new SceneNode(nodes.Count, id, refs, parameters, children));
        }
        return SceneTreeAsset.Build(
            root.GetProperty("magic").GetString() ?? "",
            root.GetProperty("headerVersion").GetUInt32(),
            root.GetProperty("headerFlags").GetUInt32(),
            lines, nodes);
    }
}
