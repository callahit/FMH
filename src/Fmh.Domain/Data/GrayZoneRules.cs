using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Grauzonen (AP 13c, Paket <c>data/anstoss</c>, <c>rules/grayZones.json</c>).</summary>
public sealed class GrayZoneRules
{
    public List<GrayAction> Actions { get; set; } = new();
    /// <summary>Mindestabstand zwischen zwei Taten in Tagen.</summary>
    [Range(0, 365)]
    public int CooldownDays { get; set; } = 14;
    /// <summary>So viele aufgeflogene Taten führen zur Entlassung.</summary>
    [Range(1, 20)]
    public int SackAfter { get; set; } = 3;
}

public enum GrayActionKind { Match, Negotiation }

public sealed class GrayAction
{
    public string Id { get; set; } = "";
    public GrayActionKind Kind { get; set; }
    /// <summary>Kosten in Euro für einen Erstligisten (skaliert mit der Vereinsgröße).</summary>
    [Range(0, 100_000_000)]
    public long Cost { get; set; }
    /// <summary>Tagesform-Bonus (Attributpunkte) der eigenen Spieler im betroffenen Spiel.</summary>
    [Range(0, 5)]
    public double DayForm { get; set; }
    /// <summary>Senkt die Gehaltsforderung in einer laufenden Verhandlung um diesen Anteil.</summary>
    [Range(0, 0.9)]
    public double WageCut { get; set; }
    /// <summary>Aufdeckungsrisiko je Tat.</summary>
    [Range(0, 1)]
    public double Detection { get; set; }
    public GrayPenalty Penalty { get; set; } = new();
}

/// <summary>Folgen, wenn die Tat auffliegt.</summary>
public sealed class GrayPenalty
{
    /// <summary>Geldstrafe in Euro für einen Erstligisten (skaliert).</summary>
    [Range(0, 100_000_000)]
    public long Fine { get; set; }
    [Range(-100, 0)]
    public double Board { get; set; }
    [Range(-100, 0)]
    public double Media { get; set; }
    /// <summary>Punktabzug in der Liga (nur Ligaspiele).</summary>
    [Range(0, 20)]
    public int Points { get; set; }
    /// <summary>Sperre je gedoptem Spieler in Spielen.</summary>
    [Range(0, 20)]
    public int PlayerBan { get; set; }
    /// <summary>Moral des betroffenen Spielers (Vertragstrick).</summary>
    [Range(-6, 0)]
    public int PlayerMorale { get; set; }
}
