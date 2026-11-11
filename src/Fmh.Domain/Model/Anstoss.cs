namespace Fmh.Core.Model;

/// <summary>
/// Anstoß-Schalter eines Managers (AP 13): Hauptschalter und einzeln abgeschaltete Elemente. Fehlt der Eintrag, ist
/// alles an (Voreinstellung bei neuem Spiel).
/// </summary>
public sealed class AnstossSettings
{
    /// <summary>Hauptschalter „Anstoß-Modus“.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Abgeschaltete Elemente (Kennung aus rules/anstoss.json, z. B. <c>satire</c>).</summary>
    public List<string> Disabled { get; set; } = new();
}
