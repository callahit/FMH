using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

/// <summary>
/// Vereine als CSV für Tabellenkalkulationen (Umsetzungskonzept 7.3): Semikolon getrennt, erste Zeile mit
/// Spaltennamen, Werte mit Semikolon, Anführungszeichen oder Zeilenumbruch in Anführungszeichen.
/// Leere Zellen bei optionalen Spalten bedeuten „nicht gesetzt“.
/// </summary>
public static class ClubCsv
{
    /// <summary>Spalten in fester Reihenfolge (Namen wie in den JSON-Dateien).</summary>
    public static readonly string[] Columns =
    {
        "key", "name", "shortName", "city", "division", "reputation", "reserveOf", "region",
        "latitude", "longitude", "primaryColor", "secondaryColor", "stadium", "capacity",
    };

    private static readonly HashSet<string> IntColumns = new() { "reputation", "capacity" };
    private static readonly HashSet<string> DoubleColumns = new() { "latitude", "longitude" };

    public static string Export(JsonArray clubs)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', Columns)).Append('\n');
        foreach (var club in clubs.OfType<JsonObject>())
            sb.Append(string.Join(';', Columns.Select(c => Quote(club[c] is JsonValue v ? Format(v) : "")))).Append('\n');
        return sb.ToString();
    }

    internal static string Format(JsonValue v) =>
        v.TryGetValue<double>(out var d) && !v.TryGetValue<string>(out _) ? d.ToString(CultureInfo.InvariantCulture) : v.ToString();

    internal static string Quote(string s) =>
        s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>Liest Vereine aus CSV. Unbekannte Spalten werden übernommen (als Text), Fehler je Zeile gesammelt.</summary>
    public static (JsonArray clubs, List<string> errors) Import(string csv)
    {
        var rows = Parse(csv);
        var errors = new List<string>();
        var clubs = new JsonArray();
        if (rows.Count == 0) return (clubs, new List<string> { "CSV ist leer." });
        var header = rows[0].Select(h => h.Trim()).ToList();
        if (!header.Contains("key") || !header.Contains("name")) errors.Add("CSV braucht mindestens die Spalten „key“ und „name“.");
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var club = new JsonObject();
            for (var c = 0; c < header.Count && c < row.Count; c++)
            {
                var col = header[c];
                var cell = row[c].Trim();
                if (cell.Length == 0) continue;
                if (IntColumns.Contains(col))
                {
                    if (int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) club[col] = n;
                    else errors.Add($"Zeile {r + 1}: „{col}“ ist keine ganze Zahl ({cell}).");
                }
                else if (DoubleColumns.Contains(col))
                {
                    if (double.TryParse(cell.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) club[col] = d;
                    else errors.Add($"Zeile {r + 1}: „{col}“ ist keine Zahl ({cell}).");
                }
                else club[col] = cell;
            }
            if (club["key"] is null) errors.Add($"Zeile {r + 1}: Schlüssel fehlt.");
            clubs.Add(club);
        }
        return (clubs, errors);
    }

    /// <summary>CSV-Zerlegung mit Anführungszeichen (auch Zeilenumbrüche in Zellen).</summary>
    internal static List<List<string>> Parse(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ';': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n': row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); break;
                default: cell.Append(ch); break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
