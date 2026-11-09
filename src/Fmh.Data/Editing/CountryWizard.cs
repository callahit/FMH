using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

/// <summary>Angaben für ein neues Land: Code, Name, Teams je Ebene (oben zuerst), Pokal ja/nein, Vorlage für die Regeln.</summary>
public sealed record NewCountrySpec(string Code, string Name, IReadOnlyList<int> TeamsPerLevel, bool Cup = true, string Template = "countries/de.json");

/// <summary>
/// Assistent „Neues Land“ (Umsetzungskonzept 7.4): erzeugt Länderdatei, fiktive Vereine, Namensliste und Sponsoren.
/// Ligen, Auf- und Abstieg und Pokal entstehen aus den Angaben; Saison, Kader-, Wirtschafts- und Meldelistenregeln
/// übernimmt das Land je Ebene von der Vorlage. Alles in einem rückgängig machbaren Schritt.
/// </summary>
public static class CountryWizard
{
    private static readonly string[] Prefixes = { "FC", "SV", "TSV", "VfB", "SC", "Union", "Eintracht", "Fortuna", "Viktoria", "Sportfreunde", "FSV", "SpVgg" };
    private static readonly string[] Starts = { "Ahl", "Bern", "Dorn", "Eller", "Falk", "Gries", "Hollen", "Ilm", "Kranz", "Lerch", "Morl", "Nessel", "Orber", "Pell", "Quell", "Rabens", "Sell", "Tann", "Ulm", "Varn", "Wessel", "Zell" };
    private static readonly string[] Ends = { "au", "bach", "berg", "brück", "dorf", "feld", "hagen", "hausen", "heim", "ingen", "rode", "stedt", "tal", "wald" };
    private static readonly string[] Colors = { "Red", "Blue", "White", "Black", "Green", "Yellow", "Orange", "Purple", "SkyBlue", "Maroon" };

