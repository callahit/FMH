using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

/// <summary>
/// Turniere (AP 8b): Qualifikation in den Fenstern der Saison vor dem Turnierjahr, Endrunde im Sommer mit Gruppen
/// und K.-o.-Runden. Die Abläufe stehen in <c>rules/internationals.json</c>.
/// </summary>
public sealed partial class InternationalsModule
{
    private const ulong DrawStream = 0x5455524E4945UL;
    /// <summary>Die Kader reisen eine Woche vor dem ersten Spiel der Endrunde an.</summary>
    private const int CampDays = 7;

    private TournamentRule? Rule(InternationalTournament t) => Data.Internationals.Tournaments.FirstOrDefault(r => r.Id == t.RuleId);

    /// <summary>Turniere der Saison anlegen: Endrunde im Sommer, der Saisonwechsel folgt danach.</summary>
    private void EnsureTournaments()
    {
        var year = World.SeasonYear + 1;
        foreach (var rule in Data.Internationals.Tournaments.Where(r => InternationalTournaments.IsTournamentYear(r, year)))
        {
            var key = InternationalTournaments.Key(rule, year);
            if (World.Tournaments.Any(t => t.Id == key)) continue;
            NationalSquads.Ensure(World, Data);
            var t = new InternationalTournament
            {
                Id = key,
                RuleId = rule.Id,
                Name = rule.Name,
                Year = year,
                FinalsStart = ScheduleGenerator.ParseDate(rule.Start, World.SeasonYear, World.Countries[0].Season.StartMonth),
            };
            // Zu spät angelegt (z. B. älterer Spielstand im Juni): Die Endrunde rückt so weit nach hinten, dass die Kader
            // morgen anreisen und kein Spieltag in der Vergangenheit liegt.
            if (World.Date >= t.FinalsStart.AddDays(-CampDays)) t.FinalsStart = World.Date.AddDays(CampDays + 1);
            World.Tournaments.Add(t);
            var ratings = Participants().Select(n => (n.Code, InternationalTournaments.Rating(World, Data, n))).ToList();
            var dates = World.InternationalWindows.SelectMany(w => w.MatchDates)
                .Where(d => d > World.Date && d < t.FinalsStart.AddDays(-CampDays)).OrderBy(d => d).ToList();
            var groups = (int)Math.Ceiling(ratings.Count / (double)rule.QualificationGroupSize);
            var rounds = 2 * (Math.Min(rule.QualificationGroupSize, ratings.Count) - 1);
            if (ratings.Count > rule.Teams && rounds <= dates.Count)
                StartQualification(t, ratings, groups, dates);
            else
            {
                // Zu wenige Länder oder Termine: Die Stärksten fahren direkt zur Endrunde.
                t.Qualified = ratings.OrderByDescending(r => r.Item2).ThenBy(r => r.Code, StringComparer.Ordinal)
                    .Take(rule.Teams).Select(r => r.Code).ToList();
                StartFinals(t, rule);
            }
        }
    }

    /// <summary>Teilnehmer: alle Länder mit genug gesunden Nationalspielern und die Hintergrundländer.</summary>
    private IEnumerable<NationalTeam> Participants() => World.NationalTeams.Where(t => t.Background
        || World.Players.Values.Count(p => NationalSquads.Eligible(p, t.Code, World, Data)) >= MinSquad);

    private Rng DrawRng(InternationalTournament t, int phase) => new(Rng.Mix(World.Seed ^ DrawStream, (ulong)(t.Year * 10 + phase)));

    private void StartQualification(InternationalTournament t, List<(string Code, double Rating)> ratings, int groups, List<DateOnly> dates)
    {
        t.Phase = TournamentPhase.Qualification;
        t.QualificationGroups = InternationalTournaments.Draw(ratings, groups, DrawRng(t, 1));
        foreach (var g in t.QualificationGroups)
        {
            var rounds = InternationalTournaments.RoundRobin(g.Codes, twice: true);
            for (var k = 0; k < rounds.Count; k++)
                foreach (var (home, away) in rounds[k])
                    AddFixture(t, $"Q-{g.Name}", dates[k], home, away);
        }
    }

    private void StartFinals(InternationalTournament t, TournamentRule rule)
    {
        t.Phase = TournamentPhase.Finals;
        var ratings = t.Qualified.Select(c => (c, InternationalTournaments.Rating(World, Data, NationalSquads.TeamOf(World, c)!)));
        var groups = Math.Max(1, Math.Min(rule.Teams / rule.GroupSize, t.Qualified.Count / Math.Max(1, rule.Advance)));
        t.Groups = InternationalTournaments.Draw(ratings, groups, DrawRng(t, 2));
        foreach (var g in t.Groups)
        {
            var rounds = InternationalTournaments.RoundRobin(g.Codes, twice: false);
            for (var k = 0; k < rounds.Count; k++)
                foreach (var (home, away) in rounds[k])
                    AddFixture(t, $"G-{g.Name}", t.FinalsStart.AddDays(k * rule.DaysBetween), home, away);
        }
    }

