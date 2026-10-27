using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public sealed class NewGameOptions
{
    public ulong Seed { get; init; } = 20260701;
    public string ManagerName { get; init; } = "Manager";
    /// <summary>Schlüssel des Wunschvereins (ClubDefinition.Key).</summary>
    public string ClubKey { get; init; } = "";
    public int StartYear { get; init; } = 2026;
    public ManagerProfile Profile { get; init; } = new();
    public WorldRules Rules { get; init; } = new();
}

/// <summary>Erzeugt eine neue Spielwelt aus den Datenpaketen (Kapitel 2, Spielstart).</summary>
public static class WorldGenerator
{
    public static World Create(GameData data, NewGameOptions options)
    {
        var rng = new Rng(options.Seed);
        var world = new World
        {
            Seed = options.Seed,
            SeasonYear = options.StartYear,
            Rules = options.Rules,
            Packages = data.Packages.Select(p => $"{p.Id}@{p.Version}").ToList(),
        };

        var countries = data.Countries
            .Where(c => c.ValidFrom <= options.StartYear)
            .GroupBy(c => c.Code)
            .Select(g => g.OrderByDescending(c => c.ValidFrom).First())
            .Where(c => c.Activation == CountryActivation.Playable)
            .ToList();
        if (countries.Count == 0) throw new InvalidDataException("Keine spielbaren Länder in den Daten.");
        world.Countries = countries;
        var startMonth = countries[0].Season.StartMonth;
        world.Date = new DateOnly(options.StartYear, startMonth, 1);

        CreateReferees(world, data, rng, countries[0].Code);

        foreach (var country in countries)
        {
            foreach (var level in country.Levels.OrderBy(l => l.Level))
            {
                foreach (var div in level.Divisions)
                {
                    var league = new League
                    {
                        Id = div.Id,
                        Name = div.Name,
                        CountryCode = country.Code,
                        Level = level.Level,
                        Format = div,
                    };
                    var defs = data.Clubs.Where(c => c.Division == div.Id).ToList();
                    if (defs.Count != div.Teams)
                        throw new InvalidDataException($"Liga '{div.Name}' braucht {div.Teams} Vereine, Daten enthalten {defs.Count}.");
                    foreach (var def in defs)
                    {
                        var club = CreateClub(world, def, country.Code, league.Id);
                        league.ClubIds.Add(club.Id);
                        CreateSquad(world, data, rng, club, country.Code);
                    }
                    world.Leagues.Add(league);
                }
            }
            AddPoolClubs(world, data, rng, country);
        }

        foreach (var club in world.Clubs.Values)
        {
            club.Tactic.FormationId = AiTactics.BestFormation(club, world, data).Id;
            LineupSelector.PickBest(club, world, data, world.Country(club.CountryCode).Squad.BenchSize);
        }
        InitCareer(world, data, rng);

        foreach (var league in world.Leagues)
            ScheduleGenerator.CreateFixtures(world, league, world.Country(league.CountryCode), rng);

        var humanClub = world.Clubs.Values.FirstOrDefault(c => c.Key == options.ClubKey)
                        ?? throw new InvalidDataException($"Verein '{options.ClubKey}' nicht gefunden.");
        var manager = new Manager
        {
            Id = 1,
            Name = options.ManagerName,
            Human = true,
            ClubId = humanClub.Id,
            Profile = options.Profile,
        };
        humanClub.ManagerId = manager.Id;
        world.Managers.Add(manager);
        if (!world.Scouts.Any(s => s.ClubId == humanClub.Id))
            Game.GameSession.CreateScouts(world, humanClub, data, rng, 2);

        Messages.Add(world, manager, MessageType.Board, "msg.sender.board",
            TextRef.Of("msg.welcome.subject", humanClub.Name),
            TextRef.Of("msg.welcome.body", manager.Name, humanClub.Name, Messages.Competition(world, humanClub.LeagueId)));

        world.RngState = rng.GetState();
        return world;
    }

    /// <summary>Vereine unterhalb der Ligen (Amateurbereich): nehmen am Pokal teil und dienen als Transferumfeld.</summary>
    private static void AddPoolClubs(World world, GameData data, Rng rng, CountryConfig country)
    {
        foreach (var pool in country.Pools)
        {
            foreach (var def in data.Clubs.Where(c => c.Division == pool.Id))
            {
                if (world.Clubs.Values.Any(c => c.Key == def.Key)) continue;
                var club = CreateClub(world, def, country.Code, pool.Id);
                CreateSquad(world, data, rng, club, country.Code);
                club.Tactic.FormationId = AiTactics.BestFormation(club, world, data).Id;
                LineupSelector.PickBest(club, world, data, country.Squad.BenchSize);
            }
        }
    }

