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
    /// <summary>Catering-Umsatz je Zuschauer und Heimspiel (DFL: Teil der sonstigen Erlöse).</summary>
    public long CateringPerSpectator { get; set; } = 5;
    /// <summary>Fanartikel-Umsatz je Zuschauer und Heimspiel, bevor Stimmung und Fanshop wirken.</summary>
    public long MerchPerSpectator { get; set; } = 10;
    /// <summary>Kreditrahmen der Hausbank in Prozent der Saisoneinnahmen.</summary>
    public int CreditLimitPercent { get; set; } = 15;
    /// <summary>Bausumme, ab der der Vorstand zustimmen muss.</summary>
    public long BoardApprovalAbove { get; set; } = 2_000_000;
    /// <summary>Bausumme, ab der eine Baugenehmigung der Stadt nötig ist.</summary>
    public long PermitAbove { get; set; } = 1_000_000;
    /// <summary>Dauerkarten-Verkauf bis (MM-dd).</summary>
    public string SeasonTicketDeadline { get; set; } = "07-31";
    /// <summary>Stichtage des Lizenzverfahrens (MM-dd): Antrag, Erstentscheidung, Nachweis der Auflagen.</summary>
    public string LicenseApplication { get; set; } = "03-15";
    public string LicenseDecision { get; set; } = "04-30";
    public string LicenseDeadline { get; set; } = "05-31";
    /// <summary>Punktabzug, wenn Auflagen bis zum Nachweisstichtag nicht erfüllt sind.</summary>
    public int ConditionPenalty { get; set; } = 3;
    /// <summary>Mindest-Zertifizierung des Nachwuchsleistungszentrums (Sterne) je Liga steht in DivisionFinance.YouthCenterRequired.</summary>
    public long StadiumCostPerPlace { get; set; } = 900;
    public long StadiumBaseCost { get; set; } = 4_000_000;
    public long StadiumCostPerBox { get; set; } = 60_000;
    public int StadiumBuildDays { get; set; } = 720;
}

/// <summary>Europäische Wettbewerbe (UEFA): Plätze, Lizenzkriterien, Financial Fair Play, Prämien.</summary>
public sealed class EuropeConfig
{
    /// <summary>Liga, deren Abschlusstabelle die Plätze vergibt.</summary>
    public string League { get; set; } = "";
    public List<EuropePlace> Places { get; set; } = new();
    /// <summary>Wettbewerb des Pokalsiegers (fällt an den nächsten Ligaplatz, wenn er schon qualifiziert ist).</summary>
    public string CupWinner { get; set; } = "";
    public string Cup { get; set; } = "";
    public List<EuropeCompetition> Competitions { get; set; } = new();
    public UefaLicenseRules License { get; set; } = new();
    public FfpRules Ffp { get; set; } = new();
}

public sealed class EuropePlace
{
    public string Competition { get; set; } = "";
    public int From { get; set; }
    public int To { get; set; }
}

public sealed class EuropeCompetition
{
    public string Id { get; set; } = "";
    /// <summary>Stärke des Teilnehmerfelds (Durchschnitt Top-11, Skala der Spielerbewertung).</summary>
    public double FieldStrength { get; set; } = 13;
    public long EntryPrize { get; set; }
    /// <summary>Runden ab der Ligaphase: Name (Textschlüssel), Datum (MM-dd), Prämie fürs Erreichen.</summary>
    public List<EuropeStage> Stages { get; set; } = new();
}

public sealed class EuropeStage
{
    public string Key { get; set; } = "";
    public string Date { get; set; } = "";
    public long Prize { get; set; }
}

public sealed class UefaLicenseRules
{
    public int MinSeats { get; set; } = 8000;
    public int Floodlight { get; set; } = 3;
    public int AcademyStars { get; set; } = 1;
}

