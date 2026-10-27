namespace Fmh.Core.Data;

public enum TextArgKind { Literal, Key, Money, Date, Int, Num, Ref, Join }

/// <summary>
/// Ein Platzhalterwert, der erst bei der Anzeige in der gewählten Sprache formatiert wird:
/// Textschlüssel, Geldbetrag, Datum, Zahl, verschachtelter Text oder Aufzählung.
/// </summary>
public sealed class TextArg
{
    public TextArgKind Kind { get; set; }
    /// <summary>Literal oder Textschlüssel.</summary>
    public string? Value { get; set; }
    /// <summary>Anzeige, falls der Schlüssel in keiner Sprache existiert (z. B. Wettbewerbsname aus den Daten).</summary>
    public string? Fallback { get; set; }
    public long Amount { get; set; }
    public string? Currency { get; set; }
    public double Number { get; set; }
    public string? Format { get; set; }
    public DateOnly? Date { get; set; }
    public TextRef? Ref { get; set; }
    public List<TextArg>? Items { get; set; }
    public string? Separator { get; set; }

    public static TextArg Literal(string s) => new() { Kind = TextArgKind.Literal, Value = s };
    public static TextArg Key(string key, string? fallback = null) => new() { Kind = TextArgKind.Key, Value = key, Fallback = fallback };
    public static TextArg Money(long amount, string currency) => new() { Kind = TextArgKind.Money, Amount = amount, Currency = currency };
    public static TextArg OfDate(DateOnly d) => new() { Kind = TextArgKind.Date, Date = d };
    public static TextArg Int(long n) => new() { Kind = TextArgKind.Int, Amount = n };
    public static TextArg Num(double v, string format = "0.0") => new() { Kind = TextArgKind.Num, Number = v, Format = format };
    public static TextArg Of(TextRef r) => new() { Kind = TextArgKind.Ref, Ref = r };
    public static TextArg Join(string separator, IEnumerable<object?> items) =>
        new() { Kind = TextArgKind.Join, Separator = separator, Items = items.Select(From).ToList() };

    /// <summary>Wandelt einen beliebigen Wert in ein Argument: Zeichenketten bleiben wörtlich, Zahlen und Daten werden typisiert.</summary>
    public static TextArg From(object? value) => value switch
    {
        null => Literal(""),
        TextArg a => a,
        TextRef r => Of(r),
        string s => Literal(s),
        int i => Int(i),
        long l => Int(l),
        double d => Num(d),
        DateOnly d => OfDate(d),
        char c => Literal(c.ToString()),
        _ => Literal(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
    };
}

/// <summary>
/// Sprachneutraler Text: Schlüssel aus der Sprachdatei plus Argumente. Nachrichten, Buchungen und
/// Verhandlungsantworten werden so gespeichert und bei jeder Anzeige in der aktuellen Sprache aufgebaut,
/// damit ein Sprachwechsel auch ältere Inhalte übersetzt.
/// </summary>
public sealed class TextRef
{
    /// <summary>Leer = wörtlicher Text (z. B. Nachrichten aus Spielständen vor der Mehrsprachigkeit).</summary>
    public string Key { get; set; } = "";
    public List<TextArg> Args { get; set; } = new();

    public static TextRef Of(string key, params object?[] args) => new() { Key = key, Args = args.Select(TextArg.From).ToList() };

    public static TextRef Literal(string text) => new() { Args = { TextArg.Literal(text) } };

    public override string ToString() => Key.Length == 0 ? string.Concat(Args.Select(a => a.Value)) : Key;
}
