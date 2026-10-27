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
    /// <summary>Vereine ohne Ligabetrieb im Spiel (z. B. Amateure), die nur im Pokal antreten.</summary>
    public List<PoolConfig> Pools { get; set; } = new();
    public List<CupConfig> Cups { get; set; } = new();
    public List<PlayoffConfig> Playoffs { get; set; } = new();
    public TransferWindowConfig TransferWindows { get; set; } = new();
    public YouthConfig Youth { get; set; } = new();
    public FinanceConfig Finance { get; set; } = new();
    public EuropeConfig? Europe { get; set; }

    public IEnumerable<DivisionConfig> Divisions => Levels.SelectMany(l => l.Divisions);
}

public sealed class PoolConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Teams { get; set; }
}

/// <summary>Pokalformat (Kapitel 3): Teilnehmer, Runden, Heimrecht, Termine.</summary>
public sealed class CupConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Ligen und Pools, deren Vereine teilnehmen.</summary>
    public List<string> Participants { get; set; } = new();
    /// <summary>Teilnehmerzahl (Zweierpotenz). Fehlende Plätze werden mit Freilosen aufgefüllt.</summary>
    public int Teams { get; set; } = 64;
    /// <summary>Unterklassige Vereine haben Heimrecht (wie im DFB-Pokal).</summary>
    public bool LowerTierHome { get; set; } = true;
    public int Legs { get; set; } = 1;
    public bool AwayGoals { get; set; }
    /// <summary>Termine je Runde im Format MM-TT (bei zwei Spielen zwei Termine je Runde, durch Komma getrennt).</summary>
    public List<string> Dates { get; set; } = new();
    public bool NeutralFinal { get; set; } = true;
}

/// <summary>Relegation zwischen zwei Ebenen (Hin- und Rückspiel).</summary>
public sealed class PlayoffConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string UpperDivision { get; set; } = "";
    public int UpperPosition { get; set; }
    public string LowerDivision { get; set; } = "";
    public int LowerPosition { get; set; }
    public int Legs { get; set; } = 2;
    public bool AwayGoals { get; set; }
    public List<string> Dates { get; set; } = new();
}

public sealed class TransferWindowConfig
{
    public string SummerStart { get; set; } = "07-01";
    public string SummerEnd { get; set; } = "08-31";
    public string WinterStart { get; set; } = "01-01";
    public string WinterEnd { get; set; } = "01-31";
    /// <summary>Vorverträge mit auslaufenden Spielern ab diesem Datum (MM-TT).</summary>
    public string PreContractFrom { get; set; } = "01-01";
}

public sealed class YouthConfig
{
    /// <summary>Monat des Jugendjahrgangs (Kapitel 7: März).</summary>
    public int IntakeMonth { get; set; } = 3;
    public int MinPlayers { get; set; } = 8;
    public int MaxPlayers { get; set; } = 16;
    public int ProContractAge { get; set; } = 17;
    public int PromoteAge { get; set; } = 16;
    /// <summary>Alter der Neuzugänge im Nachwuchsleistungszentrum (jüngste Altersstufe U15).</summary>
    public int IntakeAge { get; set; } = 14;
    /// <summary>Kader je Altersstufe (Grundwert; die Infrastruktur erweitert ihn).</summary>
    public int GroupSize { get; set; } = 14;
    /// <summary>Jahresbudget eines durchschnittlichen Leistungszentrums bei Reputation 10.</summary>
    public long ReferenceBudget { get; set; } = 900_000;
}

/// <summary>Spielerrolle (Kapitel 5, FM22): erlaubte Positionen, Aufgaben und Wirkung in der Matchengine.</summary>
public sealed class RoleDef
{
    public string Id { get; set; } = "";
    public List<Position> Positions { get; set; } = new();
    public List<Duty> Duties { get; set; } = new();
    public Duty DefaultDuty { get; set; } = Duty.Support;
    /// <summary>Faktoren auf die Aktionswahl (1 = neutral).</summary>
    public double Pass { get; set; } = 1;
    public double LongPass { get; set; } = 1;
    public double Dribble { get; set; } = 1;
    public double Cross { get; set; } = 1;
    public double Shot { get; set; } = 1;
    /// <summary>Zusätzlicher Vorstoß in Reihen beim Angriff.</summary>
    public double Push { get; set; }
    /// <summary>Wie oft der Spieler angespielt wird bzw. Ziel von Flanken ist.</summary>
    public double Target { get; set; } = 1;
    public double Aerial { get; set; } = 1;
    /// <summary>Bonus im Zweikampf als Verteidiger (Attributpunkte).</summary>
    public double Tackle { get; set; }
    /// <summary>Bonus beim Passspiel (Attributpunkte).</summary>
    public double Playmaking { get; set; }
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
    /// <summary>Rückfallsprache für fehlende Texte anderer Sprachen (nur im Grundpaket nötig).</summary>
    public string? DefaultLanguage { get; set; }

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
