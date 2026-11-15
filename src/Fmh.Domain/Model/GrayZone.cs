namespace Fmh.Core.Model;

/// <summary>Grauzonen-Akte eines Managers (AP 13c, Anstoß-Element <c>grayZones</c>): Taten und aufgeflogene Fälle.</summary>
public sealed class GrayZoneFile
{
    public List<GrayDeed> Deeds { get; set; } = new();
    /// <summary>Aufgeflogene Taten; ab der Grenze aus den Regeln folgt die Entlassung.</summary>
    public int Detected { get; set; }
}

/// <summary>Eine Tat: Schiedsrichter beeinflussen, verbotene Mittel oder Vertragstrick.</summary>
public sealed class GrayDeed
{
    /// <summary>Kennung aus rules/grayZones.json.</summary>
    public string Action { get; set; } = "";
    public DateOnly Date { get; set; }
    /// <summary>Betroffenes Spiel (bei Spiel-Taten).</summary>
    public int? FixtureId { get; set; }
    /// <summary>Betroffener Spieler (Vertragstrick).</summary>
    public int? PlayerId { get; set; }
    /// <summary>Gedopte Spieler (Startelf und Bank des Spiels).</summary>
    public List<int> Players { get; set; } = new();
    public bool Resolved { get; set; }
    public bool Detected { get; set; }
}
