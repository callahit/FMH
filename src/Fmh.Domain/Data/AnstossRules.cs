using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>
/// Anstoß-Elemente (Datei <c>rules/anstoss.json</c>, AP 13), geliefert vom Paket <c>anstoss</c>. Inhalte tragen das
/// Kennzeichen <c>"feature": "anstoss.&lt;element&gt;"</c>; ausgeschaltete Elemente werden weder ausgelöst noch angezeigt.
/// </summary>
public sealed class AnstossRules
{
    public List<AnstossElement> Elements { get; set; } = new();
}

/// <summary>Einzeln abschaltbares Element; Texte unter <c>anstoss.&lt;id&gt;.name</c> und <c>anstoss.&lt;id&gt;.info</c>.</summary>
public sealed class AnstossElement
{
    [Required]
    public string Id { get; set; } = "";
}
