using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Übernimmt vorgegebene Personen aus den Datenpaketen in die neue Welt (AP 6c). Vorgaben sind ein Rahmen: Was fehlt,
/// erzeugt der Generator wie bei Zufallsspielern. Ohne Vorgaben verbraucht nichts hier Zufallszahlen, die Welt
/// entsteht dann genau wie vorher.
/// </summary>
public static class DefinedPeople
{
    /// <summary>
    /// Vorgegebene Spieler: Beim Verein ersetzt jeder den schwächsten erzeugten Spieler seiner Hauptposition, damit die
    /// Kadergröße stimmt; ohne Verein wird er vereinslos. Vereine nicht spielbarer Länder werden übersprungen.
    /// </summary>
    public static void ApplyPlayers(World world, GameData data, Rng rng)
    {
        if (data.Players.Count == 0) return;
        var clubs = world.Clubs.Values.GroupBy(c => c.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var defined = new HashSet<int>();
        foreach (var def in data.Players)
        {
            Club? club = null;
            if (def.Club.Length > 0 && !clubs.TryGetValue(def.Club, out club)) continue;
            var country = club?.CountryCode ?? def.Nationalities.FirstOrDefault() ?? world.Countries[0].Code;
            var age = Math.Clamp(def.BirthDate is { } b ? AgeAt(b, world.Date) : def.Age ?? PlayerGenerator.RandomAge(rng), 15, 45);
            var level = def.Level ?? (club is null ? 10 : PlayerGenerator.ClubLevel(club.Reputation)) + WorldGenerator.AgeLevelOffset(age);
            var p = PlayerGenerator.Create(world, data, rng, def.Position, Math.Clamp(level, 1, 20), age, country);
            Apply(p, def, data.PositionProfiles);
            defined.Add(p.Id);
            world.Players[p.Id] = p;
            if (club is null)
            {
                p.Contract = null;
                continue;
            }

            var replaced = club.PlayerIds.Select(id => world.Players[id])
                .Where(x => !defined.Contains(x.Id) && x.MainPosition == def.Position)
                .OrderBy(x => Ratings.Strength(x, data.PositionProfiles)).FirstOrDefault();
            if (replaced is not null)
            {
                club.PlayerIds.Remove(replaced.Id);
                world.Players.Remove(replaced.Id);
            }
            p.ClubId = club.Id;
            club.PlayerIds.Add(p.Id);
            var contract = p.Contract!;
            contract.WeeklyWage = ClubRules.LeagueWage(world, club, def.WeeklyWage ?? contract.WeeklyWage);
            if (def.ContractUntil is { } until) contract.Until = new DateOnly(Math.Max(until, world.SeasonYear + 1), 6, 30);
            ContractClauses.FillDefaults(contract, world, club);
            Registration.InitFormative(p, world, club, rng);
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = world.SeasonYear, ClubId = club.Id };
        }
    }

    /// <summary>Überträgt die Vorgaben auf einen erzeugten Spieler und berechnet Fähigkeit und Potenzial neu.</summary>
    public static void Apply(Player p, PlayerDefinition def, PositionProfiles profiles)
    {
        p.FirstName = def.FirstName;
        p.LastName = def.LastName;
        if (def.BirthDate is { } birth) p.BirthDate = birth;
        if (def.Nationalities.Count > 0) p.Nationalities = def.Nationalities.ToList();
        foreach (var (attr, value) in def.Attributes) p.Attributes[attr] = value;
        if (def.Positions.Count > 0) p.Positions = new Dictionary<Position, Suitability>(def.Positions);
        p.Positions[def.Position] = Suitability.Natural;
        if (def.Foot is { } foot)
        {
            int strong = Math.Max(p.LeftFoot, p.RightFoot), weak = Math.Min(p.LeftFoot, p.RightFoot);
            (p.LeftFoot, p.RightFoot) = foot switch
            {
                PreferredFoot.Left => (strong, weak),
                PreferredFoot.Right => (weak, strong),
                _ => (strong, strong),
            };
        }
        if (def.Height is { } h) p.Height = h;
        if (def.Weight is { } w) p.Weight = w;
        if (def.Reputation is { } r) p.Reputation = r;

        var hidden = p.Hidden;
        var d = def.Hidden;
        hidden.Ambition = d.Ambition ?? hidden.Ambition;
        hidden.Professionalism = d.Professionalism ?? hidden.Professionalism;
        hidden.Loyalty = d.Loyalty ?? hidden.Loyalty;
        hidden.Pressure = d.Pressure ?? hidden.Pressure;
        hidden.Temperament = d.Temperament ?? hidden.Temperament;
        hidden.Sportsmanship = d.Sportsmanship ?? hidden.Sportsmanship;
        hidden.Adaptability = d.Adaptability ?? hidden.Adaptability;
        hidden.Controversy = d.Controversy ?? hidden.Controversy;
        hidden.Consistency = d.Consistency ?? hidden.Consistency;
        hidden.InjuryProneness = d.InjuryProneness ?? hidden.InjuryProneness;
        hidden.ImportantMatches = d.ImportantMatches ?? hidden.ImportantMatches;

        // Spielraum nach oben bleibt erhalten, wenn Attribute die Fähigkeit verschieben
        var headroom = hidden.PotentialAbility - hidden.CurrentAbility;
        hidden.CurrentAbility = Ratings.CurrentAbility(p, profiles);
        hidden.PotentialAbility = Math.Clamp(def.PotentialAbility ?? hidden.CurrentAbility + headroom, hidden.CurrentAbility, 200);
    }

    /// <summary>Vorgegebene Schiedsrichter der spielbaren Länder (oder ohne Land); leer = keine Vorgaben.</summary>
    public static List<Referee> Referees(World world, GameData data)
    {
        var codes = world.Countries.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        return data.Referees.Where(r => r.Country.Length == 0 || codes.Contains(r.Country))
            .Select((r, i) => new Referee { Id = i + 1, Name = r.Name, Strictness = Math.Clamp(r.Strictness, 1, 20) }).ToList();
    }

    /// <summary>KI-Manager: übernehmen ihren Verein; der des menschlichen Managers bleibt bei ihm, der KI-Manager ist dann frei.</summary>
    public static void ApplyManagers(World world, GameData data, Club humanClub)
    {
        var clubs = world.Clubs.Values.GroupBy(c => c.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var def in data.Managers)
        {
            var manager = new Manager { Id = world.Managers.Max(m => m.Id) + 1, Name = def.Name, Reputation = Math.Clamp(def.Reputation, 1, 20) };
            if (clubs.TryGetValue(def.Club, out var club) && club != humanClub && club.ManagerId is null)
            {
                manager.ClubId = club.Id;
                club.ManagerId = manager.Id;
            }
            world.Managers.Add(manager);
        }
    }

    private static int AgeAt(DateOnly birth, DateOnly today)
    {
        var age = today.Year - birth.Year;
        return today < birth.AddYears(age) ? age - 1 : age;
    }
}
