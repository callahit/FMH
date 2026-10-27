namespace Fmh.Core.Data;

/// <summary>
/// Sprachdatei: alle sichtbaren Texte stehen in data/&lt;paket&gt;/lang/&lt;sprache&gt;.json (Kapitel 12, Lokalisierung).
/// Fehlt ein Schlüssel, wird die Rückfallsprache (Standard des Pakets) verwendet; fehlt er auch dort,
/// erscheint "[schlüssel]", damit er im Test auffällt.
/// </summary>
public sealed class Texts
{
    private readonly Dictionary<string, string> _entries;
    private readonly Texts? _fallback;

    public Texts(Dictionary<string, string> entries, string language, Texts? fallback = null)
    {
        _entries = entries;
        Language = language;
        _fallback = fallback;
    }

    public string Language { get; }

    /// <summary>Anzeigename der Sprache in ihrer eigenen Schreibweise ("Deutsch", "English").</summary>
    public string Name => _entries.TryGetValue("lang.name", out var n) ? n : Language;

    /// <summary>Nur die Einträge dieser Sprache, ohne Rückfallsprache.</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    public bool Has(string key) => _entries.ContainsKey(key) || (_fallback?.Has(key) ?? false);

    public string this[string key] => TryGet(key) ?? $"[{key}]";

    public string? TryGet(string key) =>
        _entries.TryGetValue(key, out var v) ? v : _fallback?.TryGet(key);

    /// <summary>Text mit Platzhaltern {0}, {1} ...</summary>
    public string Format(string key, params object[] args) =>
        string.Format(Fmt.Culture, this[key], args);

    /// <summary>Baut einen sprachneutralen Text in dieser Sprache auf.</summary>
    public string Render(TextRef? r)
    {
        if (r is null) return "";
        if (r.Key.Length == 0) return string.Concat(r.Args.Select(Render));
        if (r.Args.Count == 0) return this[r.Key];
        return string.Format(Fmt.Culture, this[r.Key], r.Args.Select(a => (object)Render(a)).ToArray());
    }

    public string Render(TextArg a) => a.Kind switch
    {
        TextArgKind.Literal => a.Value ?? "",
        TextArgKind.Key => TryGet(a.Value ?? "") ?? a.Fallback ?? $"[{a.Value}]",
        TextArgKind.Money => Fmt.Money(a.Amount, a.Currency ?? ""),
        TextArgKind.Date => a.Date is { } d ? Fmt.Date(d) : "",
        TextArgKind.Int => Fmt.Int(a.Amount),
        TextArgKind.Num => Fmt.Num(a.Number, a.Format ?? "0.0"),
        TextArgKind.Ref => Render(a.Ref),
        TextArgKind.Join => string.Join(a.Separator ?? ", ", (a.Items ?? new()).Select(Render)),
        _ => "",
    };
}
