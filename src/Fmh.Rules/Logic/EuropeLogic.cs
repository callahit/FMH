using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Europapokal (AP 5): Wettbewerbe anlegen, Einstiegsrunden, Ligaphase und Koeffizienten.</summary>
public static partial class EuropeLogic
{
    public const string CountryCode = "EU";

    public static EuropeCompetitionFormat? FormatOf(GameData data, string id) => data.Europe?.Competition(id);

    /// <summary>Legacy-Lookup: gemeinsames Format, sonst noch in den Länderdaten hinterlegte Wettbewerbe.</summary>
    public static EuropeCompetition? LegacyOf(CountryConfig country, string id) =>
        country.Europe?.Competitions.FirstOrDefault(c => c.Id == id);

    public static long EntryPrize(GameData data, CountryConfig country, string competitionId) =>
        FormatOf(data, competitionId)?.EntryPrize ?? LegacyOf(country, competitionId)?.EntryPrize ?? 0;

    /// <summary>Rang der Länder nach Fünfjahreskoeffizient (1 = bestes geladenes Land).</summary>
    public static Dictionary<string, int> CountryRanks(World world, EuropeFormat format)
    {
        EnsureCoefficients(world, format);
        return world.Countries
            .OrderByDescending(c => world.CountryCoefficients.GetValueOrDefault(c.Code, format.Access.DefaultCountryScore))
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select((c, i) => (c.Code, Rank: i + 1))
            .ToDictionary(x => x.Code, x => x.Rank);
    }

    public static void EnsureCoefficients(World world, EuropeFormat format)
    {
        foreach (var c in world.Countries)
            if (!world.CountryCoefficients.ContainsKey(c.Code))
                world.CountryCoefficients[c.Code] = format.Access.DefaultCountryScore
                    + world.Clubs.Values.Where(cl => cl.CountryCode == c.Code && world.FindLeague(cl.LeagueId)?.Level == 1)
                        .Select(cl => cl.Reputation).DefaultIfEmpty(10).Average() * 0.4;
        foreach (var club in world.Clubs.Values)
            if (!world.ClubCoefficients.ContainsKey(club.Id))
                world.ClubCoefficients[club.Id] = club.Reputation * 2.5;
    }

    /// <summary>Einstiegs-Stufen-Id für einen Qualifikanten.</summary>
    public static string EntryStageId(EuropeFormat format, string competitionId, int countryRank, bool champion)
    {
        var rule = format.Access.EntryByRank.OrderBy(r => r.MaxRank).FirstOrDefault(r => countryRank <= r.MaxRank)
                   ?? format.Access.EntryByRank.LastOrDefault()
                   ?? new EuropeEntryRule();
        return competitionId switch
        {
            "UCL" => champion ? rule.UclChampion : rule.UclOther,
            "UEL" => rule.Uel,
            _ => rule.Uecl,
        };
    }

    /// <summary>Legt UCL/UEL/UECL der Saison aus den Qualifikanten an und lost die erste Runde aus.</summary>
    public static List<Cup> CreateSeason(World world, GameData data, Rng rng)
    {
        if (data.Europe is not { } format) return new();
        EnsureCoefficients(world, format);
        var ranks = CountryRanks(world, format);
        var created = new List<Cup>();
        foreach (var comp in format.Competitions)
        {
            var entries = world.EuropeanEntries
                .Where(e => e.SeasonYear == world.SeasonYear && e.CompetitionId == comp.Id && !e.Eliminated)
                .ToList();
            if (entries.Count == 0) continue;
            if (world.FindCup(comp.Id) is not null) continue;
            // Weniger als zwei Vereine: kein Turnier, der einzige Qualifikant gilt als Sieger der Saison.
            if (entries.Count < 2)
            {
                var sole = entries[0];
                sole.Stage = Math.Max(0, comp.Stages.Count - 1);
                sole.Eliminated = true;
                var club = world.Clubs[sole.ClubId];
                world.History.Add(new SeasonRecord
                {
                    SeasonYear = world.SeasonYear, LeagueId = comp.Id, LeagueName = comp.Id,
                    ChampionId = club.Id, ChampionName = club.Name, FinalOrder = new List<int> { club.Id },
                });
                continue;
            }
            var cup = BuildCup(world, format, comp, entries, ranks, rng);
            world.Cups.Add(cup);
            created.Add(cup);
            StartFirstRound(world, data, cup, rng);
            FlushByeRounds(world, data, cup, rng);
        }
        return created;
    }

