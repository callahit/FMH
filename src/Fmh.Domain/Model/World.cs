using Fmh.Core.Data;

namespace Fmh.Core.Model;

/// <summary>Der komplette Spielstand.</summary>
public sealed class World
{
    public const int CurrentSaveVersion = 1;

    public int SaveVersion { get; set; } = CurrentSaveVersion;
    public ulong Seed { get; set; }
    public ulong[] RngState { get; set; } = Array.Empty<ulong>();
    public DateOnly Date { get; set; }
    public int SeasonYear { get; set; }
    /// <summary>
    /// Marktniveau je Spielklasse (Division bzw. Pool): Ø Stärke der besten elf je Verein zum letzten Saisonstart und zum
    /// Spielstart (<see cref="MarketBases"/>). Gehälter und Marktwerte richten sich nach dem Abstand zum Marktniveau der
    /// Spielklasse, nicht nach der absoluten Stärke; so bleibt das Gehaltsgefüge stabil, wenn Kader stärker werden.
    /// </summary>
    public Dictionary<string, double> MarketLevels { get; set; } = new();
    public Dictionary<string, double> MarketBases { get; set; } = new();
    public WorldRules Rules { get; set; } = new();
    /// <summary>Datenpakete, aus denen der Spielstand erzeugt wurde (Id und Version).</summary>
    public List<string> Packages { get; set; } = new();
    /// <summary>Kopie der Länderkonfigurationen, damit ein Spielstand unabhängig von Paket-Updates bleibt.</summary>
    public List<CountryConfig> Countries { get; set; } = new();

