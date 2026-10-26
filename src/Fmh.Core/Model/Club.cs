namespace Fmh.Core.Model;

public sealed class Club
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string City { get; set; } = "";
    public string CountryCode { get; set; } = "";
    /// <summary>Reputation 1–20.</summary>
    public int Reputation { get; set; } = 10;
    /// <summary>Vereinsfarben als ConsoleColor-Namen (färben Kopfzeile und Tabellenmarkierung).</summary>
    public string PrimaryColor { get; set; } = "White";
    public string SecondaryColor { get; set; } = "Black";
    public string StadiumName { get; set; } = "";
    public int StadiumCapacity { get; set; } = 15000;
    public string LeagueId { get; set; } = "";
    public List<int> PlayerIds { get; set; } = new();
    public Tactic Tactic { get; set; } = new();
    /// <summary>Bis zu drei gespeicherte Taktikvarianten, im Spiel umschaltbar.</summary>
    public List<Tactic> SavedTactics { get; set; } = new();
    /// <summary>Taktikvertrautheit 0–100 je Formation; steigt durch Taktiktraining und Spiele.</summary>
    public Dictionary<string, double> Familiarity { get; set; } = new();
    public TrainingPlan Training { get; set; } = new();
    public TrainingCamp? Camp { get; set; }
    public int? ManagerId { get; set; }
    public ClubBudget Budget { get; set; } = new();
    /// <summary>Qualität des Trainerstabs 1–20 (bis zum Personalmodul in Phase 3 aus der Reputation).</summary>
    public int CoachQuality { get; set; } = 10;
    /// <summary>Jugendzentrum 1–20: Qualität des Jugendjahrgangs.</summary>
    public int YouthFacilities { get; set; } = 10;
    /// <summary>Rekrutierungsnetz 1–20: Zahl und Herkunft der Talente.</summary>
    public int YouthRecruitment { get; set; } = 10;
    public List<int> YouthIds { get; set; } = new();
    /// <summary>Beobachtungsliste des Managers.</summary>
    public List<int> Shortlist { get; set; } = new();

    // Phase 3: Wirtschaft und Umfeld
    public FinanceState Finance { get; set; } = new();
    public Stadium Stadium { get; set; } = new();
    public Grounds Grounds { get; set; } = new();
    public List<SponsorDeal> Sponsors { get; set; } = new();
    public List<SponsorDeal> SponsorOffers { get; set; } = new();
    public BoardState Board { get; set; } = new();
    public FanState Fans { get; set; } = new();
    /// <summary>Lokalrivalen (Club-Ids) für Derbys.</summary>
    public List<int> Rivals { get; set; } = new();
}
