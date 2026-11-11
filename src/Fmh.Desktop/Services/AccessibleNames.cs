using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace Fmh.Desktop.Services;

/// <summary>
/// Namen für Screenreader (AP 12b): Knöpfe ohne reinen Text als Inhalt (Symbol, Wappen, mehrere Zeilen) bekommen
/// beim Laden einen Namen aus dem Tooltip oder aus ihren Texten, sofern im XAML keiner gesetzt ist.
/// </summary>
public static class AccessibleNames
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Control.LoadedEvent.AddClassHandler<Button>((b, _) => Fill(b));
        Control.LoadedEvent.AddClassHandler<ToggleButton>((b, _) => Fill(b));
    }

    public static void Fill(ContentControl control)
    {
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(control)) || control.Content is string) return;
        if (Derive(control) is { Length: > 0 } name) AutomationProperties.SetName(control, name);
    }

    /// <summary>Name aus Tooltip, sonst aus den Texten im Inhalt.</summary>
    public static string? Derive(ContentControl control)
    {
        if (ToolTip.GetTip(control) is string tip && tip.Length > 0) return tip;
        var texts = control.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t));
        var name = string.Join(" ", texts);
        return name.Length > 0 ? name : null;
    }
}
