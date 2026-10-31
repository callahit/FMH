namespace Fmh.Core.Data;

/// <summary>
/// Prüft Ligapyramiden (Umsetzungskonzept 6.1): Staffeln und Teamzahlen, Bewegungsregeln mit Ziel, Relegationen,
/// zweite Mannschaften und dass Zu- und Abgänge je Ebene in jeder Saison der Gültigkeitsspanne aufgehen.
/// </summary>
public static class PyramidValidator
{
    public static List<string> Validate(GameData data)
    {
        var errors = new List<string>();
        foreach (var versions in data.Countries.GroupBy(c => c.Code))
        {
            var ordered = versions.OrderBy(c => c.ValidFrom).ToList();
            foreach (var country in ordered) ValidateVersion(country, errors);
            ValidateClubs(data, ordered[0], errors);
            ValidateTransitions(ordered, errors);
        }
        ValidateReserves(data, errors);
        return errors;
    }

    private static string Name(CountryConfig c) => $"Land '{c.Code}' ab {c.ValidFrom}";

    private static void ValidateVersion(CountryConfig country, List<string> errors)
    {
        var levels = country.Levels.OrderBy(l => l.Level).ToList();
        var seasons = Seasons(country);
        foreach (var div in country.Divisions)
        {
            if (div.Teams is < 2 or > 24) errors.Add($"Liga '{div.Name}': Teamzahl {div.Teams} liegt außerhalb von 2–24.");
            if (div.Rounds is < 1 or > 4) errors.Add($"Liga '{div.Name}': {div.Rounds} Runden (erlaubt 1–4).");
            foreach (var dup in div.Groups.GroupBy(g => g.Id).Where(g => g.Count() > 1))
                errors.Add($"Liga '{div.Name}': Staffel '{dup.Key}' ist mehrfach definiert.");
            ValidatePhases(div, errors);
            ValidateMovements(country, div, div.Name, div.Movements, seasons, errors);
            foreach (var g in div.Groups.Where(g => g.Movements.Count > 0))
                ValidateMovements(country, div, g.Name, g.Movements, seasons, errors);
        }
        foreach (var po in country.Playoffs) ValidatePlayoff(country, po, seasons, errors);
        foreach (var pool in country.Pools.Where(p => p.Feeds is not null)) ValidatePool(country, levels, pool, seasons, errors);
        if (levels.Count == 0) return;
        foreach (var season in seasons)
        {
            var when = season is { } y ? $" (Saison {y})" : "";
            if (levels[0].Divisions.Any(d => Direct(d, MovementDirection.Up, season) > 0))
                Add(errors, $"{Name(country)}: die oberste Ebene kann keine Aufsteiger haben.");
            if (levels[^1].Divisions.Any(d => Direct(d, MovementDirection.Down, season) > 0 && country.Pools.All(p => p.Feeds != d.Id)))
                Add(errors, $"{Name(country)}: die unterste Ebene kann keine Absteiger haben (außer in einen Pool mit \"feeds\").");
            for (var i = 0; i + 1 < levels.Count; i++)
            {
                var (down, up) = Flow(country, levels[i], levels[i + 1], season);
                if (down != up)
                    errors.Add($"{Name(country)}: {down} Absteiger aus Ebene {levels[i].Level}, aber {up} Aufsteiger aus Ebene {levels[i + 1].Level}{when}.");
            }
        }
    }

    private static void Add(List<string> errors, string error)
    {
        if (!errors.Contains(error)) errors.Add(error);
    }

    /// <summary>Prüfsaisons einer Version: ohne Rotation der Regelfall (null), mit Rotation jede Saison eines vollen Zyklus.</summary>
    private static List<int?> Seasons(CountryConfig country)
    {
        var cycles = country.Divisions.SelectMany(d => d.Movements.Concat(d.Groups.SelectMany(g => g.Movements))).Select(m => m.Cycle)
            .Concat(country.Playoffs.Select(p => p.Cycle)).OfType<int>().Where(c => c > 1).Distinct().ToList();
        if (cycles.Count == 0) return new List<int?> { null };
        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        var length = cycles.Aggregate(1, (a, b) => a / Gcd(a, b) * b);
        return Enumerable.Range(country.ValidFrom, Math.Min(length, 60)).Select(y => (int?)y).ToList();
    }