    /// <summary>
    /// Karrieredaten (Phase 2): Budgets, Trainer- und Jugendqualität, Taktikvertrautheit.
    /// Idempotent, damit auch ältere Spielstände nachgerüstet werden können.
    /// </summary>
    public static void InitCareer(World world, GameData data, Rng rng)
    {
        foreach (var country in world.Countries) AddPoolClubs(world, data, rng, country);
        foreach (var club in world.Clubs.Values)
        {
            if (club.Budget.Balance == 0 && club.Budget.TransferBudget == 0)
            {
                club.Budget.Balance = Budgets.StartingBalance(club);
                Budgets.SetSeasonBudget(club, world);
                int Quality(double sd) => (int)Math.Clamp(Math.Round(3 + club.Reputation * 0.75 + rng.Normal(0, sd)), 1, 20);
                club.CoachQuality = Quality(2);
                club.YouthFacilities = Quality(2.5);
                club.YouthRecruitment = Quality(3);
            }
            if (club.Familiarity.Count == 0) club.Familiarity[club.Tactic.FormationId] = 75;
            var ranked = world.SquadOf(club).Where(p => p.Contract is { Start.Year: 1 }).OrderByDescending(p => Ratings.Strength(p, data.PositionProfiles)).ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                var c = ranked[i].Contract!;
                c.Start = new DateOnly(world.SeasonYear - rng.Next(0, 3), 7, 1);
                c.Status = i switch { < 3 => SquadStatus.Star, < 11 => SquadStatus.Regular, < 16 => SquadStatus.Rotation, _ => ranked[i].Age(world.Date) <= 20 ? SquadStatus.Prospect : SquadStatus.Backup };
            }
        }
    }

    private static Club CreateClub(World world, ClubDefinition def, string country, string leagueId)
    {
        var club = new Club
        {
            Id = world.Clubs.Count + 1,
            Key = def.Key,
            Name = def.Name,
            ShortName = string.IsNullOrEmpty(def.ShortName) ? def.Name : def.ShortName,
            City = def.City,
            CountryCode = country,
            Reputation = def.Reputation,
            PrimaryColor = def.PrimaryColor,
            SecondaryColor = def.SecondaryColor,
            StadiumName = def.Stadium,
            StadiumCapacity = def.Capacity,
            LeagueId = leagueId,
        };
        world.Clubs[club.Id] = club;
        return club;
    }

    public static void CreateSquad(World world, GameData data, Rng rng, Club club, string country)
    {
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        foreach (var (pos, count) in PlayerGenerator.SquadTemplate)
        {
            for (var i = 0; i < count; i++)
            {
                var age = PlayerGenerator.RandomAge(rng);
                // Der erste Spieler je Position ist eher Stammspieler, die weiteren eher Ergänzung.
                var playerLevel = level + (i == 0 ? 0.8 : -0.6) + rng.Normal(0, 1.1) + AgeLevelOffset(age);
                var p = PlayerGenerator.Create(world, data, rng, pos, Math.Clamp(playerLevel, 3, 19), age, country);
                p.ClubId = club.Id;
                p.SeasonStats = new PlayerSeasonStats { SeasonYear = world.SeasonYear, ClubId = club.Id };
                world.Players[p.Id] = p;
                club.PlayerIds.Add(p.Id);
            }
        }
    }

    public static double AgeLevelOffset(int age) => age switch
    {
        <= 18 => -2.5,
        <= 20 => -1.5,
        <= 22 => -0.6,
        >= 33 => -0.8,
        _ => 0,
    };

    private static void CreateReferees(World world, GameData data, Rng rng, string country)
    {
        var names = data.Names.TryGetValue(country, out var n) ? n : data.Names.Values.First();
        for (var i = 0; i < 24; i++)
        {
            world.Referees.Add(new Referee
            {
                Id = i + 1,
                Name = $"{rng.Pick(names.FirstNames)} {rng.Pick(names.LastNames)}",
                Strictness = (int)Math.Clamp(Math.Round(rng.Normal(11, 4)), 1, 20),
            });
        }
    }
}

public static class Messages
{
    /// <summary>Wettbewerbsname als Text: übersetzbar über "competition.&lt;id&gt;", sonst der Name aus den Daten.</summary>
    public static TextArg Competition(World world, string id) => TextArg.Key("competition." + id, world.CompetitionName(id));

    public static Message Add(World world, Manager manager, MessageType type, string senderKey, TextRef subject, TextRef body)
    {
        var m = new Message
        {
            Id = world.NextMessageId++,
            ManagerId = manager.Id,
            Date = world.Date,
            Type = type,
            SenderKey = senderKey,
            SubjectRef = subject,
            BodyRef = body,
        };
        world.Messages.Add(m);
        return m;
    }
}