    private void AddFixture(InternationalTournament t, string stage, DateOnly date, string home, string away) =>
        World.InternationalFixtures.Add(new InternationalFixture
        {
            Id = World.InternationalFixtures.Count == 0 ? 1 : World.InternationalFixtures.Max(f => f.Id) + 1,
            WindowId = stage.StartsWith("Q", StringComparison.Ordinal) ? World.InternationalWindows.FirstOrDefault(w => w.Contains(date))?.Id ?? "" : "",
            Date = date,
            HomeCode = home,
            AwayCode = away,
            TournamentId = t.Id,
            Stage = stage,
        });

    private List<InternationalFixture> FixturesOf(InternationalTournament t, string prefix) =>
        World.InternationalFixtures.Where(f => f.TournamentId == t.Id && f.Stage.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    /// <summary>Tagesablauf der Turniere: Qualifikation auswerten, Kader berufen, nächste Runde ansetzen, Sieger ehren.</summary>
    private void TournamentDay()
    {
        foreach (var t in World.Tournaments.Where(t => t.Phase != TournamentPhase.Done).ToList())
        {
            if (Rule(t) is not { } rule) continue;
            if (t.Phase == TournamentPhase.Qualification && FixturesOf(t, "Q-").All(f => f.Result is not null))
            {
                var tables = t.QualificationGroups.Select(g => InternationalTournaments.Standings(FixturesOf(t, $"Q-{g.Name}"), g)).ToList();
                t.Qualified = InternationalTournaments.Qualified(tables, rule.Teams);
                StartFinals(t, rule);
            }
            if (t.Phase != TournamentPhase.Finals) continue;
            if (World.Date == t.FinalsStart.AddDays(-CampDays)) CallUpFinals(t);
            AdvanceFinals(t, rule);
        }
    }

    private void CallUpFinals(InternationalTournament t)
    {
        var taken = new HashSet<int>();
        foreach (var team in t.Qualified.Select(c => NationalSquads.TeamOf(World, c)!).Where(n => !n.Background).OrderBy(n => n.Code, StringComparer.Ordinal))
        {
            NationalSquads.Call(World, team, NationalSquads.Nominate(World, Data, team, taken));
            taken.UnionWith(team.Squad);
        }
        NotifyCallups(TR("intl.tournamentCallup.body", CallupLines(), t.Name));
    }

    /// <summary>Nach jedem Spieltag: ausgeschiedene Kader entlassen, nächste Runde ansetzen oder den Sieger küren.</summary>
    private void AdvanceFinals(InternationalTournament t, TournamentRule rule)
    {
        var group = FixturesOf(t, "G-");
        var knockout = FixturesOf(t, "KO");
        if (group.Count == 0 || group.Any(f => f.Result is null) || knockout.Any(f => f.Result is null)) return;
        if (knockout.Count == 0)
        {
            var tables = t.Groups.Select(g => InternationalTournaments.Standings(FixturesOf(t, $"G-{g.Name}"), g)).ToList();
            var entrants = InternationalTournaments.KnockoutEntrants(tables, rule.Advance);
            ReleaseAll(t.Qualified.Except(entrants));
            ScheduleRound(t, entrants, group.Max(f => f.Date).AddDays(rule.DaysBetween));
            return;
        }
        var last = knockout.Where(f => f.Date == knockout.Max(x => x.Date)).OrderBy(f => f.Id).ToList();
        ReleaseAll(last.Select(f => f.WinnerCode == f.HomeCode ? f.AwayCode : f.HomeCode));
        var winners = last.Select(f => f.WinnerCode!).ToList();
        if (winners.Count > 1)
        {
            ScheduleRound(t, winners, last[0].Date.AddDays(rule.DaysBetween));
            return;
        }
        t.Champion = winners[0];
        t.Phase = TournamentPhase.Done;
        ReleaseAll(winners);
        var final = last[0];
        News(TR("intl.champion.subject", NationalSquads.TeamOf(World, t.Champion)!.Name, t.Name),
            TR("intl.champion.body", t.Name, NationalSquads.TeamOf(World, final.HomeCode)!.Name, final.Result!.HomeGoals,
                final.Result.AwayGoals, NationalSquads.TeamOf(World, final.AwayCode)!.Name, NationalSquads.TeamOf(World, t.Champion)!.Name));
    }

    private void ScheduleRound(InternationalTournament t, List<string> entrants, DateOnly date)
    {
        for (var i = 0; i + 1 < entrants.Count; i += 2)
            AddFixture(t, $"KO{entrants.Count}", date, entrants[i], entrants[i + 1]);
    }

    private void ReleaseAll(IEnumerable<string> codes)
    {
        foreach (var team in codes.Select(c => NationalSquads.TeamOf(World, c)).Where(n => n is { Background: false }))
            NationalSquads.Release(World, Data, team!);
    }

    private void News(TextRef subject, TextRef body)
    {
        if (Board.AwaitingNewJob) return;
        Notify(MessageType.Info, "msg.sender.association", subject, body);
    }
}
