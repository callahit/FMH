using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Grauzonen (AP 13c): Schiedsrichter beeinflussen, verbotene Mittel, Vertragstrick.</summary>
public sealed partial class GameSession
{
    /// <summary>Grauzonen sind verfügbar (Paket geladen, Element an).</summary>
    public bool GrayZonesOn => Data.GrayZones is not null && Anstoss.IsOn(World, HumanManager.Id, GrayZones.Feature);

    public IReadOnlyList<GrayAction> GrayActions => GrayZonesOn ? Data.GrayZones!.Actions : Array.Empty<GrayAction>();

    /// <summary>Akte des Menschen (null, solange nichts geschehen ist).</summary>
    public GrayZoneFile? GrayZoneFile => World.GrayZones.GetValueOrDefault(HumanManager.Id);

    /// <summary>Spiel, auf das eine Spiel-Tat wirken würde.</summary>
    public Fixture? GrayZoneFixture => _grayZones.NextFixture();

    /// <summary>Tat ausführen (Vertragstrick mit laufender Verhandlung); Fehlertext-Schlüssel oder null.</summary>
    public string? UseGrayZone(string actionId, Negotiation? negotiation = null) => _grayZones.Use(actionId, negotiation);
}
