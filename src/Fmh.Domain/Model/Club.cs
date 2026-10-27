using System.Text.Json.Serialization;

namespace Fmh.Core.Model;

/// <summary>
/// Verein mit vereinsweiten Daten. Mannschaftsbezogene Felder (Liga, Kader, Taktik, Training, Jugend, Beobachtungsliste)
/// gehören der <see cref="Team"/>; die gleichnamigen Eigenschaften hier leiten an die erste Mannschaft weiter,
/// bis alle Module direkt mit Mannschaften arbeiten.
/// </summary>
public sealed class Club
{
    private int _id;

    public int Id
    {
        get => _id;
        set
        {
            _id = value;
            FirstTeam.Id = value;
            FirstTeam.ClubId = value;
        }
    }

    /// <summary>Mannschaften des Vereins; die erste ist die Männer-Profimannschaft.</summary>
    [JsonIgnore]
    public List<Team> Teams { get; set; } = new() { new Team() };

    [JsonIgnore]
    public Team FirstTeam => Teams[0];

    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string City { get; set; } = "";
    public string CountryCode { get; set; } = "";
    /// <summary>Zweite Mannschaft: Id des Vereins der ersten Mannschaft (eigener Kader, eigene Finanzen).</summary>
    public int? ReserveOf { get; set; }
    /// <summary>Region und Koordinaten für die Staffelzuteilung geteilter Ligen.</summary>
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Reputation 1–20.</summary>
    public int Reputation { get; set; } = 10;
    /// <summary>Vereinsfarben als ConsoleColor-Namen (färben Kopfzeile und Tabellenmarkierung).</summary>
    public string PrimaryColor { get; set; } = "White";
    public string SecondaryColor { get; set; } = "Black";
    public string StadiumName { get; set; } = "";
    public int StadiumCapacity { get; set; } = 15000;
    public string LeagueId { get => FirstTeam.LeagueId; set => FirstTeam.LeagueId = value; }
    public List<int> PlayerIds { get => FirstTeam.PlayerIds; set => FirstTeam.PlayerIds = value; }
    public Tactic Tactic { get => FirstTeam.Tactic; set => FirstTeam.Tactic = value; }
    public List<Tactic> SavedTactics { get => FirstTeam.SavedTactics; set => FirstTeam.SavedTactics = value; }
    public Dictionary<string, double> Familiarity { get => FirstTeam.Familiarity; set => FirstTeam.Familiarity = value; }
    public TrainingPlan Training { get => FirstTeam.Training; set => FirstTeam.Training = value; }
    public TrainingCamp? Camp { get => FirstTeam.Camp; set => FirstTeam.Camp = value; }
    public int? ManagerId { get; set; }
    public ClubBudget Budget { get; set; } = new();
    /// <summary>Qualität des Trainerstabs 1–20 (bis zum Personalmodul in Phase 3 aus der Reputation).</summary>
    public int CoachQuality { get; set; } = 10;
    /// <summary>Jugendzentrum 1–20: Qualität des Jugendjahrgangs.</summary>
    public int YouthFacilities { get; set; } = 10;
    /// <summary>Rekrutierungsnetz 1–20: Zahl und Herkunft der Talente.</summary>
    public int YouthRecruitment { get; set; } = 10;
    public List<int> YouthIds { get => FirstTeam.YouthIds; set => FirstTeam.YouthIds = value; }
    public List<int> Shortlist { get => FirstTeam.Shortlist; set => FirstTeam.Shortlist = value; }

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
    public YouthAcademy Academy { get; set; } = new();
    public LicenseFile License { get; set; } = new();
}
