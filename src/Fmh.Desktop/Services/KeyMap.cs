using Avalonia.Input;
using Fmh.Core.Data;

namespace Fmh.Desktop.Services;

/// <summary>
/// Tastenbelegung (AP 12b): Standard aus <c>rules/ui.json</c>, eigene Belegungen geräteweit in den Einstellungen.
/// Eine Taste gehört immer nur einer Aktion; wird sie neu vergeben, tauschen die beiden Aktionen ihre Tasten.
/// </summary>
public sealed class KeyMap
{
    private readonly Dictionary<string, KeyGesture> _byAction;

    private KeyMap(Dictionary<string, KeyGesture> byAction) => _byAction = byAction;

    /// <summary>Aktionen in der Reihenfolge der Daten.</summary>
    public IReadOnlyList<string> Actions { get; private init; } = Array.Empty<string>();

    public static KeyMap Build(UiRules rules, AppSettings settings)
    {
        var map = new Dictionary<string, KeyGesture>();
        foreach (var rule in rules.KeyBindings)
        {
            var own = settings.KeyBindings.TryGetValue(rule.Action, out var g) && Parse(g) is { } parsed && !IsReserved(parsed) ? parsed : null;
            if ((own ?? Parse(rule.Gesture)) is { } gesture) map[rule.Action] = gesture;
        }
        return new KeyMap(map) { Actions = rules.KeyBindings.Select(r => r.Action).Distinct().ToList() };
    }

    public static KeyMap Current => Build(AppState.Current.Data.Ui, AppState.Current.Settings);

    public static KeyGesture? Parse(string text)
    {
        try { return string.IsNullOrWhiteSpace(text) ? null : KeyGesture.Parse(text); }
        catch (ArgumentException) { return null; }
    }

    public KeyGesture? GestureOf(string action) => _byAction.TryGetValue(action, out var g) ? g : null;

    /// <summary>Aktion zur gedrückten Taste; Umschalt, Strg und Alt müssen genau passen.</summary>
    public string? ActionFor(Key key, KeyModifiers modifiers)
    {
        modifiers &= KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;
        foreach (var action in Actions)
            if (_byAction.TryGetValue(action, out var g) && g.Key == key && g.KeyModifiers == modifiers) return action;
        return null;
    }

    /// <summary>Nur Umschalt, Strg, Alt oder Systemtaste: noch keine vollständige Belegung.</summary>
    public static bool IsModifierOnly(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None;

    /// <summary>
    /// Für Eingabefelder, Listen und Seiten reserviert: Enter, Tab und Pfeiltasten ohne Zusatztaste. Sonst würde z. B.
    /// Enter im Suchfeld weiterspielen oder ein Pfeil auf der Taktikseite speichern.
    /// </summary>
    public static bool IsReserved(KeyGesture gesture) => gesture.KeyModifiers == KeyModifiers.None
        && gesture.Key is Key.Enter or Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right;

    /// <summary>
    /// Belegt <paramref name="action"/> neu und speichert die Abweichungen vom Standard. Hatte eine andere Aktion die
    /// Taste, bekommt sie die bisherige Taste von <paramref name="action"/>; deren Kennung wird zurückgegeben.
    /// </summary>
    public static string? Rebind(UiRules rules, AppSettings settings, string action, KeyGesture gesture)
    {
        var map = Build(rules, settings);
        if (!map.Actions.Contains(action)) throw new ArgumentException("Unbekannte Aktion: " + action, nameof(action));
        if (IsReserved(gesture)) throw new ArgumentException("Reservierte Taste: " + gesture, nameof(gesture));
        var old = map.GestureOf(action);
        var other = map.ActionFor(gesture.Key, gesture.KeyModifiers);
        if (other == action) return null;
        Store(rules, settings, action, gesture);
        if (other is not null && old is not null) Store(rules, settings, other, old);
        return other;
    }

    private static void Store(UiRules rules, AppSettings settings, string action, KeyGesture gesture)
    {
        var text = gesture.ToString();
        var standard = rules.KeyBindings.FirstOrDefault(r => r.Action == action)?.Gesture;
        if (standard is not null && Parse(standard) is { } s && s.Equals(gesture)) settings.KeyBindings.Remove(action);
        else settings.KeyBindings[action] = text;
    }

    /// <summary>Taste für die Anzeige in der aktuellen Sprache, z. B. "Strg+S" oder "Leertaste".</summary>
    public static string Display(KeyGesture? gesture, Texts t) => gesture is null ? "–"
        : string.Join("+", gesture.ToString().Split('+').Select(part => t.TryGet("keys.name." + part) ?? part));

    public string Display(string action, Texts t) => Display(GestureOf(action), t);
}
