using System.ComponentModel.DataAnnotations;
using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>
/// Vorgegebener Spieler (Umsetzungskonzept 7.2, Datei <c>players/*.json</c>). Nur Schlüssel, Name und Position sind
/// Pflicht; alles Weitere ist ein Rahmen, den der Weltgenerator wie bei Zufallsspielern auffüllt.
/// </summary>
public sealed class PlayerDefinition
{
    /// <summary>Eindeutiger Schlüssel über alle Pakete, z. B. "de1-3-p7".</summary>
    public string Key { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    /// <summary>Verein (Schlüssel aus clubs/*.json); leer = vereinslos zum Spielstart.</summary>
    public string Club { get; set; } = "";
    /// <summary>Hauptposition; dort ist der Spieler immer „Natural“.</summary>
    public Position Position { get; set; } = Position.MC;
    /// <summary>Weitere Positionen mit Eignung; fehlende erzeugt der Generator.</summary>
    public Dictionary<Position, Suitability> Positions { get; set; } = new();
    /// <summary>Geburtsdatum (JJJJ-MM-TT); sonst <see cref="Age"/> am Spielstart, sonst zufällig.</summary>
    public DateOnly? BirthDate { get; set; }
    [Range(15, 45)]
    public int? Age { get; set; }
    /// <summary>Nationalitäten (Ländercodes); leer = Land des Vereins.</summary>
    public List<string> Nationalities { get; set; } = new();
    /// <summary>Gesamtstärke 1–20, aus der die Attribute entstehen; leer = nach Verein und Alter.</summary>
    [Range(1, 20)]
    public double? Level { get; set; }
    /// <summary>Potenzial 1–200 (Skala der versteckten Fähigkeit); nie unter der aktuellen Fähigkeit.</summary>
    [Range(1, 200)]
    public int? PotentialAbility { get; set; }
    /// <summary>Einzelne Attribute 1–20, die die erzeugten Werte ersetzen.</summary>
    public Dictionary<Attr, int> Attributes { get; set; } = new();
    /// <summary>Versteckte Charakterwerte 1–20, die die erzeugten Werte ersetzen.</summary>
    public HiddenDefinition Hidden { get; set; } = new();
    public PreferredFoot? Foot { get; set; }
    [Range(150, 215)]
    public int? Height { get; set; }
    [Range(50, 110)]
    public int? Weight { get; set; }
    [Range(1, 20)]
    public int? Reputation { get; set; }
    /// <summary>Wochengehalt in Euro (vor der Umrechnung auf das Lohnniveau der Liga); leer = nach Stärke.</summary>
    [Range(0, 5_000_000)]
    public int? WeeklyWage { get; set; }
    /// <summary>Vertrag bis 30. Juni dieses Jahres.</summary>
    [Range(2000, 2100)]
    public int? ContractUntil { get; set; }
}

/// <summary>Versteckte Werte eines vorgegebenen Spielers; leere Felder erzeugt der Generator.</summary>
public sealed class HiddenDefinition
{
    [Range(1, 20)] public int? Ambition { get; set; }
    [Range(1, 20)] public int? Professionalism { get; set; }
    [Range(1, 20)] public int? Loyalty { get; set; }
    [Range(1, 20)] public int? Pressure { get; set; }
    [Range(1, 20)] public int? Temperament { get; set; }
    [Range(1, 20)] public int? Sportsmanship { get; set; }
    [Range(1, 20)] public int? Adaptability { get; set; }
    [Range(1, 20)] public int? Controversy { get; set; }
    [Range(1, 20)] public int? Consistency { get; set; }
    [Range(1, 20)] public int? InjuryProneness { get; set; }
    [Range(1, 20)] public int? ImportantMatches { get; set; }
}

/// <summary>Vorgegebenes Personal (Datei <c>staff/*.json</c>); ersetzt beim Verein das erzeugte Personal derselben Rolle.</summary>
public sealed class StaffDefinition
{
    public string Name { get; set; } = "";
    /// <summary>Verein (Schlüssel); leer = auf dem Arbeitsmarkt.</summary>
    public string Club { get; set; } = "";
    public StaffRole Role { get; set; }
    [Range(18, 80)]
    public int Age { get; set; } = 45;
    /// <summary>Hauptwert der Rolle 1–20.</summary>
    [Range(1, 20)]
    public int Ability { get; set; } = 10;
    /// <summary>Motivation und Menschenführung 1–20; leer = wie die Fähigkeit.</summary>
    [Range(1, 20)]
    public int? Motivation { get; set; }
    /// <summary>Nur Jugendtrainer: betreute Altersstufe.</summary>
    public YouthAgeGroup? AgeGroup { get; set; }
    [Range(2000, 2100)]
    public int? ContractUntil { get; set; }
}

/// <summary>KI-Manager eines Vereins (Datei <c>managers/*.json</c>); übernimmt den Verein, den der Spieler verlässt.</summary>
public sealed class ManagerDefinition
{
    public string Name { get; set; } = "";
    /// <summary>Verein (Schlüssel); leer = ohne Verein.</summary>
    public string Club { get; set; } = "";
    [Range(1, 20)]
    public int Reputation { get; set; } = 8;
    /// <summary>Spielidee: wirkt auf Grundhaltung und Anweisungen der Mannschaft.</summary>
    public ManagerStyle Style { get; set; } = ManagerStyle.Balanced;
    /// <summary>Taktisches Können 1–20; leer = wie der Ruf.</summary>
    [Range(1, 20)]
    public int? Tactics { get; set; }
    /// <summary>Menschenführung 1–20; leer = 10.</summary>
    [Range(1, 20)]
    public int? Motivation { get; set; }
    [Range(25, 80)]
    public int Age { get; set; } = 50;
    /// <summary>Ländercode der Herkunft.</summary>
    public string Nationality { get; set; } = "";
}

/// <summary>Schiedsrichter (Datei <c>referees/*.json</c>); sind welche vorgegeben, ersetzen sie die erzeugten.</summary>
public sealed class RefereeDefinition
{
    public string Name { get; set; } = "";
    /// <summary>Ländercode; leer = alle Länder.</summary>
    public string Country { get; set; } = "";
    /// <summary>Strenge 1–20.</summary>
    [Range(1, 20)]
    public int Strictness { get; set; } = 10;
}
