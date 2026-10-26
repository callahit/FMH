using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>Alle aus Datenpaketen geladenen Inhalte. Es gibt keine fest einprogrammierten Vereine, Ligen oder Regeln.</summary>
public sealed class GameData
{
    public List<PackageManifest> Packages { get; init; } = new();
    public List<CountryConfig> Countries { get; init; } = new();
    public List<ClubDefinition> Clubs { get; init; } = new();
    public Dictionary<string, NameList> Names { get; init; } = new();
    public List<Formation> Formations { get; init; } = new();
    public PositionProfiles PositionProfiles { get; init; } = new();
    public List<InjuryType> Injuries { get; init; } = new();
    public List<RoleDef> Roles { get; init; } = new();
    public EngineConfig Engine { get; init; } = new();
    public Texts Texts { get; init; } = new(new(), "de");
    public Dictionary<string, List<string>> Commentary { get; init; } = new();

    public Formation Formation(string id) =>
        Formations.FirstOrDefault(f => f.Id == id) ?? Formations[0];

    public RoleDef? Role(string id) => Roles.FirstOrDefault(r => r.Id == id);

    /// <summary>Standardrolle einer Position: die erste Rolle, die die Position erlaubt.</summary>
    public RoleDef? DefaultRole(Position pos) => Roles.FirstOrDefault(r => r.Positions.Contains(pos));

    public InjuryType Injury(string key) =>
        Injuries.FirstOrDefault(i => i.Key == key) ?? new InjuryType { Key = key, MinDays = 7, MaxDays = 14 };
}
