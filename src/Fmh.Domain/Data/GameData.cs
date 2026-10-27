using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>Eine Sprache: Texte der Oberfläche (mit Rückfallsprache) und Kommentarbausteine.</summary>
public sealed record LanguagePack(string Code, Texts Texts, Dictionary<string, List<string>> Commentary)
{
    public string Name => Texts.Name;
}

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
    public List<BuildingDef> Buildings { get; init; } = new();
    public List<EventDef> Events { get; init; } = new();
    public List<PressQuestionDef> PressQuestions { get; init; } = new();
    public Dictionary<string, SponsorList> Sponsors { get; init; } = new();
    /// <summary>Anzeigekurse; gerechnet wird immer in Euro.</summary>
    public CurrencyConfig Currencies { get; init; } = new();

    public BuildingDef? Building(string id) => Buildings.FirstOrDefault(b => b.Id == id);
    /// <summary>Alle verfügbaren Sprachen (Code → Texte und Kommentarbausteine).</summary>
    public Dictionary<string, LanguagePack> Languages { get; init; } = new();
    /// <summary>Rückfallsprache: fehlende Texte anderer Sprachen kommen von hier.</summary>
    public string DefaultLanguage { get; init; } = "de";

    /// <summary>Texte und Kommentar der aktuell gewählten Sprache.</summary>
    public Texts Texts { get; set; } = new(new(), "de");
    public Dictionary<string, List<string>> Commentary { get; set; } = new();
    public string Language => Texts.Language;

    /// <summary>Wechselt die Sprache (Texte, Kommentar, Zahlen- und Datumsformate). Unbekannte Codes fallen auf die Standardsprache zurück.</summary>
    public void UseLanguage(string code)
    {
        if (!Languages.TryGetValue(code, out var pack) && !Languages.TryGetValue(DefaultLanguage, out pack)) return;
        Texts = pack.Texts;
        Commentary = pack.Commentary;
        Fmt.Use(Texts);
    }

    public Formation Formation(string id) =>
        Formations.FirstOrDefault(f => f.Id == id) ?? Formations[0];

    public RoleDef? Role(string id) => Roles.FirstOrDefault(r => r.Id == id);

    /// <summary>Standardrolle einer Position: die erste Rolle, die die Position erlaubt.</summary>
    public RoleDef? DefaultRole(Position pos) => Roles.FirstOrDefault(r => r.Positions.Contains(pos));

    public InjuryType Injury(string key) =>
        Injuries.FirstOrDefault(i => i.Key == key) ?? new InjuryType { Key = key, MinDays = 7, MaxDays = 14 };
}
