using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Fmh.Core.Data.Schema;

/// <summary>
/// Liest die XML-Kommentare (&lt;summary&gt;) einer Assembly, damit die JSON-Schemas Beschreibungen für
/// Texteditoren und den Editor bekommen. Fehlt die XML-Datei, bleiben die Beschreibungen leer.
/// </summary>
public sealed partial class DocComments
{
    private readonly Dictionary<string, string> _summaries;

    private DocComments(Dictionary<string, string> summaries) => _summaries = summaries;

    public static DocComments For(Assembly assembly)
    {
        var file = Path.ChangeExtension(assembly.Location, ".xml");
        if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(file)) return new DocComments(new());
        var doc = XDocument.Load(file);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in doc.Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { } name || member.Element("summary") is not { } summary) continue;
            map[name] = Clean(summary);
        }
        return new DocComments(map);
    }

    public string? Type(Type type) => type.FullName is { } n && _summaries.TryGetValue("T:" + n.Replace('+', '.'), out var s) ? s : null;

    public string? Member(MemberInfo member)
    {
        if (member.DeclaringType?.FullName is not { } owner) return null;
        var prefix = member is FieldInfo ? "F:" : "P:";
        return _summaries.TryGetValue($"{prefix}{owner.Replace('+', '.')}.{member.Name}", out var s) ? s : null;
    }

    /// <summary>Text ohne Markup: Verweise werden zum Namen, Leerraum zu einfachen Leerzeichen.</summary>
    private static string Clean(XElement summary)
    {
        foreach (var see in summary.Descendants().Where(e => e.Name.LocalName is "see" or "paramref" or "typeparamref").ToList())
        {
            var target = see.Attribute("cref")?.Value ?? see.Attribute("name")?.Value ?? see.Attribute("langword")?.Value ?? "";
            see.ReplaceWith(target[(target.LastIndexOf('.') + 1)..]);
        }
        return Whitespace().Replace(summary.Value, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
