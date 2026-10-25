using System.Globalization;

namespace Fmh.Core;

/// <summary>
/// Deutsche Zahlenformatierung ohne ICU-Abhängigkeit (die Anwendung läuft im invarianten Globalisierungsmodus).
/// </summary>
public static class Fmt
{
    public static readonly CultureInfo Culture = Create();

    private static CultureInfo Create()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberDecimalSeparator = ",";
        c.NumberFormat.NumberGroupSeparator = ".";
        c.NumberFormat.PercentDecimalSeparator = ",";
        c.NumberFormat.PercentGroupSeparator = ".";
        c.NumberFormat.CurrencyDecimalSeparator = ",";
        c.NumberFormat.CurrencyGroupSeparator = ".";
        return c;
    }

    public static string Num(double value, string format = "0.0") => value.ToString(format, Culture);

    public static string Int(long value) => value.ToString("#,0", Culture);

    public static string Date(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string ShortDate(DateOnly d) => d.ToString("dd.MM.", CultureInfo.InvariantCulture);

    private static readonly string[] Weekdays = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

    public static string Weekday(DateOnly d) => Weekdays[(int)d.DayOfWeek];

    /// <summary>Geldbetrag kompakt: 4,21 Mio. EUR, 350 Tsd. EUR, 900 EUR.</summary>
    public static string Money(long amount, string currency)
    {
        var abs = Math.Abs(amount);
        if (abs >= 1_000_000) return $"{Num(amount / 1_000_000.0, "0.00")} Mio. {currency}";
        if (abs >= 10_000) return $"{Num(amount / 1_000.0, "0")} Tsd. {currency}";
        return $"{Int(amount)} {currency}";
    }
}
