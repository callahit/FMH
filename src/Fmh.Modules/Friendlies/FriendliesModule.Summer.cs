using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

/// <summary>
/// Sommerturniere (AP 9b): Der Manager meldet seinen Verein an, drei Vereine mit ähnlichem Ruf kommen dazu. Halbfinale,
/// zwei Tage später Finale und Spiel um Platz 3, jeweils auf neutralem Platz mit Elfmeterschießen bei Gleichstand.
/// Prämien je Platz; die Spiele zählen wie Testspiele nicht in die Statistik.
/// </summary>
public sealed partial class FriendliesModule
{
    private const ulong SummerStream = 0x53554D4D4552UL;

    private DateOnly SummerDate(SummerTournamentRule rule) =>
        ScheduleGenerator.ParseDate(rule.Date, World.SeasonYear, World.Countries[0].Season.StartMonth);

    private static string Key(SummerTournamentRule rule, int year) => $"{rule.Id}-{year}";

    /// <summary>Sommerturniere dieser Vorbereitung, für die man sich noch anmelden kann.</summary>
    public IReadOnlyList<SummerTournamentRule> OpenSummerTournaments() => Rules.SummerTournaments
        .Where(r => SummerDate(r) > World.Date.AddDays(1) && World.SummerTournaments.All(t => t.Id != Key(r, World.SeasonYear)))
        .OrderBy(SummerDate).ToList();

    private bool BusyBetween(Club club, DateOnly from, DateOnly to) =>
        World.AllMatches.Any(f => f.Involves(club.Id) && f.Date >= from.AddDays(-Rules.MinRestDays) && f.Date <= to.AddDays(Rules.MinRestDays));

    /// <summary>Anmeldung: drei Gegner mit ähnlichem Ruf, Halbfinals ausgelost. Fehlertext-Schlüssel oder null.</summary>
    public string? EnterSummerTournament(string ruleId)
    {
        if (OpenSummerTournaments().FirstOrDefault(r => r.Id == ruleId) is not { } rule) return "summer.error.closed";
        var own = HumanClub;
        var semi = SummerDate(rule);
        var final = semi.AddDays(rule.DaysToFinal);
        if (World.InInternationalWindow(semi) || World.InInternationalWindow(final)) return "friendly.error.window";
        if (BusyBetween(own, semi, final)) return "friendly.error.busy";
        var rng = new Rng(Rng.Mix(World.Seed ^ SummerStream, (ulong)(World.SeasonYear * 100 + Rules.SummerTournaments.IndexOf(rule))));
        var pool = Candidates().Where(c => Math.Abs(c.Reputation - own.Reputation) <= rule.ReputationRange && !BusyBetween(c, semi, final)).ToList();
        if (pool.Count < 3) return "summer.error.noTeams";
        var guests = pool.OrderBy(_ => rng.NextDouble()).Take(3).ToList();
        var t = new SummerTournament { Id = Key(rule, World.SeasonYear), RuleId = rule.Id, Name = rule.Name, Year = World.SeasonYear };
        t.ClubIds.Add(own.Id);
        t.ClubIds.AddRange(guests.Select(c => c.Id));
        World.SummerTournaments.Add(t);
        var draw = t.ClubIds.OrderBy(_ => rng.NextDouble()).ToList();
        AddMatch(t, "SF", semi, draw[0], draw[1]);
        AddMatch(t, "SF", semi, draw[2], draw[3]);
        return null;
    }

    private void AddMatch(SummerTournament t, string stage, DateOnly date, int home, int away)
    {
        var friendly = new Friendly { TournamentId = t.Id, Stage = stage, HomeSharePercent = 100 };
        var f = friendly.Fixture;
        f.Id = World.NextFixtureId++;
        f.Date = date;
        f.HomeId = home;
        f.AwayId = away;
        f.RefereeId = World.Referees.Count == 0 ? 0 : World.Referees[(int)((uint)f.Id % (uint)World.Referees.Count)].Id;
        World.Friendlies.Add(friendly);
    }

    private List<Friendly> MatchesOf(SummerTournament t, string stage) =>
        World.Friendlies.Where(f => f.TournamentId == t.Id && f.Stage == stage).OrderBy(f => f.Fixture.Id).ToList();

    /// <summary>Sieger eines Turnierspiels: Tore, bei Gleichstand das Elfmeterschießen.</summary>
    public static int Winner(Fixture f)
    {
        var r = f.Result!;
        var homeWins = r.HomeGoals != r.AwayGoals ? r.HomeGoals > r.AwayGoals : (r.PenaltiesHome ?? 0) > (r.PenaltiesAway ?? 0);
        return homeWins ? f.HomeId : f.AwayId;
    }

    private static int Loser(Fixture f) => Winner(f) == f.HomeId ? f.AwayId : f.HomeId;

    /// <summary>Nach den Halbfinals Finale und Spiel um Platz 3 ansetzen, danach Platzierung und Prämien.</summary>
    private void AdvanceSummerTournaments()
    {
        foreach (var t in World.SummerTournaments.Where(t => t.Ranking.Count == 0))
        {
            if (Rules.SummerTournaments.FirstOrDefault(r => r.Id == t.RuleId) is not { } rule) continue;
            var semis = MatchesOf(t, "SF");
            if (semis.Count < 2 || semis.Any(f => f.Fixture.Result is null)) continue;
            var finals = MatchesOf(t, "F");
            if (finals.Count == 0)
            {
                var date = semis[0].Fixture.Date.AddDays(rule.DaysToFinal);
                AddMatch(t, "F", date, Winner(semis[0].Fixture), Winner(semis[1].Fixture));
                AddMatch(t, "P3", date, Loser(semis[0].Fixture), Loser(semis[1].Fixture));
                continue;
            }
            var third = MatchesOf(t, "P3");
            if (finals[0].Fixture.Result is null || third.Count == 0 || third[0].Fixture.Result is null) continue;
            t.Ranking = new List<int> { Winner(finals[0].Fixture), Loser(finals[0].Fixture), Winner(third[0].Fixture), Loser(third[0].Fixture) };
            for (var place = 0; place < t.Ranking.Count; place++)
            {
                var club = World.Clubs[t.Ranking[place]];
                var prize = place < rule.Prizes.Count ? rule.Prizes[place] : 0;
                if (prize > 0 && Finance.HasFinances(club)) Finance.Book(club, TransactionType.PrizeMoney, prize, TR("tx.summerPrize", t.Name, place + 1));
            }
            if (!Board.AwaitingNewJob && t.Ranking.IndexOf(HumanClub.Id) is var own and >= 0)
                Notify(MessageType.Info, "msg.sender.assistant", TR("summer.result.subject", t.Name, own + 1),
                    TR("summer.result.body", t.Name, World.Clubs[t.Ranking[0]].Name, own + 1, TextArg.Money(own < rule.Prizes.Count ? rule.Prizes[own] : 0, Currency)));
        }
    }
}