    /// <summary>
    /// Regel der Saison ohne Übergangsregeln: <paramref name="season"/> null = nur Regeln ohne Rotation, sonst die Rotation dieser
    /// Saison. Mit <paramref name="transition"/> zählen auch Übergangsregeln (onlySeason) der Saison.
    /// </summary>
    private static bool Applies(int? onlySeason, int? cycle, int cycleStart, int? season, bool transition) =>
        (onlySeason is null || transition && onlySeason == season)
        && (season is null ? cycle is null : SeasonCycle.InCycle(cycle, cycleStart, season.Value));

    private static bool Applies(MovementRule m, int? season, bool transition = false) => Applies(m.OnlySeason, m.Cycle, m.CycleStart, season, transition);

    private static bool Applies(PlayoffConfig p, int? season, bool transition = false) => Applies(p.OnlySeason, p.Cycle, p.CycleStart, season, transition);

    private static void ValidateMovements(CountryConfig country, DivisionConfig div, string name, List<MovementRule> movements,
                                          List<int?> seasons, List<string> errors)
    {
        foreach (var m in movements)
        {
            var positions = m.PositionList();
            if (positions.Count == 0 || positions.Any(p => p < 1 || p > div.Teams))
                errors.Add($"Liga '{name}': ungültige Plätze '{m.Positions}' in einer Bewegungsregel.");
            if (m.Kind == MovementKind.Playoff && country.Playoffs.All(p => p.Id != m.Playoff))
                errors.Add($"Liga '{name}': Bewegungsregel verweist auf unbekannte Relegation '{m.Playoff}'.");
            if (m.Cycle is < 2) errors.Add($"Liga '{name}': Rotation über {m.Cycle} Saisons (mindestens 2).");
        }
        foreach (var season in seasons)
        {
            var used = new HashSet<int>();
            if (movements.Where(m => Applies(m, season)).SelectMany(m => m.PositionList()).Any(p => !used.Add(p)))
                Add(errors, $"Liga '{name}': Platz in mehreren Bewegungsregeln.");
        }
    }

    /// <summary>Pool unter der untersten Ebene: muss eine Division dieser Ebene speisen und genug Vereine für deren Absteiger haben.</summary>
    private static void ValidatePool(CountryConfig country, List<LevelConfig> levels, PoolConfig pool, List<int?> seasons, List<string> errors)
    {
        var div = levels.Count == 0 ? null : levels[^1].Divisions.FirstOrDefault(d => d.Id == pool.Feeds);
        if (div is null) { errors.Add($"Pool '{pool.Name}': '{pool.Feeds}' ist keine Division der untersten Ebene."); return; }
        var down = seasons.Max(s => Direct(div, MovementDirection.Down, s));
        if (pool.Teams < down) errors.Add($"Pool '{pool.Name}': {pool.Teams} Vereine reichen nicht für {down} Absteiger.");
    }

    /// <summary>Direkte Auf- oder Absteiger einer Division über alle Staffeln (eigene Regeln der Staffeln eingerechnet).</summary>
    private static int Direct(DivisionConfig d, MovementDirection to, int? season, bool transition = false) => d.LeagueIds.Sum(id =>
        d.MovementsOf(id).Where(m => m.Kind == MovementKind.Direct && m.To == to && Applies(m, season, transition))
            .SelectMany(m => m.PositionList()).Distinct().Count());

    /// <summary>Saisonphasen: jede Folgephase deckt alle Plätze der vorherigen Tabelle genau einmal ab.</summary>
    private static void ValidatePhases(DivisionConfig div, List<string> errors)
    {
        foreach (var phase in div.Phases)
        {
            var positions = phase.Groups.SelectMany(g => g.PositionList()).ToList();
            if (positions.Count != div.Teams || positions.Distinct().Count() != div.Teams || positions.Any(p => p < 1 || p > div.Teams))
                errors.Add($"Liga '{div.Name}': Die Gruppen der Phase '{phase.Name}' decken nicht alle Plätze genau einmal ab.");
            if (phase.Groups.Any(g => g.PositionList().Count < 2)) errors.Add($"Liga '{div.Name}': Eine Gruppe der Phase '{phase.Name}' hat weniger als zwei Teams.");
            if (phase.PointsCarry is <= 0 or > 1) errors.Add($"Liga '{div.Name}': Punkteübernahme {phase.PointsCarry} liegt außerhalb von (0, 1].");
            if (phase.Rounds is < 1 or > 4) errors.Add($"Liga '{div.Name}': {phase.Rounds} Runden in der Phase '{phase.Name}' (erlaubt 1–4).");
        }
    }

