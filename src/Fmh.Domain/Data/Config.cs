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
    /// <summary>Letzte Saison (Startjahr), in der diese Formatversion gilt; null = unbegrenzt.</summary>
    public int? ValidTo { get; set; }
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
    public ReserveRules Reserves { get; set; } = new();

    public IEnumerable<DivisionConfig> Divisions => Levels.SelectMany(l => l.Divisions);

    public bool ValidIn(int seasonYear) => ValidFrom <= seasonYear && (ValidTo is null || ValidTo >= seasonYear);
}

public sealed class PoolConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Teams { get; set; }
    /// <summary>
    /// Division der untersten Ebene, unter der der Pool liegt: Ihre Absteiger fallen in den Pool, dafür steigen
    /// ebenso viele Poolvereine auf. Leer = der Pool spielt nur im Pokal.
    /// </summary>
    public string? Feeds { get; set; }
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
    /// <summary>Spiele im Finale, abweichend von <see cref="Legs"/>.</summary>
    public int? FinalLegs { get; set; }
    public bool AwayGoals { get; set; }
    /// <summary>Termine je Runde im Format MM-TT (bei zwei Spielen zwei Termine je Runde, durch Komma getrennt).</summary>
    public List<string> Dates { get; set; } = new();
    public bool NeutralFinal { get; set; } = true;
}

/// <summary>
/// Relegation bzw. Aufstiegsturnier zwischen zwei Ebenen. Zwei Teilnehmer spielen ein Duell, vier oder acht ein
/// K.-o.-Turnier. Die <see cref="Places"/> Sieger spielen in der nächsten Saison in <see cref="UpperDivision"/>,
/// alle übrigen Teilnehmer in <see cref="LowerDivision"/>.
/// </summary>
public sealed class PlayoffConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string UpperDivision { get; set; } = "";
    /// <summary>Älteres Format: Platz in der oberen Liga (wird auf <see cref="Entrants"/> abgebildet).</summary>
    public int UpperPosition { get; set; }
    public string LowerDivision { get; set; } = "";
    /// <summary>Älteres Format: Platz in der unteren Liga (wird auf <see cref="Entrants"/> abgebildet).</summary>
    public int LowerPosition { get; set; }
    public List<PlayoffEntrant> Entrants { get; set; } = new();
    public int Places { get; set; } = 1;
    /// <summary>Übergangsregel, die nur in dieser Saison gilt.</summary>
    public int? OnlySeason { get; set; }
    /// <summary>Rotation: Die Relegation findet nur alle <see cref="Cycle"/> Saisons statt, erstmals in <see cref="CycleStart"/>.</summary>
    public int? Cycle { get; set; }
    public int CycleStart { get; set; }
    public int Legs { get; set; } = 2;
    /// <summary>Spiele im Finale eines Aufstiegsturniers, abweichend von <see cref="Legs"/> (z. B. 1 in England).</summary>
    public int? FinalLegs { get; set; }
    /// <summary>
    /// Gesetzte Paarungen statt Auslosung: Teilnehmer in der Reihenfolge der <see cref="Entrants"/>, der Beste gegen den
    /// Schwächsten (3. gegen 6., 4. gegen 5.); der Besserplatzierte hat im Rückspiel Heimrecht.
    /// </summary>
    public bool Seeded { get; set; }

    public bool AppliesTo(int seasonYear) => (OnlySeason is null || OnlySeason == seasonYear) && SeasonCycle.InCycle(Cycle, CycleStart, seasonYear);
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
    /// <summary>
    /// Letzter Termin für reguläre Spieltage (MM-TT). Passen nicht alle Spieltage wöchentlich davor, kommen Englische Wochen
    /// am <see cref="MidweekDay"/> hinzu, gleichmäßig über die Saison verteilt. Leer = nur wöchentlich.
    /// </summary>
    public string? LastMatchday { get; set; }
    public DayOfWeek MidweekDay { get; set; } = DayOfWeek.Tuesday;
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
    /// <summary>Staffeln (geteilte Liga); leer = eine Liga mit der Kennung der Division. <see cref="Teams"/> gilt je Staffel.</summary>
    public List<GroupConfig> Groups { get; set; } = new();
    public GroupAssignment Assignment { get; set; } = GroupAssignment.Fixed;
    public List<MovementRule> Movements { get; set; } = new();
    /// <summary>Weitere Saisonphasen nach dem Grunddurchgang (bisher höchstens eine).</summary>
    public List<PhaseConfig> Phases { get; set; } = new();

    /// <summary>Kennungen der Ligen im Spiel: die Staffeln oder die Division selbst.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> LeagueIds => Groups.Count == 0 ? new[] { Id } : Groups.Select(g => g.Id);

    /// <summary>Bewegungsregeln einer Liga im Spiel: die der Staffel, falls sie eigene hat, sonst die der Division.</summary>
    public List<MovementRule> MovementsOf(string leagueId) =>
        Groups.FirstOrDefault(g => g.Id == leagueId) is { Movements.Count: > 0 } g ? g.Movements : Movements;
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
    /// <summary>Zweite Mannschaft: Schlüssel des Vereins der ersten Mannschaft.</summary>
    public string? ReserveOf { get; set; }
    /// <summary>Region für die Staffelzuteilung.</summary>
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
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
