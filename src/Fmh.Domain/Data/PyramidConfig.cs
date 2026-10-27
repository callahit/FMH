namespace Fmh.Core.Data;

/// <summary>Zuteilung der Vereine auf die Staffeln einer geteilten Liga (Umsetzungskonzept 6.1).</summary>
public enum GroupAssignment
{
    /// <summary>Feste Staffeln; Neulinge kommen in die Staffel ihrer Region bzw. die nächstgelegene mit freiem Platz.</summary>
    Fixed,
    /// <summary>Jede Saison neu nach Koordinaten der Vereine (von West nach Ost).</summary>
    Geographic,
    /// <summary>Jede Saison neu ausgelost.</summary>
    Rotating,
}

public enum MovementDirection { Up, Down }

public enum MovementKind { Direct, Playoff }

/// <summary>Staffel einer geteilten Liga; im Spiel eine eigene Liga mit der Kennung der Staffel.</summary>
public sealed class GroupConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Regionen, deren Vereine bevorzugt in diese Staffel kommen (Zuteilung "fixed").</summary>
    public List<string> Regions { get; set; } = new();
}

/// <summary>
/// Bewegungsregel einer Liga: welche Tabellenplätze auf- oder absteigen. Ersetzt die früheren Felder
/// <c>promotion</c>/<c>relegation</c>, die beim Laden darauf abgebildet werden.
/// </summary>
public sealed class MovementRule
{
    /// <summary>Tabellenplätze, z. B. "1-2" oder "18".</summary>
    public string Positions { get; set; } = "";
    public MovementDirection To { get; set; }
    public MovementKind Kind { get; set; } = MovementKind.Direct;
    /// <summary>Bei <see cref="MovementKind.Playoff"/>: Kennung der Relegation bzw. des Aufstiegsturniers.</summary>
    public string? Playoff { get; set; }
    /// <summary>Übergangsregel, die nur in dieser Saison (Startjahr) gilt, z. B. Sonderabsteiger vor einer Reform.</summary>
    public int? OnlySeason { get; set; }

    public bool AppliesTo(int seasonYear) => OnlySeason is null || OnlySeason == seasonYear;

    /// <summary>Plätze als Liste (1-basiert); leer bei ungültiger Angabe.</summary>
    public IReadOnlyList<int> PositionList()
    {
        var parts = Positions.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var from)) return Array.Empty<int>();
        var to = from;
        if (parts.Length > 1 && !int.TryParse(parts[1], out to)) return Array.Empty<int>();
        return from <= to ? Enumerable.Range(from, to - from + 1).ToList() : Array.Empty<int>();
    }
}

/// <summary>Teilnehmer einer Relegation: Tabellenplatz einer Liga (bei geteilten Ligen aus jeder Staffel).</summary>
public sealed class PlayoffEntrant
{
    /// <summary>Kennung der Liga oder einer einzelnen Staffel.</summary>
    public string Division { get; set; } = "";
    public int Position { get; set; }
}

/// <summary>Regeln für zweite Mannschaften (Umsetzungskonzept 6.1).</summary>
public sealed class ReserveRules
{
    /// <summary>Mindestabstand in Ebenen zur ersten Mannschaft (1 = nie in derselben Liga).</summary>
    public int MinLevelGap { get; set; } = 1;
    public bool PlayInCups { get; set; }
    /// <summary>Spielrecht: Altersgrenze (Jahre am Saisonstart); darüber nur <see cref="OverAgePlayers"/> Spieler je Spiel.</summary>
    public int AgeLimit { get; set; } = 23;
    public int OverAgePlayers { get; set; } = 3;
}

/// <summary>
/// Bildet ältere Länderformate auf das neue Schema ab (Migration der Datenpakete und Spielstände):
/// <c>promotion</c>/<c>relegation</c> werden zu Bewegungsregeln, <c>upperPosition</c>/<c>lowerPosition</c> zu Teilnehmern.
/// Umgekehrt werden die Zählfelder aus den Regeln ergänzt, weil Anzeige und Saisonziele sie nutzen. Idempotent.
/// </summary>
public static class FormatNormalizer
{
    public static CountryConfig Normalize(this CountryConfig country)
    {
        foreach (var div in country.Divisions) div.Normalize();
        foreach (var po in country.Playoffs.Where(p => p.Entrants.Count == 0 && p.UpperPosition > 0 && p.LowerPosition > 0))
        {
            po.Entrants.Add(new PlayoffEntrant { Division = po.UpperDivision, Position = po.UpperPosition });
            po.Entrants.Add(new PlayoffEntrant { Division = po.LowerDivision, Position = po.LowerPosition });
        }
        return country;
    }

    public static DivisionConfig Normalize(this DivisionConfig div)
    {
        if (div.Movements.Count == 0)
        {
            if (div.Promotion > 0)
                div.Movements.Add(new MovementRule { Positions = $"1-{div.Promotion}", To = MovementDirection.Up });
            if (div.Relegation > 0)
                div.Movements.Add(new MovementRule { Positions = $"{div.Teams - div.Relegation + 1}-{div.Teams}", To = MovementDirection.Down });
            return div;
        }
        var regular = div.Movements.Where(m => m.Kind == MovementKind.Direct && m.OnlySeason is null).ToList();
        if (div.Promotion == 0)
        {
            var up = regular.Where(m => m.To == MovementDirection.Up).SelectMany(m => m.PositionList()).ToHashSet();
            while (up.Contains(div.Promotion + 1)) div.Promotion++;
        }
        if (div.Relegation == 0)
        {
            var down = regular.Where(m => m.To == MovementDirection.Down).SelectMany(m => m.PositionList()).ToHashSet();
            while (down.Contains(div.Teams - div.Relegation)) div.Relegation++;
        }
        return div;
    }
}
