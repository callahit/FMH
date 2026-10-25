using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>Aktivierungsstufe eines Landes (Kapitel 3).</summary>
public enum CountryActivation { Playable, Background, Inactive }

/// <summary>Tabellenkriterien nach Punkten, Reihenfolge frei konfigurierbar.</summary>
public enum TieBreaker { GoalDifference, GoalsFor, HeadToHead, AwayGoals, Wins }

/// <summary>Länderkonfiguration: Ligapyramide, Kalender, Regeln (Kapitel 3). Gilt ab <see cref="ValidFrom"/>.</summary>
public sealed class CountryConfig
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public int ValidFrom { get; set; }
    public CountryActivation Activation { get; set; } = CountryActivation.Playable;
    public SeasonConfig Season { get; set; } = new();
    public SquadRules Squad { get; set; } = new();
    public DisciplineConfig Discipline { get; set; } = new();
    public List<LevelConfig> Levels { get; set; } = new();

    public IEnumerable<DivisionConfig> Divisions => Levels.SelectMany(l => l.Divisions);
}

public sealed class SeasonConfig
{
    /// <summary>Monat des Saisonbeginns (7 = Juli, Herbst–Frühjahr; 1 = Kalenderjahr).</summary>
    public int StartMonth { get; set; } = 7;
    /// <summary>Frühester Termin des 1. Spieltags im Format MM-TT.</summary>
    public string FirstMatchday { get; set; } = "08-15";
    public string WinterBreakStart { get; set; } = "12-20";
    public string WinterBreakEnd { get; set; } = "01-15";
    public DayOfWeek MatchDay { get; set; } = DayOfWeek.Saturday;
}

public sealed class SquadRules
{
    public int MaxSquad { get; set; } = 30;
    public int BenchSize { get; set; } = 9;
    public int Substitutions { get; set; } = 5;
}

public sealed class DisciplineConfig
{
    public int YellowCardsForSuspension { get; set; } = 5;
    public int RedCardBanMin { get; set; } = 1;
    public int RedCardBanMax { get; set; } = 3;
    public int SecondYellowBan { get; set; } = 1;
}

public sealed class LevelConfig
{
    public int Level { get; set; }
    public List<DivisionConfig> Divisions { get; set; } = new();
}

public sealed class DivisionConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Teams { get; set; } = 18;
    /// <summary>2 = Doppelrunde, 3 = Dreifachrunde, 4 = Vierfachrunde.</summary>
    public int Rounds { get; set; } = 2;
    public PointsConfig Points { get; set; } = new();
    public List<TieBreaker> TieBreakers { get; set; } = new() { TieBreaker.GoalDifference, TieBreaker.GoalsFor };
    /// <summary>Direkte Aufstiegsplätze in die nächsthöhere Ebene.</summary>
    public int Promotion { get; set; }
    /// <summary>Direkte Abstiegsplätze in die nächsttiefere Ebene.</summary>
    public int Relegation { get; set; }
}

public sealed class PointsConfig
{
    public int Win { get; set; } = 3;
    public int Draw { get; set; } = 1;
    public int Loss { get; set; }
}

/// <summary>Vereinsdefinition im Datenpaket. Spieler werden beim Start erzeugt.</summary>
public sealed class ClubDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string City { get; set; } = "";
    public string Division { get; set; } = "";
    public int Reputation { get; set; } = 10;
    public string PrimaryColor { get; set; } = "White";
    public string SecondaryColor { get; set; } = "Black";
    public string Stadium { get; set; } = "";
    public int Capacity { get; set; } = 15000;
}

/// <summary>Namenslisten für Zufallsspieler je Land.</summary>
public sealed class NameList
{
    public string Country { get; set; } = "";
    public List<string> FirstNames { get; set; } = new();
    public List<string> LastNames { get; set; } = new();
}

public sealed class PackageManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public List<string> DependsOn { get; set; } = new();

    /// <summary>Verzeichnis des Pakets auf der Platte (nicht Teil der Datei).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Directory { get; set; } = "";
}

public sealed class InjuryType
{
    public string Key { get; set; } = "";
    public int MinDays { get; set; }
    public int MaxDays { get; set; }
    public double Weight { get; set; } = 1;
}

/// <summary>Gewichtung der Attribute je Position für Positionsstärke und Gesamtstärke.</summary>
public sealed class PositionProfiles
{
    public Dictionary<Position, Dictionary<Attr, double>> Weights { get; set; } = new();
}