    public static IReadOnlyList<string> Create(PackageWorkspace ws, NewCountrySpec spec)
    {
        var code = spec.Code.Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 3 || !code.All(char.IsAsciiLetterUpper)) throw new ArgumentException("Ländercode: 2–3 Buchstaben.", nameof(spec));
        if (spec.TeamsPerLevel.Count is < 1 or > 6 || spec.TeamsPerLevel.Any(t => t is < 4 or > 24))
            throw new ArgumentException("1–6 Ebenen mit je 4–24 Teams.", nameof(spec));
        if (CountryTools.CountryFiles(ws).Any(f => (ws.Document(f) as JsonObject)?["code"]?.ToString() == code))
            throw new InvalidOperationException($"Land '{code}' gibt es schon.");
        var template = ws.Document(spec.Template) as JsonObject ?? throw new InvalidOperationException($"Vorlage '{spec.Template}' fehlt.");
        var lower = code.ToLowerInvariant();
        var country = Country(template, code, spec);
        var edits = new List<JsonEdit>
        {
            new SetValue($"countries/{lower}.json", JsonPath.Root, country),
            new SetValue($"clubs/{lower}.json", JsonPath.Root, Clubs(code, spec)),
        };
        var templateCode = template["code"]?.ToString()?.ToLowerInvariant() ?? "de";
        foreach (var folder in new[] { "names", "sponsors" })
            if (ws.Document($"{folder}/{templateCode}.json") is JsonObject list)
            {
                var copy = (JsonObject)list.DeepClone();
                copy["country"] = code;
                edits.Add(new SetValue($"{folder}/{lower}.json", JsonPath.Root, copy));
            }
        ws.Apply(new EditBatch($"Neues Land {code}", edits));
        return edits.Select(e => e.File).ToList();
    }

    private static JsonObject Country(JsonObject template, string code, NewCountrySpec spec)
    {
        var c = (JsonObject)template.DeepClone();
        c["code"] = code;
        c["name"] = spec.Name;
        c.Remove("validTo");
        c.Remove("europe"); // ohne Europapokalplätze, bis das Land im Koeffizienten steht
        var templateDivs = (template["levels"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .OrderBy(l => (int?)l["level"] ?? 0).Select(l => (l["divisions"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()).ToList();
        JsonObject? TemplateDiv(int level) => templateDivs.Count == 0 ? null : templateDivs[Math.Min(level, templateDivs.Count) - 1];
        string Id(int level) => $"{code}{level}";
        var teams = spec.TeamsPerLevel;
        // Austausch zwischen zwei Ebenen: 2 Vereine, bei kleinen Ligen 1
        int Exchange(int upperIndex) => Math.Max(1, Math.Min(2, Math.Min(teams[upperIndex], teams[upperIndex + 1]) / 6));

        var levels = new JsonArray();
        for (var i = 0; i < teams.Count; i++)
        {
            var level = i + 1;
            var div = (JsonObject?)TemplateDiv(level)?.DeepClone() ?? new JsonObject();
            foreach (var key in new[] { "groups", "phases", "promotion", "relegation", "assignment" }) div.Remove(key);
            div["id"] = Id(level);
            div["name"] = level == 1 ? $"{spec.Name} Liga" : $"{level}. Liga";
            div["teams"] = teams[i];
            div["rounds"] = 2;
            var moves = new JsonArray();
            if (i > 0) moves.Add(new JsonObject { ["positions"] = Range(1, Exchange(i - 1)), ["to"] = "Up" });
            if (i + 1 < teams.Count) moves.Add(new JsonObject { ["positions"] = Range(teams[i] - Exchange(i) + 1, teams[i]), ["to"] = "Down" });
            div["movements"] = moves;
            levels.Add(new JsonObject { ["level"] = level, ["divisions"] = new JsonArray(div) });
        }
        c["levels"] = levels;
        c["pools"] = new JsonArray();
        c["playoffs"] = new JsonArray();
        c["cups"] = spec.Cup ? new JsonArray(Cup(template, code, spec, teams.Count)) : new JsonArray();

        // Regeln je Ebene von der Vorlage, auf die neuen Kennungen umgeschlüsselt
        JsonObject Remap(JsonNode? source, bool dropReference)
        {
            var result = new JsonObject();
            for (var level = 1; level <= teams.Count; level++)
                if (TemplateDiv(level)?["id"]?.ToString() is { } tid && (source as JsonObject)?[tid] is JsonObject entry)
                {
                    var copy = (JsonObject)entry.DeepClone();
                    if (dropReference) copy.Remove("reference");
                    result[Id(level)] = copy;
                }
            return result;
        }
        if (c["finance"] is JsonObject finance) finance["divisions"] = Remap(template["finance"]?["divisions"], dropReference: true);
        c["registration"] = Remap(template["registration"], dropReference: false);
        if (c["loans"] is JsonObject loans && loans["divisions"] is JsonObject) loans["divisions"] = Remap(template["loans"]?["divisions"], false);
        return c;
    }

    private static string Range(int from, int to) => from == to ? $"{from}" : $"{from}-{to}";

    /// <summary>Landespokal aller Ligen nach dem größten Pokal der Vorlage (Termine, Spiele je Runde).</summary>
    private static JsonObject Cup(JsonObject template, string code, NewCountrySpec spec, int levels)
    {
        var source = (template["cups"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().MaxBy(c => (int?)c["teams"] ?? 0);
        var cup = (JsonObject?)source?.DeepClone() ?? new JsonObject();
        var total = spec.TeamsPerLevel.Sum();
        var size = 2;
        while (size < total) size *= 2;
        cup["id"] = $"{code}-CUP";
        cup["name"] = $"{spec.Name}-Pokal";
        cup["teams"] = size;
        cup["participants"] = new JsonArray(Enumerable.Range(1, levels).Select(l => (JsonNode)JsonValue.Create($"{code}{l}")!).ToArray());
        cup["entries"] = new JsonArray();
        cup["region"] = "";
        var rounds = (int)Math.Ceiling(Math.Log2(size));
        var dates = (cup["dates"] as JsonArray)?.Select(d => d?.ToString() ?? "").Where(d => d.Length == 5).ToList() ?? new List<string>();
        cup["dates"] = new JsonArray(FillDates(dates, rounds).Select(d => (JsonNode)JsonValue.Create(d)!).ToArray());
        return cup;
    }

    /// <summary>
    /// Ergänzt Pokaltermine (MM-TT) bis zur Rundenzahl: jeweils in die Mitte der größten Lücke, damit alle Runden
    /// in der Saison der Vorlage bleiben. Ohne Vorlagetermine gelten Saisontermine von August bis Mai.
    /// </summary>
    public static List<string> FillDates(List<string> dates, int rounds)
    {
        if (dates.Count < 2) dates = new List<string> { "08-10", "05-20" };
        // Tage ab Saisonbeginn (Juli), damit der Jahreswechsel die Reihenfolge nicht stört
        static int Day(string d)
        {
            var day = new DateTime(2001, int.Parse(d[..2]), int.Parse(d[3..])).DayOfYear;
            return day < 182 ? day + 365 : day;
        }
        var days = dates.Select(Day).ToList();
        while (days.Count < rounds)
        {
            var gap = Enumerable.Range(0, days.Count - 1).MaxBy(i => days[i + 1] - days[i]);
            days.Insert(gap + 1, (days[gap] + days[gap + 1]) / 2);
        }
        return days.Select(d => new DateTime(2001, 1, 1).AddDays((d - 1) % 365).ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    /// <summary>Fiktive Vereine je Ebene: Ruf und Stadion nach Ebene, Namen aus Silben (feste Reihenfolge, eindeutig).</summary>
    private static JsonArray Clubs(string code, NewCountrySpec spec)
    {
        var clubs = new JsonArray();
        var used = new HashSet<string>();
        var n = 0;
        for (var i = 0; i < spec.TeamsPerLevel.Count; i++)
        {
            var level = i + 1;
            for (var j = 0; j < spec.TeamsPerLevel[i]; j++, n++)
            {
                string city;
                var k = n;
                do city = Starts[k % Starts.Length] + Ends[(k / Starts.Length + k * 5) % Ends.Length];
                while (!used.Add(city) && k++ < 10_000);
                var reputation = Math.Max(1, 16 - 3 * i - j * 6 / Math.Max(1, spec.TeamsPerLevel[i]));
                var capacity = Math.Max(1500, 42000 / (1 << (2 * i)) - j * 400 / (i + 1));
                clubs.Add(new JsonObject
                {
                    ["key"] = $"{code.ToLowerInvariant()}{level}-{j + 1}",
                    ["name"] = $"{Prefixes[n % Prefixes.Length]} {city}",
                    ["shortName"] = city,
                    ["city"] = city,
                    ["division"] = $"{code}{level}",
                    ["reputation"] = reputation,
                    ["primaryColor"] = Colors[n % Colors.Length],
                    ["secondaryColor"] = Colors[(n + 3) % Colors.Length],
                    ["stadium"] = $"Stadion {city}",
                    ["capacity"] = capacity,
                });
            }
        }
        return clubs;
    }
}
