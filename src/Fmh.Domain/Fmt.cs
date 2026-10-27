using System.Globalization;
using Fmh.Core.Data;

namespace Fmh.Core;

/// <summary>
/// Zahlen-, Datums- und Geldformate der gewählten Sprache. Die Regeln stehen in der Sprachdatei
/// (Schlüssel "fmt.*"), damit auch ohne ICU (invarianter Globalisierungsmodus) jede Sprache passt.
/// Standard sind die deutschen Formate.
/// </summary>
public static class Fmt
{
    private static string _date = "dd.MM.yyyy";
    private static string _shortDate = "dd.MM.";
    private static string[] _weekdays = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
    private static string _million = "{0} Mio. {1}";
    private static string _thousand = "{0} Tsd. {1}";
    private static string _money = "{0} {1}";
    private static string _displayCurrency = CurrencyConfig.Base;
    private static double _displayRate = 1.0;

    /// <summary>Anzeigewährung (alle Beträge im Spiel sind Euro; umgerechnet wird nur für die Anzeige).</summary>
    public static string DisplayCurrency => _displayCurrency;
    public static double DisplayRate => _displayRate;

    /// <summary>Stellt die Anzeigewährung ein: Code und Einheiten je Euro.</summary>
    public static void UseCurrency(string code, double perEuro)
    {
        _displayCurrency = string.IsNullOrWhiteSpace(code) ? CurrencyConfig.Base : code;
        _displayRate = perEuro > 0 && _displayCurrency != CurrencyConfig.Base ? perEuro : 1.0;
    }

    /// <summary>Euro-Betrag in der Anzeigewährung.</summary>
    public static double ToDisplay(double euro) => euro * _displayRate;

    /// <summary>Betrag in der Anzeigewährung zurück in Euro.</summary>
    public static double FromDisplay(double shown) => shown / _displayRate;

    public static CultureInfo Culture { get; private set; } = CreateCulture(",", ".");

    private static CultureInfo CreateCulture(string decimalSep, string groupSep)
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberDecimalSeparator = decimalSep;
        c.NumberFormat.NumberGroupSeparator = groupSep;
        c.NumberFormat.PercentDecimalSeparator = decimalSep;
        c.NumberFormat.PercentGroupSeparator = groupSep;
        c.NumberFormat.CurrencyDecimalSeparator = decimalSep;
        c.NumberFormat.CurrencyGroupSeparator = groupSep;
        return c;
    }

    /// <summary>Übernimmt die Formate einer Sprache. Fehlende Schlüssel behalten die deutschen Werte.</summary>
    public static void Use(Texts t)
    {
        Culture = CreateCulture(t.TryGet("fmt.decimal") ?? ",", t.TryGet("fmt.group") ?? ".");
        _date = t.TryGet("fmt.date") ?? "dd.MM.yyyy";
        _shortDate = t.TryGet("fmt.shortDate") ?? "dd.MM.";
        _weekdays = (t.TryGet("fmt.weekdays") ?? "So,Mo,Di,Mi,Do,Fr,Sa").Split(',');
        if (_weekdays.Length != 7) _weekdays = new[] { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
        _million = t.TryGet("fmt.moneyMillion") ?? "{0} Mio. {1}";
        _thousand = t.TryGet("fmt.moneyThousand") ?? "{0} Tsd. {1}";
        _money = t.TryGet("fmt.money") ?? "{0} {1}";
    }

    public static string Num(double value, string format = "0.0") => value.ToString(format, Culture);

    public static string Int(long value) => value.ToString("#,0", Culture);

    public static string Date(DateOnly d) => d.ToString(_date, CultureInfo.InvariantCulture);

    public static string ShortDate(DateOnly d) => d.ToString(_shortDate, CultureInfo.InvariantCulture);

    public static string Weekday(DateOnly d) => _weekdays[(int)d.DayOfWeek];

    /// <summary>
    /// Euro-Betrag kompakt in der Anzeigewährung, z. B. "4,21 Mio. EUR" bzw. "GBP 3.66m". Der Parameter
    /// <paramref name="currency"/> stammt aus Zeiten mehrerer Spielwährungen und wird ignoriert.
    /// </summary>
    public static string Money(long amount, string currency = CurrencyConfig.Base)
    {
        amount = (long)Math.Round(ToDisplay(amount));
        currency = _displayCurrency;
        var abs = Math.Abs(amount);
        if (abs >= 1_000_000) return string.Format(_million, Num(amount / 1_000_000.0, "0.00"), currency);
        if (abs >= 10_000) return string.Format(_thousand, Num(amount / 1_000.0, "0"), currency);
        return string.Format(_money, Int(amount), currency);
    }
}
