namespace Fmh.Core.Data;

/// <summary>
/// Spielwährung und Anzeigekurse: Alle Beträge im Spiel sind in Euro (Leitwährung). Andere Währungen dienen nur der Anzeige;
/// ihre Kurse stehen in <c>rules/currencies.json</c> und lassen sich im Managerprofil überschreiben.
/// </summary>
public sealed class CurrencyConfig
{
    /// <summary>Leitwährung, in der alle Daten und Spielstände rechnen.</summary>
    public const string Base = "EUR";

    /// <summary>Stand der Standardkurse (Quelle in der Datei).</summary>
    public string AsOf { get; set; } = "";
    public List<CurrencyRate> Rates { get; set; } = new() { new CurrencyRate { Code = Base, PerEuro = 1.0 } };

    /// <summary>Kurs einer Währung: Einheiten je Euro; unbekannte Währungen zählen 1:1.</summary>
    public double PerEuro(string code) => Rates.FirstOrDefault(r => r.Code == code)?.PerEuro ?? 1.0;
}

public sealed class CurrencyRate
{
    public string Code { get; set; } = "";
    /// <summary>Einheiten dieser Währung je Euro (z. B. 0,87 Pfund).</summary>
    public double PerEuro { get; set; } = 1.0;
}
