using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

/// <summary>Werkzeuge für Länder im Editor: neue Formatversion, Pyramidenübersicht.</summary>
public static class CountryTools
{
    /// <summary>Länderdateien (alle Formatversionen) im Arbeitsbereich.</summary>
    public static IReadOnlyList<string> CountryFiles(PackageWorkspace ws) =>
        ws.Files.Where(f => f.StartsWith("countries/", StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Neue Formatversion ab <paramref name="fromSeason"/> (Startjahr): Kopie der Länderdatei mit <c>validFrom</c>,
    /// die bisherige Version endet mit <c>validTo = fromSeason - 1</c>. Ein Schritt, rückgängig machbar.
    /// </summary>
    public static string NewFormatVersion(PackageWorkspace ws, string file, int fromSeason)
    {
        var doc = ws.Document(file) as JsonObject ?? throw new InvalidOperationException($"'{file}' ist keine Länderdatei.");
        var validFrom = (int?)doc["validFrom"] ?? 0;
        if (fromSeason <= validFrom) throw new ArgumentException($"Die neue Version muss nach {validFrom} beginnen.", nameof(fromSeason));
        if ((int?)doc["validTo"] is { } to && fromSeason > to + 1)
            throw new ArgumentException($"Die Version gilt nur bis {to}; eine neue Version kann höchstens ab {to + 1} beginnen.", nameof(fromSeason));
        var code = doc["code"]?.ToString() ?? "xx";
        var target = $"countries/{code.ToLowerInvariant()}-{fromSeason}.json";
        if (ws.Document(target) is not null) throw new InvalidOperationException($"'{target}' gibt es schon.");
        var copy = (JsonObject)doc.DeepClone();
        copy["validFrom"] = fromSeason;
        if ((int?)doc["validTo"] is { } oldTo) copy["validTo"] = oldTo; else copy.Remove("validTo");
        ws.Apply(new EditBatch($"Formatversion {code} ab {fromSeason}", new JsonEdit[]
        {
            new SetValue(file, JsonPath.Root.Then("validTo"), JsonValue.Create(fromSeason - 1)),
            new SetValue(target, JsonPath.Root, copy),
        }));
        return target;
    }

    /// <summary>Pyramide einer Formatversion für die Anzeige: je Ebene die Divisionen mit Staffeln, Teams und Bewegungen.</summary>
    public static List<PyramidLevel> Pyramid(JsonObject country)
    {
        var result = new List<PyramidLevel>();
        foreach (var level in (country["levels"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().OrderBy(l => (int?)l["level"] ?? 0))
        {
            var divisions = (level["divisions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(d =>
            {
                var groupNodes = (d["groups"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
                var groups = groupNodes.Select(g => g["name"]?.ToString() ?? g["id"]?.ToString() ?? "").ToList();
                var teams = (int?)d["teams"] ?? 18;
                var own = Moves(d["movements"]);
                if (own.up == 0 && own.down == 0) own = ((int?)d["promotion"] ?? 0, (int?)d["relegation"] ?? 0);
                // Geteilte Division: Summe über die Staffeln, jede mit eigenen Regeln oder sonst denen der Division
                double up = own.up, down = own.down;
                if (groupNodes.Count > 0)
                {
                    up = down = 0;
                    foreach (var g in groupNodes)
                    {
                        var m = g["movements"] is JsonArray { Count: > 0 } list ? Moves(list) : own;
                        up += m.up;
                        down += m.down;
                    }
                }
                return new PyramidDivision(d["id"]?.ToString() ?? "", d["name"]?.ToString() ?? "", teams, Math.Max(1, groups.Count), groups,
                    (int)Math.Round(up), (int)Math.Round(down));
            }).ToList();
            result.Add(new PyramidLevel((int?)level["level"] ?? 0, divisions));
        }
        return result;
    }

    /// <summary>
    /// Direkte Auf- und Abstiegsplätze je Saison aus den Bewegungsregeln (ohne einmalige Übergangsregeln);
    /// eine rotierende Regel (<c>cycle</c>) zählt anteilig, etwa ein Platz alle drei Jahre als ein Drittel.
    /// </summary>
    private static (double up, double down) Moves(JsonNode? movements)
    {
        double up = 0, down = 0;
        foreach (var m in (movements as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if (m["onlySeason"] is not null || (m["kind"]?.ToString() ?? "Direct") != "Direct") continue;
            var n = new MovementRule { Positions = m["positions"]?.ToString() ?? "" }.PositionList().Count / (double)Math.Max(1, (int?)m["cycle"] ?? 1);
            if (m["to"]?.ToString() == "Up") up += n; else down += n;
        }
        return (up, down);
    }
}

public sealed record PyramidLevel(int Level, IReadOnlyList<PyramidDivision> Divisions)
{
    public int Teams => Divisions.Sum(d => d.Teams * d.Leagues);
}

/// <summary>
/// Division in der Übersicht; <see cref="Leagues"/> = Zahl der Staffeln (1 ohne Teilung). <see cref="Up"/> und
/// <see cref="Down"/> sind die direkten Plätze je Saison über alle Staffeln.
/// </summary>
public sealed record PyramidDivision(string Id, string Name, int Teams, int Leagues, IReadOnlyList<string> Groups, int Up, int Down)
{
    public string Shape => Leagues > 1 ? $"{Leagues} × {Teams}" : $"{Teams}";
}
