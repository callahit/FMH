using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>Prüft Datenpakete auf Widersprüche und liefert eine Fehlerliste (Kapitel 12, Editor-Validierung).</summary>
public static class DataValidator
{
    public static List<string> Validate(GameData data)
    {
        var errors = new List<string>();

        if (data.Formations.Count == 0) errors.Add("Keine Formationen definiert.");
        foreach (var f in data.Formations)
        {
            if (f.Slots.Count != 11) errors.Add($"Formation '{f.Id}' hat {f.Slots.Count} statt 11 Positionen.");
            if (f.Slots.Count(s => s.Position == Position.GK) != 1) errors.Add($"Formation '{f.Id}' braucht genau einen Torwart.");
            if (f.Slots.Any(s => s.Col < 0 || s.Col >= Grid.Cols || s.Row < 0 || s.Row >= Grid.Rows))
                errors.Add($"Formation '{f.Id}' hat Positionen außerhalb des 5x7-Rasters.");
        }
        foreach (var dup in data.Formations.GroupBy(f => f.Id).Where(g => g.Count() > 1))
            errors.Add($"Formation '{dup.Key}' ist mehrfach definiert.");

        foreach (var dup in data.Clubs.GroupBy(c => c.Key).Where(g => g.Count() > 1))
            errors.Add($"Vereinsschlüssel '{dup.Key}' ist mehrfach vergeben.");

        foreach (var country in data.Countries)
        {
            if (!data.Names.ContainsKey(country.Code))
                errors.Add($"Land '{country.Code}': keine Namensliste vorhanden.");
            var levels = country.Levels.OrderBy(l => l.Level).ToList();
            foreach (var div in country.Divisions)
            {
                var clubs = data.Clubs.Count(c => c.Division == div.Id);
                if (clubs != div.Teams)
                    errors.Add($"Liga '{div.Name}' hat {clubs} statt {div.Teams} Teams.");
                if (div.Teams is < 2 or > 24) errors.Add($"Liga '{div.Name}': Teamzahl {div.Teams} liegt außerhalb von 2–24.");
                if (div.Rounds is < 1 or > 4) errors.Add($"Liga '{div.Name}': {div.Rounds} Runden (erlaubt 1–4).");
                if (div.Promotion + div.Relegation >= div.Teams) errors.Add($"Liga '{div.Name}': zu viele Auf- und Abstiegsplätze.");
            }
            for (var i = 0; i < levels.Count - 1; i++)
            {
                var down = levels[i].Divisions.Sum(d => d.Relegation);
                var up = levels[i + 1].Divisions.Sum(d => d.Promotion);
                if (down != up)
                    errors.Add($"Land '{country.Code}': {down} Absteiger aus Ebene {levels[i].Level}, aber {up} Aufsteiger aus Ebene {levels[i + 1].Level}.");
            }
            if (levels.Count > 0 && levels[0].Divisions.Any(d => d.Promotion > 0))
                errors.Add($"Land '{country.Code}': die oberste Ebene kann keine Aufsteiger haben.");
        }
        foreach (var club in data.Clubs.Where(c => !data.Countries.SelectMany(x => x.Divisions).Any(d => d.Id == c.Division)))
            errors.Add($"Verein '{club.Name}' gehört zu unbekannter Liga '{club.Division}'.");

        foreach (var pos in PositionInfo.All.Where(p => !data.PositionProfiles.Weights.ContainsKey(p)))
            errors.Add($"Positionsprofil für '{pos}' fehlt.");
        foreach (var inj in data.Injuries.Where(i => i.MinDays <= 0 || i.MaxDays < i.MinDays))
            errors.Add($"Verletzung '{inj.Key}': ungültige Dauer {inj.MinDays}–{inj.MaxDays} Tage.");

        var e = data.Engine;
        if (e.PassBaseByRow.Length != Grid.Rows || e.XgCentralByRow.Length != Grid.Rows || e.XgWideByRow.Length != Grid.Rows)
            errors.Add("Engine-Konfiguration: Reihenwerte brauchen je 7 Einträge.");
        if (e.ActionWeights.Length != Grid.Rows || e.ActionWeights.Any(w => w.Length != 4))
            errors.Add("Engine-Konfiguration: Aktionsgewichte brauchen 7 Reihen mit je 4 Werten.");

        return errors;
    }
}