    private static Cup BuildCup(World world, EuropeFormat format, EuropeCompetitionFormat comp,
        List<EuropeanEntry> entries, Dictionary<string, int> ranks, Rng rng)
    {
        var cup = new Cup
        {
            Id = comp.Id,
            Name = comp.Id,
            CountryCode = CountryCode,
            Kind = CompetitionKind.Europe,
            SeasonYear = world.SeasonYear,
            Config = new CupConfig
            {
                Id = comp.Id,
                Name = comp.Id,
                Legs = 2,
                FinalLegs = 1,
                AwayGoals = false,
                NeutralFinal = true,
                LowerTierHome = false,
            },
            ParticipantIds = entries.Select(e => e.ClubId).Distinct().OrderBy(id => id).ToList(),
        };

        var stageIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < comp.Stages.Count; i++)
        {
            var st = comp.Stages[i];
            stageIndex[st.Id] = i;
            var weekday = Enum.TryParse<DayOfWeek>(st.Weekday, true, out var wd) ? wd : DayOfWeek.Wednesday;
            var dates = st.Dates.Select(d => CupLogic.ToWeekday(ParseEuropeDate(d, world.SeasonYear), weekday)).ToList();
            cup.Rounds.Add(new CupRound
            {
                Number = i + 1,
                NameKey = st.Key,
                StageId = st.Id,
                StageType = st.Type,
                Prize = st.Prize,
                Legs = st.Type == "league" ? 1 : Math.Max(1, st.Legs),
                Dates = dates,
                Teams = 0,
            });
            cup.Config.RoundLegs.Add(st.Type == "league" ? 1 : Math.Max(1, st.Legs));
        }

        // Meister = Ligaplatz 1 der Europa-Liga des Landes in der Vorsaison (History) bzw. höchster Ruf.
        foreach (var e in entries)
        {
            var club = world.Clubs[e.ClubId];
            var rank = ranks.GetValueOrDefault(club.CountryCode, 99);
            var champion = WasChampion(world, club);
            var stageId = EntryStageId(format, comp.Id, rank, champion);
            if (!stageIndex.ContainsKey(stageId)) stageId = FirstAvailable(comp, stageId, stageIndex);
            var idx = stageIndex[stageId];
            cup.Rounds[idx].Entrants.Add(e.ClubId);
            e.Stage = idx - 1;
            e.Eliminated = false;
        }

        // Frühe Qualirunden ohne Teilnehmer streichen und Indizes anpassen.
        CompactEmptyLeadingRounds(cup, entries);
        _ = rng;
        return cup;
    }

    private static bool WasChampion(World world, Club club)
    {
        var europe = world.Country(club.CountryCode).Europe;
        if (europe is null) return false;
        var prev = world.History.LastOrDefault(h => h.LeagueId == europe.League && h.SeasonYear == world.SeasonYear - 1);
        return prev?.ChampionId == club.Id;
    }

    private static string FirstAvailable(EuropeCompetitionFormat comp, string wanted, Dictionary<string, int> index)
    {
        if (index.ContainsKey(wanted)) return wanted;
        var order = new[] { "q1", "q2", "q3", "po", "league", "r16", "qf", "sf", "final" };
        var start = Array.IndexOf(order, wanted);
        if (start < 0) start = 0;
        for (var i = start; i < order.Length; i++)
            if (index.ContainsKey(order[i])) return order[i];
        return comp.Stages[0].Id;
    }

    private static void CompactEmptyLeadingRounds(Cup cup, List<EuropeanEntry> entries)
    {
        while (cup.Rounds.Count > 1 && cup.Rounds[0].Entrants.Count == 0 && cup.Rounds[0].StageType != "league")
        {
            cup.Rounds.RemoveAt(0);
            cup.Config.RoundLegs.RemoveAt(0);
            for (var i = 0; i < cup.Rounds.Count; i++) cup.Rounds[i].Number = i + 1;
            foreach (var e in entries) e.Stage--;
        }
    }

    private static DateOnly ParseEuropeDate(string monthDay, int seasonYear)
    {
        var parts = monthDay.Split('-');
        var month = int.Parse(parts[0]);
        var day = int.Parse(parts[1]);
        // Europapokal-Sommer (Juli/August) gehört zur neuen Saison ab Juli.
        var year = month >= 7 ? seasonYear : seasonYear + 1;
        return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    public static void StartFirstRound(World world, GameData data, Cup cup, Rng rng)
    {
        var first = cup.Rounds.FirstOrDefault(r => r.Entrants.Count > 0) ?? cup.Rounds[0];
        if (first.StageType == "league") ScheduleLeague(world, data, cup, first, first.Entrants, rng);
        else DrawKnockout(world, cup, first, first.Entrants, rng);
    }
}
