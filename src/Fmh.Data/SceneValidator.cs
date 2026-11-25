namespace Fmh.Core.Data;

/// <summary>
/// Prüft den Szenenkatalog des Textmodus (rules/scenes.json): eindeutige Ids, sinnvolle Werte und zu jeder Zeile
/// Text in der Standardsprache (lang/&lt;code&gt;.scenes.json). Fehlt eine Übersetzung in einer anderen Sprache,
/// erzählt das Spiel dort die einfache Kommentarzeile; das ist kein Fehler (der Editor markiert es).
/// </summary>
public static class SceneValidator
{
    public static List<string> Validate(GameData data)
    {
        var errors = new List<string>();
        var rules = data.Scenes;
        if (rules.AttackShare is < 0 or > 1)
            errors.Add($"Szenen: „attackShare“ muss zwischen 0 und 1 liegen ({rules.AttackShare}).");

        var all = rules.Scenes.Select(s => (s.Id, Rare: false, s.When, s.Steps))
            .Concat(rules.Rare.Select(r => (r.Id, Rare: true, r.When, r.Steps))).ToList();
        foreach (var dup in all.GroupBy(s => s.Id).Where(g => g.Count() > 1))
            errors.Add($"Szene '{dup.Key}' ist mehrfach definiert.");

        foreach (var scene in rules.Scenes)
        {
            if (scene.On.Count == 0) errors.Add($"Szene '{scene.Id}': keine Ereignisse („on“).");
            if (scene.Weight <= 0) errors.Add($"Szene '{scene.Id}': Gewicht muss positiv sein.");
        }
        foreach (var rare in rules.Rare.Where(r => r.Chance is < 0 or > 1))
            errors.Add($"Seltene Szene '{rare.Id}': Wahrscheinlichkeit muss zwischen 0 und 1 liegen.");

        foreach (var (id, _, when, steps) in all)
        {
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Szene ohne Id.");
            if (steps.Count == 0) errors.Add($"Szene '{id}': keine Zeilen.");
            CheckCondition(errors, id, when);
            foreach (var step in steps)
            {
                if (step.Chance is < 0 or > 1) errors.Add($"Szene '{id}': Zeile '{step.Text}' hat eine Wahrscheinlichkeit außerhalb von 0–1.");
                CheckCondition(errors, id, step.When);
                if (string.IsNullOrWhiteSpace(step.Text)) { errors.Add($"Szene '{id}': Zeile ohne Textschlüssel."); continue; }
                if (data.Languages.TryGetValue(data.DefaultLanguage, out var lang)
                    && (!lang.Commentary.TryGetValue(step.Text, out var variants) || variants.Count == 0))
                    errors.Add($"Szene '{id}': Text '{step.Text}' fehlt in der Standardsprache '{lang.Code}'.");
            }
        }
        return errors;
    }

    private static void CheckCondition(List<string> errors, string id, SceneCondition? c)
    {
        if (c is null) return;
        if (c.MinMinute > c.MaxMinute) errors.Add($"Szene '{id}': „minMinute“ liegt nach „maxMinute“.");
        if (c.MinXg > c.MaxXg) errors.Add($"Szene '{id}': „minXg“ liegt über „maxXg“.");
        if (c.MinRow > c.MaxRow) errors.Add($"Szene '{id}': „minRow“ liegt über „maxRow“.");
        if (c.MinMargin > c.MaxMargin) errors.Add($"Szene '{id}': „minMargin“ liegt über „maxMargin“.");
        foreach (var a in (c.Attrs ?? new()).Concat(c.KeeperAttrs ?? new()).Where(a => a.Min > a.Max))
            errors.Add($"Szene '{id}': Attribut {a.Attr} hat Mindestwert über Höchstwert.");
    }
}
