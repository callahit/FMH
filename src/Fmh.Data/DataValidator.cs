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
            if (country.Currency != CurrencyConfig.Base)
                errors.Add($"Land '{country.Code}': Beträge müssen in {CurrencyConfig.Base} angegeben sein (Leitwährung), nicht in {country.Currency}.");
        }
        if (data.Currencies.Rates.Any(r => r.PerEuro <= 0) || data.Currencies.PerEuro(CurrencyConfig.Base) != 1.0)
            errors.Add("Währungskurse: Kurse müssen positiv sein, der Euro hat den Kurs 1.");
        errors.AddRange(PyramidValidator.Validate(data));
        bool KnownDivision(string id) =>
            data.Countries.SelectMany(x => x.Divisions).Any(d => d.Id == id || d.Groups.Any(g => g.Id == id))
            || data.Countries.SelectMany(x => x.Pools).Any(p => p.Id == id);
        foreach (var club in data.Clubs.Where(c => !KnownDivision(c.Division)))
            errors.Add($"Verein '{club.Name}' gehört zu unbekannter Liga '{club.Division}'.");

        foreach (var country in data.Countries)
        {
            foreach (var (div, fin) in country.Finance.Divisions.Where(kv => kv.Value.Reference is not null))
            {
                var r = fin.Reference!;
                foreach (var key in r.Income.Keys.Except(FinanceReference.IncomeCategories).Concat(r.Expenses.Keys.Except(FinanceReference.ExpenseCategories)))
                    errors.Add($"Ligazahlen '{div}': unbekannte Kategorie '{key}'.");
                if (string.IsNullOrWhiteSpace(r.Source) || r.Income.Values.Concat(r.Expenses.Values).Any(v => v <= 0))
                    errors.Add($"Ligazahlen '{div}': Quelle fehlt oder Betrag nicht positiv.");
            }
            foreach (var (div, r) in country.Registration)
            {
                if (!KnownDivision(div)) errors.Add($"Meldeliste: unbekannte Liga '{div}'.");
                var cap = r.MaxPlayers > 0 ? r.MaxPlayers : country.Squad.MaxSquad;
                if (r.MinLocal > cap || r.MinClubTrained > r.MinLocal || r.MinNationals > cap)
                    errors.Add($"Meldeliste '{div}': Mindestzahlen passen nicht zu {cap} Plätzen.");
            }
            foreach (var pool in country.Pools)
            {
                var count = data.Clubs.Count(c => c.Division == pool.Id);
                if (count != pool.Teams) errors.Add($"Pool '{pool.Name}' braucht {pool.Teams} Vereine, Daten enthalten {count}.");
            }
            foreach (var cup in country.Cups)
            {
                foreach (var id in cup.Participants.Concat(cup.Entries.SelectMany(e => e.Participants)).Where(id => !KnownDivision(id)))
                    errors.Add($"Pokal '{cup.Name}': unbekannte Teilnehmerliga '{id}'.");
                foreach (var q in cup.Entries.Select(e => e.QualifiersOf).Where(q => q.Length > 0 && country.Cups.All(c => c.Id != q)))
                    errors.Add($"Pokal '{cup.Name}': Startplätze aus unbekanntem Pokal '{q}'.");
                // Mehr Bewerber als Plätze sind erlaubt: Der Pokal nimmt die höheren Ebenen zuerst (CupEntries.Resolve).
                // Mit späterem Einstieg ergibt sich die Rundenzahl erst aus den Teilnehmern; dann muss es je Einstiegsrunde Termine geben.
                var rounds = cup.Entries.Any(e => e.Round > 1) ? cup.Entries.Max(e => e.Round) + 1 : (int)Math.Ceiling(Math.Log2(Math.Max(2, cup.Teams)));
                if (cup.Dates.Count < rounds) errors.Add($"Pokal '{cup.Name}': {rounds} Runden, aber nur {cup.Dates.Count} Termine.");
                if (cup.FixedPairing && cup.HomeEntry is < 0 or > 1) errors.Add($"Pokal '{cup.Name}': Heimrecht nur für Eintrag 0 oder 1.");
            }
        }

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