    private static int Leagues(DivisionConfig d) => Math.Max(1, d.Groups.Count);

    /// <summary>
    /// Wechsel zwischen zwei Ebenen in einer Saison: direkte Absteiger der oberen gegen direkte Aufsteiger der unteren
    /// plus die Relegationsplätze, die an die untere Ebene gehen können (Sieger minus Teilnehmer der oberen Ebene).
    /// </summary>
    private static (int down, int up) Flow(CountryConfig country, LevelConfig upper, LevelConfig lower, int? season, bool transition = false)
    {
        int Count(LevelConfig level, MovementDirection to) => level.Divisions.Sum(d => Direct(d, to, season, transition));
        var down = Count(upper, MovementDirection.Down);
        var up = Count(lower, MovementDirection.Up);
        bool In(LevelConfig level, string id) => level.Divisions.Any(d => d.Id == id || d.Groups.Any(g => g.Id == id));
        int EntrantCount(LevelConfig level, PlayoffConfig po) => po.Entrants.Where(e => In(level, e.Division)).Sum(e =>
            level.Divisions.FirstOrDefault(d => d.Id == e.Division) is { } d ? Leagues(d) : 1);
        foreach (var po in country.Playoffs.Where(p => In(upper, p.UpperDivision) && In(lower, p.LowerDivision) && Applies(p, season, transition)))
            up += po.Places - EntrantCount(upper, po);
        return (down, up);
    }

    private static void ValidatePlayoff(CountryConfig country, PlayoffConfig po, List<int?> seasons, List<string> errors)
    {
        DivisionConfig? Find(string id) => country.Divisions.FirstOrDefault(d => d.Id == id || d.Groups.Any(g => g.Id == id));
        if (Find(po.UpperDivision) is null || Find(po.LowerDivision) is null) { errors.Add($"Relegation '{po.Name}': unbekannte Liga."); return; }
        if (po.Entrants.Count == 0) { errors.Add($"Relegation '{po.Name}': keine Teilnehmer."); return; }
        var entrants = 0;
        foreach (var e in po.Entrants)
        {
            if (Find(e.Division) is not { } div) { errors.Add($"Relegation '{po.Name}': unbekannte Liga '{e.Division}'."); continue; }
            entrants += div.Id == e.Division ? Leagues(div) : 1;
            if (e.Position < 1 || e.Position > div.Teams) errors.Add($"Relegation '{po.Name}': ungültiger Tabellenplatz {e.Position}.");
            else if (seasons.Any(s => Applies(po, s) && (div.Id == e.Division ? div.LeagueIds : new[] { e.Division }).Any(id => div.MovementsOf(id)
                         .Any(m => m.Kind == MovementKind.Direct && Applies(m, s) && m.PositionList().Contains(e.Position)))))
                errors.Add($"Relegation '{po.Name}': Platz {e.Position} überschneidet sich mit direktem Auf- oder Abstieg.");
        }
        if (entrants < 2) errors.Add($"Relegation '{po.Name}': mindestens zwei Teilnehmer nötig.");
        if (po.Cycle is < 2) errors.Add($"Relegation '{po.Name}': Rotation über {po.Cycle} Saisons (mindestens 2).");
        // Ein Duell oder K.-o.-Turnier hat genau einen Sieger; mehrere Plätze je Relegation sind noch nicht umgesetzt.
        if (po.Places != 1) errors.Add($"Relegation '{po.Name}': {po.Places} Plätze, unterstützt wird genau einer.");
        var rounds = entrants <= 2 ? 1 : (int)Math.Ceiling(Math.Log2(entrants));
        if (entrants <= 2 ? po.Dates.Count < po.Legs : po.Dates.Count < rounds) errors.Add($"Relegation '{po.Name}': zu wenige Termine.");
    }