public sealed class FfpRules
{
    /// <summary>Zulässiges Break-even-Defizit über drei Spielzeiten.</summary>
    public long AcceptableDeviation { get; set; } = 5_000_000;
    /// <summary>Ab diesem Defizit Ausschluss aus europäischen Wettbewerben.</summary>
    public long SevereDeviation { get; set; } = 30_000_000;
    /// <summary>Höchstquote Kaderkosten zu Umsatz.</summary>
    public double SquadCostRatio { get; set; } = 0.7;
    /// <summary>Geldstrafe in Prozent des Umsatzes.</summary>
    public double FinePercent { get; set; } = 3;
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
    /// <summary>Gehaltsniveau der Spielklasse gegenüber dem Profimarkt (1,0); Halbprofis verdienen deutlich weniger.</summary>
    public double WageLevel { get; set; } = 1.0;
    /// <summary>Sponsorenniveau der Spielklasse gegenüber der höchsten Liga (1,0).</summary>
    public double SponsorLevel { get; set; } = 1.0;
    /// <summary>
    /// Betriebskosten (Verwaltung, Spielbetrieb, Personal außerhalb des Kaders) als Anteil der laufenden Einnahmen;
    /// in der Bundesliga real rund 45 % (DFL-Wirtschaftsreport). 0 = keine.
    /// </summary>
    public double OperatingCostShare { get; set; }
    /// <summary>Höchstes Jahresgehalt, das ein Verein dieser Spielklasse zahlen kann (0 = ohne Grenze).</summary>
    public long MaxAnnualWage { get; set; }

    /// <summary>Höchstes Wochengehalt der Spielklasse (<see cref="int.MaxValue"/> ohne Grenze).</summary>
    public int MaxWeeklyWage => MaxAnnualWage > 0 ? (int)(MaxAnnualWage / 52) : int.MaxValue;

    /// <summary>Kalibrierungsziel: durchschnittliches Jahresgehalt eines Kaderspielers (0 = ohne Ziel).</summary>
    public long TargetAnnualWage { get; set; }

    /// <summary>
    /// Jahresbudget des Nachwuchsleistungszentrums als Vielfaches des Länder-Referenzbudgets (skaliert nach Vereinsgröße);
    /// 0 = bisherige Staffel nach Ligaebene.
    /// </summary>
    public double AcademyBudgetFactor { get; set; }

    /// <summary>Veröffentlichte Ligazahlen je Verein als Kalibrierungsziel (null = keine).</summary>
    public FinanceReference? Reference { get; set; }
}

/// <summary>
/// Durchschnittliche Erträge und Aufwendungen eines Vereins der Spielklasse aus einem Ligabericht, ohne Transfers.
/// Kategorien wie im DFL-Wirtschaftsreport; fehlende Kategorien werden nicht geprüft.
/// </summary>
public sealed class FinanceReference
{
    /// <summary>Quelle mit Saison, z. B. „DFL-Wirtschaftsreport 2025 (Saison 2023/24)“.</summary>
    public string Source { get; set; } = "";
    /// <summary>Amtliche Zahlen des Ligaverbands (true) oder Schätzung Dritter (false).</summary>
    public bool Official { get; set; }
    /// <summary>Erträge je Kategorie: matchday, advertising, media, merchandising, other und total.</summary>
    public Dictionary<string, long> Income { get; set; } = new();
    /// <summary>Aufwand je Kategorie: squad (Personal Spielbetrieb), operations (Verwaltung, Spielbetrieb, Sonstiges), youth und total.</summary>
    public Dictionary<string, long> Expenses { get; set; } = new();

    public static readonly string[] IncomeCategories = { "matchday", "advertising", "media", "merchandising", "other", "total" };
    public static readonly string[] ExpenseCategories = { "squad", "operations", "youth", "total" };

    /// <summary>
    /// DFL-Kategorie einer Buchungsart (Catering zählt wie im Wirtschaftsreport zu den sonstigen Erlösen);
    /// null für Transfers, Kredite, Bau, Zinsen, Strafen und Ereignisse, die der Vergleich auslässt.
    /// </summary>
    public static string? Category(TransactionType type) => type switch
    {
        TransactionType.Tickets or TransactionType.SeasonTickets => "matchday",
        TransactionType.Sponsorship => "advertising",
        TransactionType.Tv or TransactionType.European => "media",
        TransactionType.Merchandise => "merchandising",
        TransactionType.Catering or TransactionType.BuildingIncome or TransactionType.PrizeMoney => "other",
        TransactionType.Wages or TransactionType.StaffWages or TransactionType.Bonuses => "squad",
        TransactionType.Operations or TransactionType.Upkeep or TransactionType.Travel or TransactionType.TrainingCamp => "operations",
        TransactionType.Youth => "youth",
        _ => null,
    };
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
    /// <summary>Nur ohne laufendes Grundstücksangebot und ohne laufenden Stadionneubau.</summary>
    public bool? NoStadiumSite { get; set; }
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
