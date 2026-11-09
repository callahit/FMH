using System.Text;
using System.Text.Json.Nodes;
using Fmh.Core.Data.Schema;

namespace Fmh.Core.Data.Editing;

/// <summary>
/// CSV für Listen-Dateien mit Schema (Spieler, Personal, Manager, Schiedsrichter): Semikolon, Kopfzeile mit Feldnamen,
/// Werte im Typ des Schemas. Felder, die nicht als Spalte vorkommen, bleiben beim Import erhalten, wenn der
/// Schlüssel einer Zeile schon existiert.
/// </summary>
public static class TableCsv
{
    public static string Export(JsonArray rows, IReadOnlyList<string> columns)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', columns)).Append('\n');
        foreach (var row in rows.OfType<JsonObject>())
            sb.Append(string.Join(';', columns.Select(c => ClubCsv.Quote(row[c] is JsonValue v ? ClubCsv.Format(v) : "")))).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Liest Zeilen; <paramref name="itemSchema"/> bestimmt die Typen (aufgelöst gegen <paramref name="root"/>).
    /// <paramref name="existing"/> liefert bisherige Einträge, deren übrige Felder (z. B. Attribute) erhalten bleiben.
    /// </summary>
    public static (JsonArray rows, List<string> errors) Import(string csv, JsonObject root, string keyField, JsonArray? existing = null)
    {
        var table = ClubCsv.Parse(csv);
        var errors = new List<string>();
        var result = new JsonArray();
        if (table.Count == 0) return (result, new List<string> { "CSV ist leer." });
        var header = table[0].Select(h => h.Trim()).ToList();
        if (!header.Contains(keyField)) errors.Add($"CSV braucht die Spalte „{keyField}“.");
        var before = (existing ?? new JsonArray()).OfType<JsonObject>()
            .Where(o => o[keyField] is JsonValue).GroupBy(o => o[keyField]!.ToString()).ToDictionary(g => g.Key, g => g.First());
        for (var r = 1; r < table.Count; r++)
        {
            var cells = table[r];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var key = header.IndexOf(keyField) is var k and >= 0 && k < cells.Count ? cells[k].Trim() : "";
            var item = before.TryGetValue(key, out var old) ? (JsonObject)old.DeepClone() : new JsonObject();
            for (var c = 0; c < header.Count && c < cells.Count; c++)
            {
                var col = header[c];
                var field = SchemaNavigator.At(root, JsonPath.Root.Then(0).Then(col));
                if (field is null) { errors.Add($"Zeile {r + 1}: unbekannte Spalte „{col}“."); continue; }
                if (!SchemaNavigator.TryParse(field, cells[c], out var value)) { errors.Add($"Zeile {r + 1}: „{col}“ ungültig ({cells[c].Trim()})."); continue; }
                if (value is null) item.Remove(col); else item[col] = value;
            }
            if (key.Length == 0) errors.Add($"Zeile {r + 1}: „{keyField}“ fehlt.");
            result.Add(item);
        }
        return (result, errors.Distinct().ToList());
    }
}
