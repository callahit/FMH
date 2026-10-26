using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>K.-o.-Wettbewerbe (Kapitel 3): Pokal mit Auslosung je Runde und Relegation mit Hin- und Rückspiel.</summary>
public static class CupLogic
{
    /// <summary>Text-Schlüssel des Rundennamens nach Zahl der verbleibenden Teams.</summary>
    public static string RoundKey(int teamsLeft, int roundNumber) => teamsLeft switch
    {
        2 => "cup.round.final",
        4 => "cup.round.semi",
        8 => "cup.round.quarter",
        16 => "cup.round.last16",
        _ => "cup.round.n",
    };

    public static Cup CreateCup(World world, CountryConfig country, CupConfig cfg)
    {
        var participants = world.Clubs.Values
            .Where(c => c.CountryCode == country.Code && cfg.Participants.Contains(c.LeagueId))
            .Select(c => c.Id).OrderBy(id => id).ToList();
        var size = 2;
        while (size < Math.Max(cfg.Teams, participants.Count)) size *= 2;
        var cup = new Cup
        {
            Id = cfg.Id,
            Name = cfg.Name,
            CountryCode = country.Code,
            Kind = CompetitionKind.Cup,
            SeasonYear = world.SeasonYear,
            Config = cfg,
            ParticipantIds = participants,
        };
        var rounds = (int)Math.Round(Math.Log2(size));
        for (var r = 0; r < rounds; r++)
        {
            var teamsLeft = size >> r;
            var dates = r < cfg.Dates.Count ? cfg.Dates[r].Split(',') : new[] { "06-01" };
            cup.Rounds.Add(new CupRound
            {
                Number = r + 1,
                NameKey = RoundKey(teamsLeft, r + 1),
                Dates = dates.Select(d => ToWeekday(ScheduleGenerator.ParseDate(d.Trim(), world.SeasonYear, country.Season.StartMonth), DayOfWeek.Wednesday)).ToList(),
            });
        }
        return cup;
    }

    /// <summary>Verschiebt ein Datum auf den gewünschten Wochentag derselben Woche (Montag bis Sonntag).</summary>
    public static DateOnly ToWeekday(DateOnly d, DayOfWeek day)
    {
        var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        return monday.AddDays(((int)day + 6) % 7);
    }

    public static Cup CreatePlayoff(World world, CountryConfig country, PlayoffConfig cfg, int upperClub, int lowerClub)
    {
        var cup = new Cup
        {
            Id = cfg.Id,
            Name = cfg.Name,
            CountryCode = country.Code,
            Kind = CompetitionKind.Playoff,
            SeasonYear = world.SeasonYear,
            Config = new CupConfig { Id = cfg.Id, Name = cfg.Name, Legs = cfg.Legs, AwayGoals = cfg.AwayGoals, LowerTierHome = false, NeutralFinal = false },
            ParticipantIds = new List<int> { upperClub, lowerClub },
            UpperDivision = cfg.UpperDivision,
            LowerDivision = cfg.LowerDivision,
        };
        cup.Rounds.Add(new CupRound
        {
            Number = 1,
            Name = cfg.Name,
            NameKey = "competition." + cfg.Id,
            Dates = cfg.Dates.Select(d => ScheduleGenerator.ParseDate(d, world.SeasonYear, country.Season.StartMonth)).ToList(),
        });
        // Hinspiel beim Unterklassigen, Rückspiel beim Erstligisten.
        AddTie(world, cup, cup.Rounds[0], lowerClub, upperClub);
        return cup;
    }

    /// <summary>Lost eine Runde aus. Ungerade Teilnehmerzahl oder Freilose: überzählige Teams kommen kampflos weiter.</summary>
    public static void Draw(World world, Cup cup, CupRound round, List<int> teams, Rng rng)
    {
        var pool = teams.ToList();
        rng.Shuffle(pool);
        var size = 1 << (cup.Rounds.Count - round.Number + 1);
        var byes = Math.Max(0, size - pool.Count);
        // Freilose bekommen die stärksten Vereine (höchste Liga, höchste Reputation).
        var byeTeams = pool.OrderBy(id => Level(world, id)).ThenByDescending(id => world.Clubs[id].Reputation).Take(byes).ToList();
        foreach (var id in byeTeams)
        {
            pool.Remove(id);
            round.Ties.Add(new CupTie { Id = world.NextTieId++, HomeId = id, AwayId = id, Legs = 0, WinnerId = id, Decision = "Bye" });
        }
        for (var i = 0; i + 1 < pool.Count; i += 2)
        {
            int home = pool[i], away = pool[i + 1];
            if (cup.Config.LowerTierHome && Level(world, away) > Level(world, home)) (home, away) = (away, home);
            AddTie(world, cup, round, home, away);
        }
    }