    /// <summary>Vereine der ersten Formatversion: Zahl je Liga bzw. Staffel und bekannte Zuordnung.</summary>
    private static void ValidateClubs(GameData data, CountryConfig country, List<string> errors)
    {
        foreach (var div in country.Divisions)
        {
            var direct = data.Clubs.Count(c => c.Division == div.Id);
            if (div.Groups.Count == 0)
            {
                if (direct != div.Teams) errors.Add($"Liga '{div.Name}' hat {direct} statt {div.Teams} Teams.");
                continue;
            }
            var total = direct + div.Groups.Sum(g => data.Clubs.Count(c => c.Division == g.Id));
            if (total != div.Teams * div.Groups.Count)
                errors.Add($"Liga '{div.Name}' hat {total} statt {div.Teams * div.Groups.Count} Teams in {div.Groups.Count} Staffeln.");
            foreach (var g in div.Groups.Where(g => data.Clubs.Count(c => c.Division == g.Id) > div.Teams))
                errors.Add($"Staffel '{g.Name}' hat mehr als {div.Teams} Teams.");
            if (div.Assignment == GroupAssignment.Geographic && data.Clubs.Any(c => (c.Division == div.Id || div.Groups.Any(g => g.Id == c.Division)) && c.Longitude is null))
                errors.Add($"Liga '{div.Name}': geografische Zuteilung braucht Koordinaten für alle Vereine.");
        }
    }

    /// <summary>Formatwechsel: Teamzahl je Ebene der neuen Version = alte Teamzahl plus Saldo der Übergangsregeln.</summary>
    private static void ValidateTransitions(List<CountryConfig> versions, List<string> errors)
    {
        for (var v = 0; v + 1 < versions.Count; v++)
        {
            var (old, next) = (versions[v], versions[v + 1]);
            var season = next.ValidFrom - 1;
            var levels = old.Levels.OrderBy(l => l.Level).ToList();
            var totals = levels.ToDictionary(l => l.Level, l => l.Divisions.Sum(d => d.Teams * Leagues(d)));
            for (var i = 0; i + 1 < levels.Count; i++)
            {
                var (down, up) = Flow(old, levels[i], levels[i + 1], season, transition: true);
                totals[levels[i].Level] += up - down;
                totals[levels[i + 1].Level] += down - up;
            }
            foreach (var level in next.Levels)
            {
                var teams = level.Divisions.Sum(d => d.Teams * Leagues(d));
                if (totals.TryGetValue(level.Level, out var expected) && expected != teams)
                    errors.Add($"{Name(next)}: Ebene {level.Level} hat {teams} Teams, nach der Saison {season} sind es {expected}.");
            }
            if (old.ValidTo is { } to && to + 1 != next.ValidFrom)
                errors.Add($"{Name(next)}: schließt nicht an die Vorversion an (gültig bis {to}).");
        }
    }

    private static void ValidateReserves(GameData data, List<string> errors)
    {
        var byKey = data.Clubs.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());
        (CountryConfig country, int level)? Level(ClubDefinition c) => data.Countries.SelectMany(x => x.Levels.Select(l => (x, l)))
            .Where(t => t.l.Divisions.Any(d => d.Id == c.Division || d.Groups.Any(g => g.Id == c.Division)))
            .Select(t => ((CountryConfig, int)?)(t.x, t.l.Level)).FirstOrDefault();
        foreach (var reserve in data.Clubs.Where(c => c.ReserveOf is not null))
        {
            if (!byKey.TryGetValue(reserve.ReserveOf!, out var parent)) { errors.Add($"Zweite Mannschaft '{reserve.Name}': Verein '{reserve.ReserveOf}' fehlt."); continue; }
            if (parent.ReserveOf is not null) errors.Add($"Zweite Mannschaft '{reserve.Name}': gehört zu einer zweiten Mannschaft.");
            if (Level(reserve) is { } r && Level(parent) is { } p && r.level < p.level + r.country.Reserves.MinLevelGap)
                errors.Add($"Zweite Mannschaft '{reserve.Name}': spielt zu hoch (Ebene {r.level}, erste Mannschaft Ebene {p.level}).");
        }
    }
}
