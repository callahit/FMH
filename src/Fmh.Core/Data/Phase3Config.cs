using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>Wirtschaftsregeln eines Landes (Kapitel 8): TV-Gelder, Eintrittspreise, Prämien, Lizenzierung.</summary>
public sealed class FinanceConfig
{
    /// <summary>Je Liga bzw. Pool (Id).</summary>
    public Dictionary<string, DivisionFinance> Divisions { get; set; } = new();
    /// <summary>Pokalprämie je erreichter Runde (Index 0 = Teilnahme an Runde 1).</summary>
    public List<long> CupPrizes { get; set; } = new();
    public long CupWinnerPrize { get; set; }
    public long PlayoffPrize { get; set; }
    /// <summary>Kreditrahmen der Hausbank in Prozent der Saisoneinnahmen.</summary>
    public int CreditLimitPercent { get; set; } = 15;
    /// <summary>Bausumme, ab der der Vorstand zustimmen muss.</summary>
    public long BoardApprovalAbove { get; set; } = 2_000_000;
    /// <summary>Bausumme, ab der eine Baugenehmigung der Stadt nötig ist.</summary>
    public long PermitAbove { get; set; } = 1_000_000;
    /// <summary>Dauerkarten-Verkauf bis (MM-dd).</summary>
    public string SeasonTicketDeadline { get; set; } = "07-31";
}

public sealed class DivisionFinance
{
    /// <summary>TV-Gelder der Liga je Saison, halb gleich verteilt, halb nach Tabellenplatz.</summary>
    public long TvPool { get; set; }
    public double TvEqualShare { get; set; } = 0.5;
    public int Standing { get; set; } = 10;
    public int Seating { get; set; } = 20;
    public int Box { get; set; } = 1000;
    /// <summary>Mindestens nötige Flutlichtstufe (Lizenzpflicht).</summary>
    public int FloodlightRequired { get; set; }
    /// <summary>Jugendleistungszentrum Pflicht (Lizenz).</summary>
    public int YouthCenterRequired { get; set; }
    /// <summary>Eigenkapital, das bei der Lizenzierung mindestens vorhanden sein muss.</summary>
    public long MinEquity { get; set; }
    public long TravelPerAwayMatch { get; set; } = 20_000;
}

public sealed class BuildingRequirement
{
    public string Type { get; set; } = "";
    public int Level { get; set; } = 1;
}

public sealed class BuildingSynergy
{
    public string With { get; set; } = "";
    public string Effect { get; set; } = "";
    public double Value { get; set; }
}

/// <summary>Gebäudetyp des Vereinsgeländes (Kapitel 9), vollständig als Daten.</summary>
public sealed class BuildingDef
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;
    public long Cost { get; set; }
    /// <summary>Kostenfaktor je weiterer Stufe.</summary>
    public double CostGrowth { get; set; } = 1.6;
    /// <summary>Unterhalt pro Monat auf Stufe 1 (steigt je Stufe um 50 %).</summary>
    public long Upkeep { get; set; }
    public int BuildDays { get; set; } = 60;
    public List<BuildingRequirement> Requires { get; set; } = new();
    /// <summary>Wirkung je Stufe, Schlüssel siehe Effects.</summary>
    public Dictionary<string, double> Effects { get; set; } = new();
    /// <summary>Lagebonus: "entrance" (nahe Stadioneingang), "dressing" (nahe Kabinentrakt).</summary>
    public string? Location { get; set; }
    public List<BuildingSynergy> Synergies { get; set; } = new();
    /// <summary>Mehrfach baubar (z. B. Trainingsplätze, Imbissbuden).</summary>
    public bool Multiple { get; set; }
    /// <summary>Freies Bauland: keine Wirkung, verkaufbar.</summary>
    public bool Land { get; set; }
}

/// <summary>Bedingungen eines Zufallsereignisses.</summary>
public sealed class EventCondition
{
    public int? MinReputation { get; set; }
    public int? MaxReputation { get; set; }
    public List<int>? Months { get; set; }
    /// <summary>Betroffener Spieler: any, young, star, striker, keeper, injured, old, foreign.</summary>
    public string? Player { get; set; }
    public bool? AfterWin { get; set; }
    public bool? AfterLoss { get; set; }
    public double? MinFanMood { get; set; }
    public double? MaxFanMood { get; set; }
    public double? MaxBoardTrust { get; set; }
    public string? RequiresBuilding { get; set; }
    public bool? LowBalance { get; set; }
}

public sealed class EventOption
{
    public string Id { get; set; } = "";
    /// <summary>
    /// Wirkungen: money (EUR bei Reputation 10, skaliert mit Vereinsgröße), fans, board, morale, media, reputation,
    /// playerMorale, playerCondition, playerInjury (Tage), playerSuspend (Spiele), playerFine (Wochengehälter), pitch, attendance (%).
    /// </summary>
    public Dictionary<string, double> Effects { get; set; } = new();
    /// <summary>Erfolgswahrscheinlichkeit; bei Misserfolg gelten FailEffects.</summary>
    public double Chance { get; set; } = 1;
    public Dictionary<string, double>? FailEffects { get; set; }
}

/// <summary>Zufallsereignis (Kapitel 10) als Daten; Texte unter event.&lt;id&gt;.* in den Sprachdateien.</summary>
public sealed class EventDef
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public double Weight { get; set; } = 1;
    /// <summary>Leer = alle Tonfälle.</summary>
    public List<MediaStyle> Styles { get; set; } = new();
    public EventCondition When { get; set; } = new();
    public List<EventOption> Options { get; set; } = new();
    public int CooldownDays { get; set; } = 365;
}

/// <summary>Frage für Pressekonferenzen; Text unter press.q.&lt;key&gt;.</summary>
public sealed class PressQuestionDef
{
    public string Key { get; set; } = "";
    /// <summary>before, after</summary>
    public string When { get; set; } = "before";
    /// <summary>Bedingung: any, win, loss, draw, derby, streakWin, streakLoss, bigGame, injury, newSigning, favourite, rumour.</summary>
    public string Context { get; set; } = "any";
    public string Category { get; set; } = "general";
    public List<MediaStyle> Styles { get; set; } = new();
}

public sealed class SponsorCompany
{
    public string Name { get; set; } = "";
    public string Industry { get; set; } = "";
    /// <summary>Größe 1–20: ab welcher Vereinsgröße der Sponsor passt.</summary>
    public int Size { get; set; } = 10;
    /// <summary>Für Trikot, Ärmel, Ausrüster, Banden oder Stadionnamen geeignet.</summary>
    public List<SponsorSlot> Slots { get; set; } = new();
}

public sealed class SponsorList
{
    public string Country { get; set; } = "";
    public List<SponsorCompany> Companies { get; set; } = new();
}
