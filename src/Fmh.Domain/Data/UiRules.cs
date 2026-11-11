using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Bedienung (Datei <c>rules/ui.json</c>, AP 12b): Tastenbelegung, Schriftgrößen und Tutorial.</summary>
public sealed class UiRules
{
    /// <summary>Standardbelegung; eigene Belegungen liegen geräteweit in den Einstellungen.</summary>
    public List<KeyBindingRule> KeyBindings { get; set; } = new();
    /// <summary>Wählbare Skalierungen der Oberfläche (1 = normal).</summary>
    public List<double> FontScales { get; set; } = new() { 1.0 };
    /// <summary>Tutorial-Hinweise, je Seite einer beim ersten Öffnen.</summary>
    public List<TutorialStep> Tutorial { get; set; } = new();
}

/// <summary>Aktion mit Standardtaste, z. B. <c>continue</c> = <c>Space</c> oder <c>save</c> = <c>Ctrl+S</c>.</summary>
public sealed class KeyBindingRule
{
    /// <summary>Kennung der Aktion; Text unter <c>keys.&lt;action&gt;</c>. Seiten heißen <c>page.&lt;menü&gt;</c>.</summary>
    [Required]
    public string Action { get; set; } = "";
    /// <summary>Tastenkombination im Avalonia-Format (<c>F1</c>, <c>Ctrl+S</c>, <c>Space</c>).</summary>
    [Required]
    public string Gesture { get; set; } = "";
}

/// <summary>Tutorial-Hinweis; Texte unter <c>tutorial.&lt;id&gt;.title</c> und <c>tutorial.&lt;id&gt;.text</c>.</summary>
public sealed class TutorialStep
{
    [Required]
    public string Id { get; set; } = "";
    /// <summary>Menüpunkt der Seite (<c>inbox</c>, <c>squad</c>, …), auf der der Hinweis erscheint.</summary>
    [Required]
    public string Page { get; set; } = "";
}