    public Dictionary<int, Player> Players { get; set; } = new();
    public Dictionary<int, Club> Clubs { get; set; } = new();
    public List<League> Leagues { get; set; } = new();
    public List<Referee> Referees { get; set; } = new();
    public List<Manager> Managers { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    public List<SeasonRecord> History { get; set; } = new();
    public List<Cup> Cups { get; set; } = new();
    public List<Negotiation> Negotiations { get; set; } = new();
    public List<TransferRecord> Transfers { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<Installment> Installments { get; set; } = new();
    /// <summary>Laufende erfolgsabhängige Zahlungen an abgebende Vereine.</summary>
    public List<TransferCondition> TransferConditions { get; set; } = new();
    /// <summary>Verträge mit Id und Historie (laufend und abgelöst). Der Spieler trägt zusätzlich den aktuellen Vertrag.</summary>
    public List<ContractRecord> ContractHistory { get; set; } = new();
    public List<Scout> Scouts { get; set; } = new();
    public List<ScoutAssignment> ScoutAssignments { get; set; } = new();
    public List<ScoutReport> ScoutReports { get; set; } = new();
    /// <summary>Kenntnisstand des menschlichen Managers je Spieler-Id (0–100).</summary>
    public Dictionary<int, int> Knowledge { get; set; } = new();
    /// <summary>Gesperrte Verhandlungen "Spieler:Verein" bis Datum (nach 3 gescheiterten Runden 14 Tage).</summary>
    public Dictionary<string, DateOnly> NegotiationLocks { get; set; } = new();
    public List<StaffMember> Staff { get; set; } = new();
    public List<PendingEvent> Events { get; set; } = new();
    /// <summary>Zuletzt ausgelöste Ereignisse je Ereignis-Id (gegen Wiederholungen).</summary>
    public Dictionary<string, DateOnly> EventHistory { get; set; } = new();
    public PressConference? PendingPress { get; set; }
    /// <summary>Teilnehmer an europäischen Wettbewerben (laufende und kommende Saison).</summary>
    public List<EuropeanEntry> EuropeanEntries { get; set; } = new();
    /// <summary>Länderkoeffizienten (Fünfjahresfenster, laufende Summe; AP 5).</summary>
    public Dictionary<string, double> CountryCoefficients { get; set; } = new();
    /// <summary>Vereinskoeffizienten für Setzliste und Einstieg (AP 5).</summary>
    public Dictionary<int, double> ClubCoefficients { get; set; } = new();

    /// <summary>Alle Ligen der laufenden Saison sind beendet und ausgewertet; der Wechsel folgt zum Saisonstart.</summary>
    public bool SeasonFinished { get; set; }
    public int AutosaveCounter { get; set; }

    public int NextPlayerId { get; set; } = 1;
    public int NextFixtureId { get; set; } = 1;
    public int NextMessageId { get; set; } = 1;
    public int NextTieId { get; set; } = 1;
    public int NextNegotiationId { get; set; } = 1;
    public int NextScoutId { get; set; } = 1;
    public int NextAssignmentId { get; set; } = 1;
    public int NextStaffId { get; set; } = 1;
    public int NextBuildingId { get; set; } = 1;
    public int NextProjectId { get; set; } = 1;
    public int NextSponsorId { get; set; } = 1;
    public int NextLoanId { get; set; } = 1;
    public int NextEventId { get; set; } = 1;
    public int NextDealId { get; set; } = 1;
    public int NextContractId { get; set; } = 1;

    public League LeagueOf(Club club) => Leagues.First(l => l.Id == club.LeagueId);

    public League? FindLeague(string id) => Leagues.FirstOrDefault(l => l.Id == id);

    public Cup? FindCup(string id) => Cups.FirstOrDefault(c => c.Id == id);

    /// <summary>Name eines Wettbewerbs (Liga, Pokal, Relegation).</summary>
    public string CompetitionName(string id) => FindLeague(id)?.Name ?? FindCup(id)?.Name ?? id;

    public IEnumerable<Fixture> AllFixtures => Leagues.SelectMany(l => l.Fixtures).Concat(Cups.SelectMany(c => c.Fixtures));

    public League League(string id) => Leagues.First(l => l.Id == id);

    public IEnumerable<Player> SquadOf(Club club) => club.PlayerIds.Select(id => Players[id]);

    public CountryConfig Country(string code) => Countries.First(c => c.Code == code);
}

/// <summary>Weltregeln gelten für den ganzen Spielstand und alle Manager gleich (Kapitel 2).</summary>
public sealed class WorldRules
{
    /// <summary>Multiplikator der Verletzungshäufigkeit (0,5 = selten, 1 = normal, 2 = oft).</summary>
    public double InjuryFrequency { get; set; } = 1.0;
    public bool Ironman { get; set; }
    public int AutosaveSlots { get; set; } = 3;
}

public enum AttributeDisplay { Numbers, Bars, Stars, StrengthOnly }
/// <summary>Spieldarstellung; Konferenz zeigt die übrigen Spiele des Tages parallel (Kapitel 6).</summary>
public enum MatchDisplayMode { Commentary, Ticker, Instant, Conference, Scene }
public enum WageDisplay { Week, Month, Year }

/// <summary>Persönliche Einstellungen eines menschlichen Managers (Kapitel 2).</summary>
public sealed class ManagerProfile
{
    public WageDisplay WageDisplay { get; set; } = WageDisplay.Month;
    public AttributeDisplay AttributeDisplay { get; set; } = AttributeDisplay.Numbers;
    public MatchDisplayMode MatchDisplay { get; set; } = MatchDisplayMode.Commentary;
    /// <summary>Spieltempo 1 ("Echtzeit-Feeling") bis 5 ("Blitz").</summary>
    public int MatchSpeed { get; set; } = 3;
    public bool PauseOnGoals { get; set; } = true;
    public bool PauseOnRedCards { get; set; } = true;
    public string Theme { get; set; } = "Klassik";
    public string Language { get; set; } = "de";
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public EventFrequency Events { get; set; } = EventFrequency.Normal;
    public MediaStyle Style { get; set; } = MediaStyle.Balanced;
    /// <summary>Pressekonferenzen an den Pressesprecher abgeben.</summary>
    public bool DelegatePress { get; set; }
    /// <summary>Anzeigewährung (gerechnet wird in Euro) und eigene Kurse je Währung (Einheiten je Euro), leer = Standardkurse.</summary>
    public string DisplayCurrency { get; set; } = "EUR";
    public Dictionary<string, double> ExchangeRates { get; set; } = new();
}

public sealed class Manager
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Human { get; set; }
    public int? ClubId { get; set; }
    public int Reputation { get; set; } = 5;
    public ManagerProfile Profile { get; set; } = new();
    public ManagerCareer Career { get; set; } = new();
}

public enum MessageType { Info, Board, Medical, Discipline, Result, Season, Transfer, Contract, Scouting, Youth, Training, Cup, Finance, Stadium, Fans, Press, Event, Career }

/// <summary>Aktion, die eine Nachricht auslöst (z. B. Transferangebot annehmen oder ablehnen).</summary>
public enum MessageAction { None, IncomingOffer, ContractExpiring, ScoutReport, Negotiation, YouthIntake, Event, SponsorOffer, PressConference, SeasonGoal, JobOffer, Construction, Academy, License }

public sealed class Message
{
    public int Id { get; set; }
    public int ManagerId { get; set; }
    public DateOnly Date { get; set; }
    public MessageType Type { get; set; }
    /// <summary>Wörtliche Texte aus Spielständen vor der Mehrsprachigkeit; neue Nachrichten nutzen die Verweise.</summary>
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Sprachneutrale Texte, die bei jeder Anzeige in der gewählten Sprache aufgebaut werden.</summary>
    public string? SenderKey { get; set; }
    public Data.TextRef? SubjectRef { get; set; }
    public Data.TextRef? BodyRef { get; set; }

    public string SenderText(Data.Texts t) => SenderKey is { Length: > 0 } k ? t[k] : Sender;
    public string SubjectText(Data.Texts t) => SubjectRef is { } r ? t.Render(r) : Subject;
    public string BodyText(Data.Texts t) => BodyRef is { } r ? t.Render(r) : Body;
    public bool Read { get; set; }
    public MessageAction Action { get; set; }
    public int? ActionRefId { get; set; }
    /// <summary>Pflicht-Entscheidung: blockiert das Weiterspielen, bis sie erledigt ist (Kapitel 3).</summary>
    public bool Required { get; set; }
    public bool Resolved { get; set; }
}

public sealed class SeasonRecord
{
    public int SeasonYear { get; set; }
    public string LeagueId { get; set; } = "";
    public string LeagueName { get; set; } = "";
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = "";
    public List<int> FinalOrder { get; set; } = new();
}
