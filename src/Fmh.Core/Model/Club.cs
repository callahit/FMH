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
    public int? ManagerId { get; set; }
}