    private static void AddTie(World world, Cup cup, CupRound round, int home, int away)
    {
        var legs = Math.Max(1, cup.Config.Legs);
        var tie = new CupTie { Id = world.NextTieId++, HomeId = home, AwayId = away, Legs = legs };
        for (var leg = 1; leg <= legs; leg++)
        {
            var date = round.Dates.Count >= leg ? round.Dates[leg - 1] : round.Dates[0].AddDays(7 * (leg - 1));
            var f = new Fixture
            {
                Id = world.NextFixtureId++,
                CompetitionId = cup.Id,
                Kind = cup.Kind,
                TieId = tie.Id,
                Leg = leg,
                Matchday = round.Number,
                Date = date,
                HomeId = leg == 1 ? home : away,
                AwayId = leg == 1 ? away : home,
                RefereeId = world.Referees.Count == 0 ? 0 : world.Referees[(tie.Id * 7 + leg) % world.Referees.Count].Id,
            };
            tie.FixtureIds.Add(f.Id);
            cup.Fixtures.Add(f);
        }
        round.Ties.Add(tie);
    }

    /// <summary>Liga-Ebene eines Vereins (Pools zählen als unterste Ebene).</summary>
    public static int Level(World world, int clubId)
    {
        var club = world.Clubs[clubId];
        return world.FindLeague(club.LeagueId)?.Level ?? 99;
    }

    public static CupTie? TieOf(Cup cup, Fixture f) => cup.Rounds.SelectMany(r => r.Ties).FirstOrDefault(t => t.Id == f.TieId);

    /// <summary>K.-o.-Kontext für die Engine: Verlängerung und Elfmeterschießen nur, wenn das Duell in diesem Spiel entschieden wird.</summary>
    public static KnockoutContext? KnockoutFor(Cup cup, Fixture f)
    {
        var tie = TieOf(cup, f);
        if (tie is null) return null;
        if (tie.Legs <= 1) return new KnockoutContext();
        if (f.Leg < tie.Legs) return null;
        var first = cup.Fixtures.First(x => x.Id == tie.FixtureIds[0]).Result;
        if (first is null) return null;
        // Aus Sicht des heutigen Heimteams: es war im Hinspiel der Gast.
        return new KnockoutContext
        {
            FirstLegHome = first.AwayGoals,
            FirstLegAway = first.HomeGoals,
            AwayGoals = cup.Config.AwayGoals,
            HasFirstLeg = true,
        };
    }

    /// <summary>Wertet ein gespieltes Pokalspiel aus; gibt true zurück, wenn das Duell damit entschieden ist.</summary>
    public static bool ResolveTie(Cup cup, Fixture f)
    {
        var tie = TieOf(cup, f);
        if (tie is null || tie.WinnerId is not null || f.Result is not { } r) return false;
        if (f.Leg < tie.Legs) return false;

        if (tie.Legs <= 1)
        {
            var homeWins = r.HomeGoals > r.AwayGoals || (r.HomeGoals == r.AwayGoals && (r.PenaltiesHome ?? 0) > (r.PenaltiesAway ?? 0));
            tie.WinnerId = homeWins ? f.HomeId : f.AwayId;
            tie.Decision = r.PenaltiesHome is not null ? "Penalties" : r.ExtraTime ? "ExtraTime" : "Regular";
            return true;
        }

        var legs = tie.FixtureIds.Select(id => cup.Fixtures.First(x => x.Id == id)).ToList();
        int Goals(int club) => legs.Sum(l => l.HomeId == club ? l.Result!.HomeGoals : l.Result!.AwayGoals);
        int AwayGoalsOf(int club) => legs.Where(l => l.AwayId == club).Sum(l => l.Result!.AwayGoals);
        var a = tie.HomeId;
        var b = tie.AwayId;
        if (Goals(a) != Goals(b)) { tie.WinnerId = Goals(a) > Goals(b) ? a : b; tie.Decision = r.ExtraTime ? "ExtraTime" : "Regular"; }
        else if (cup.Config.AwayGoals && AwayGoalsOf(a) != AwayGoalsOf(b)) { tie.WinnerId = AwayGoalsOf(a) > AwayGoalsOf(b) ? a : b; tie.Decision = "AwayGoals"; }
        else
        {
            var homeWins = (r.PenaltiesHome ?? 0) > (r.PenaltiesAway ?? 0);
            tie.WinnerId = homeWins ? f.HomeId : f.AwayId;
            tie.Decision = "Penalties";
        }
        return true;
    }

    /// <summary>Gesamtergebnis eines Duells aus Sicht des erstgenannten Teams, mit Zusatz für Verlängerung bzw. Elfmeterschießen.</summary>
    public static TextRef TieScore(Cup cup, CupTie tie)
    {
        if (tie.Decision == "Bye") return TextRef.Of("cup.bye");
        var legs = tie.FixtureIds.Select(id => cup.Fixtures.First(x => x.Id == id)).Where(l => l.Result is not null).ToList();
        if (legs.Count == 0) return TextRef.Literal("–:–");
        var hg = legs.Sum(l => l.HomeId == tie.HomeId ? l.Result!.HomeGoals : l.Result!.AwayGoals);
        var ag = legs.Sum(l => l.HomeId == tie.AwayId ? l.Result!.HomeGoals : l.Result!.AwayGoals);
        var last = legs[^1].Result!;
        if (last.PenaltiesHome is { } ph && last.PenaltiesAway is { } pa)
        {
            var (a, b) = legs[^1].HomeId == tie.HomeId ? (ph, pa) : (pa, ph);
            return TextRef.Of("cup.scorePenalties", $"{hg}:{ag}", $"{a}:{b}");
        }
        return last.ExtraTime ? TextRef.Of("cup.scoreExtraTime", $"{hg}:{ag}") : TextRef.Literal($"{hg}:{ag}");
    }
}
