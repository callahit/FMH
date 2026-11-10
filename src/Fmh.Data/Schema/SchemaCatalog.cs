using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Fmh.Core.Model;

namespace Fmh.Core.Data.Schema;

/// <summary>Zuordnung einer Datendatei (Pfad relativ zum Paket, z. B. „countries/*.json“) zu ihrem Typ.</summary>
public sealed record SchemaEntry(string Name, Type Type, string Pattern)
{
    public string FileName => $"{Name}.schema.json";

    /// <summary>Passt der relative Pfad (mit „/“) auf das Muster? „*“ steht für einen Dateinamen ohne Ordner.</summary>
    public bool Matches(string relativePath)
    {
        var star = Pattern.IndexOf('*');
        if (star < 0) return relativePath == Pattern;
        var (prefix, suffix) = (Pattern[..star], Pattern[(star + 1)..]);
        return relativePath.Length > prefix.Length + suffix.Length && relativePath.StartsWith(prefix, StringComparison.Ordinal)
               && relativePath.EndsWith(suffix, StringComparison.Ordinal)
               && !relativePath[prefix.Length..^suffix.Length].Contains('/')
               // Kommentarbausteine haben ein eigenes Schema
               && !(Name == "lang" && relativePath.EndsWith(".commentary.json", StringComparison.Ordinal));
    }
}

/// <summary>
/// JSON-Schemas der Datenpakete (Umsetzungskonzept 7.3), erzeugt aus den Datentypen, die der Paketlader liest. So
/// können Schema und Spiel nicht auseinanderlaufen. Die Dateien unter <c>data/schema/</c> geben Texteditoren
/// Autovervollständigung; Validator und Editor prüfen gegen dieselben Schemas.
/// </summary>
public static class SchemaCatalog
{
    public const string Folder = "schema";
    public const string Draft = "https://json-schema.org/draft/2020-12/schema";

    public static readonly IReadOnlyList<SchemaEntry> Entries = new SchemaEntry[]
    {
        new("package", typeof(PackageManifest), "package.json"),
        new("country", typeof(CountryConfig), "countries/*.json"),
        new("clubs", typeof(List<ClubDefinition>), "clubs/*.json"),
        new("players", typeof(List<PlayerDefinition>), "players/*.json"),
        new("staff", typeof(List<StaffDefinition>), "staff/*.json"),
        new("managers", typeof(List<ManagerDefinition>), "managers/*.json"),
        new("referees", typeof(List<RefereeDefinition>), "referees/*.json"),
        new("names", typeof(NameList), "names/*.json"),
        new("sponsors", typeof(SponsorList), "sponsors/*.json"),
        new("events", typeof(List<EventDef>), "events/*.json"),
        new("formations", typeof(List<Formation>), "formations.json"),
        new("positions", typeof(PositionProfiles), "rules/positions.json"),
        new("injuries", typeof(List<InjuryType>), "rules/injuries.json"),
        new("roles", typeof(List<RoleDef>), "rules/roles.json"),
        new("buildings", typeof(List<BuildingDef>), "rules/buildings.json"),
        new("press", typeof(List<PressQuestionDef>), "rules/press.json"),
        new("engine", typeof(EngineConfig), "rules/engine.json"),
        new("currencies", typeof(CurrencyConfig), "rules/currencies.json"),
        new("transfers", typeof(TransferRules), "rules/transfers.json"),
        new("manager-rules", typeof(ManagerRules), "rules/managers.json"),
        new("relations", typeof(RelationsConfig), "rules/relations.json"),
        new("agents", typeof(AgentRules), "rules/agents.json"),
        new("europe", typeof(EuropeFormat), "competitions/europe.json"),
        new("lang", typeof(Dictionary<string, string>), "lang/*.json"),
        new("commentary", typeof(Dictionary<string, List<string>>), "lang/*.commentary.json"),
    };

    public static SchemaEntry? For(string relativePath) => Entries.FirstOrDefault(e => e.Matches(relativePath.Replace('\\', '/')));

    private static readonly Lazy<DocComments> Docs = new(() => DocComments.For(typeof(CountryConfig).Assembly));

