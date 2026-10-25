using System.Globalization;

namespace Fmh.Core.Data;

/// <summary>
/// Sprachdatei: alle sichtbaren Texte stehen in data/&lt;paket&gt;/lang/&lt;sprache&gt;.json (Kapitel 12, Lokalisierung).
/// Fehlende Schlüssel werden als "[schlüssel]" angezeigt, damit sie im Test auffallen.
/// </summary>
public sealed class Texts
{
    private readonly Dictionary<string, string> _entries;

    public Texts(Dictionary<string, string> entries, string language)
    {
        _entries = entries;
        Language = language;
    }

    public string Language { get; }

    public IReadOnlyDictionary<string, string> Entries => _entries;

    public bool Has(string key) => _entries.ContainsKey(key);

    public string this[string key] => _entries.TryGetValue(key, out var v) ? v : $"[{key}]";

    /// <summary>Text mit Platzhaltern {0}, {1} ...</summary>
    public string Format(string key, params object[] args) =>
        string.Format(Fmt.Culture, this[key], args);
}