    /// <summary>Erzeugt das Schema eines Eintrags (mit Beschreibungen, sofern die XML-Kommentare vorliegen).</summary>
    public static JsonObject Generate(SchemaEntry entry)
    {
        var options = new JsonSerializerOptions(Json.Options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var exporter = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true, TransformSchemaNode = Transform };
        var schema = (JsonObject)JsonSchemaExporter.GetJsonSchemaAsNode(options, entry.Type, exporter);
        var result = new JsonObject
        {
            ["$schema"] = Draft,
            ["$id"] = $"https://fmh.local/schema/{entry.FileName}",
            ["title"] = $"FMH {entry.Pattern}",
        };
        foreach (var (key, value) in schema.ToList())
        {
            schema.Remove(key);
            result[key] = value;
        }
        return result;
    }

    private static JsonNode Transform(JsonSchemaExporterContext ctx, JsonNode node)
    {
        if (node is not JsonObject schema) return node;
        var type = ctx.TypeInfo.Type;
        if (type == typeof(PlayerAttributes))
        {
            // Eigener Konverter: Objekt mit allen 44 Attributen (Name wie im Enum) zu je 1–20
            var props = new JsonObject();
            foreach (var a in AttrInfo.All) props[a.ToString()] = Range("integer", AttrInfo.Min, AttrInfo.Max);
            return new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] is { IsEnum: true } keyType)
            schema["propertyNames"] = new JsonObject { ["enum"] = new JsonArray(Enum.GetNames(keyType).Select(n => (JsonNode)n).ToArray()) };
        if (schema["properties"] is JsonObject && !schema.ContainsKey("additionalProperties"))
            schema["additionalProperties"] = false;

        if (ctx.PropertyInfo?.AttributeProvider is MemberInfo member)
        {
            if (member.GetCustomAttribute<RangeAttribute>() is { } range)
            {
                schema["minimum"] = JsonValue.Create(Convert.ToDouble(range.Minimum, System.Globalization.CultureInfo.InvariantCulture));
                schema["maximum"] = JsonValue.Create(Convert.ToDouble(range.Maximum, System.Globalization.CultureInfo.InvariantCulture));
            }
            if (Docs.Value.Member(member) is { } text) schema["description"] = text;
        }
        else if (ctx.PropertyInfo is null && Docs.Value.Type(type) is { } text && !schema.ContainsKey("description"))
            schema["description"] = text;
        return schema;
    }

    /// <summary>
    /// Zuordnung für VS Code (<c>.vscode/settings.json</c>, „json.schemas“): Autovervollständigung und Prüfung beim
    /// Bearbeiten der Datenpakete im Texteditor.
    /// </summary>
    public static JsonObject EditorSettings()
    {
        var list = new JsonArray();
        foreach (var e in Entries)
        {
            var match = new JsonArray($"data/*/{e.Pattern}");
            if (e.Name == "lang") match.Add("!data/*/lang/*.commentary.json");
            list.Add(new JsonObject { ["fileMatch"] = match, ["url"] = $"./data/{Folder}/{e.FileName}" });
        }
        return new JsonObject { ["json.schemas"] = list };
    }

    private static JsonObject Range(string type, int min, int max) => new() { ["type"] = type, ["minimum"] = min, ["maximum"] = max };

    /// <summary>
    /// Schemas zum Prüfen: die Dateien aus <c>data/schema/</c> neben einem der Paketordner, sonst frisch erzeugt
    /// (z. B. für Testverzeichnisse ohne Schemaordner). Beide sind gleich; ein Test hält die Dateien aktuell.
    /// </summary>
    public static Dictionary<string, JsonObject> Load(IEnumerable<string> packageDirectories)
    {
        var folder = packageDirectories.Select(d => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(d)) ?? "", Folder))
            .FirstOrDefault(Directory.Exists);
        var result = new Dictionary<string, JsonObject>();
        foreach (var entry in Entries)
        {
            var file = folder is null ? null : Path.Combine(folder, entry.FileName);
            result[entry.Name] = file is not null && File.Exists(file)
                ? (JsonObject)JsonNode.Parse(File.ReadAllText(file))!
                : Generated.Value[entry.Name];
        }
        return result;
    }

    private static readonly Lazy<Dictionary<string, JsonObject>> Generated = new(() => Entries.ToDictionary(e => e.Name, Generate));
}
