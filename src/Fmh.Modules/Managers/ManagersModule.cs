using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Trainerkarussell der KI-Manager: Bilanz je Station, Entlassungen nach Tabellenlage und Form, Nachfolger aus dem
/// Pool vereinsloser Trainer, Bilanz zum Saisonende. Alle Entscheidungen sind feste Regeln ohne Spielzufall; neue
/// Trainer entstehen aus dem eigenen Zufallsstrom von <see cref="ManagerGenerator"/>.
/// </summary>
public sealed class ManagersModule : GameModule
{
    public override string Id => "managers";

    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e => Record(e.Fixture));
    }

    public override void OnDay(DayContext day)
    {
        FillVacancies(day.Date);
        foreach (var league in day.Fixtures.Where(f => f.Kind == CompetitionKind.League && f.Result is not null)
                     .Select(f => World.FindLeague(f.CompetitionId)).OfType<League>().Distinct().OrderBy(l => l.Id).ToList())
            Evaluate(league, day.Date);
        if (day.Stop == StopReason.SeasonEnd) ReviewSeason(day.Date);
    }

    /// <summary>Bilanz der laufenden Station beider KI-Trainer.</summary>
    private void Record(Fixture f)
    {
        if (f.Result is not { } r) return;
        foreach (var (clubId, diff) in new[] { (f.HomeId, r.HomeGoals - r.AwayGoals), (f.AwayId, r.AwayGoals - r.HomeGoals) })
        {
            if (!World.Clubs.TryGetValue(clubId, out var club) || ManagerGenerator.AiManagerOf(World, club)?.CurrentStation is not { } st) continue;
            st.Matches++;
            if (diff > 0) st.Wins++;
            else if (diff == 0) st.Draws++;
            else st.Losses++;
        }
    }

    /// <summary>Vereine ohne Trainer (auch nach Entlassung oder Weggang des Spielers) bekommen einen KI-Manager.</summary>
    public void FillVacancies(DateOnly date)
    {
        foreach (var club in World.Clubs.Values.Where(c => ManagerGenerator.NeedsManager(World, c)).OrderBy(c => c.Id).ToList())
            Hire(club, date, null);
    }

    /// <summary>Erwarteter Tabellenplatz nach Ruf (Rang innerhalb der Liga, bei Gleichstand nach Id).</summary>
    public static Dictionary<int, int> ExpectedPositions(League league, World world) =>
        league.ClubIds.Select(id => world.Clubs[id]).OrderByDescending(c => c.Reputation).ThenBy(c => c.Id)
            .Select((c, i) => (c.Id, i + 1)).ToDictionary(t => t.Id, t => t.Item2);

    /// <summary>
    /// Nach einem Spieltag: Entlassung, wenn der Verein auf einem direkten Abstiegsplatz bzw. den letzten BottomPlaces Plätzen deutlich unter
    /// den Erwartungen steht und die letzten fünf Spiele höchstens drei Punkte brachten. Frühestens nach einem Drittel
    /// der Saison und höchstens einmal je Verein und Saison.
    /// </summary>
    public void Evaluate(League league, DateOnly date)
    {
        var rules = Data.ManagerRules.InSeason;
        var total = league.Fixtures.Count == 0 ? 0 : league.Fixtures.Max(f => f.Matchday);
        if (league.ClubIds.Count < 6 || league.CurrentMatchday < Math.Max(rules.MinMatchday, (int)(total * rules.EarliestShare)) || league.CurrentMatchday >= total) return;
        var seasonStart = new DateOnly(World.SeasonYear, 7, 1);
        var n = league.ClubIds.Count;
        var expected = ExpectedPositions(league, World);
        foreach (var row in LeagueTable.Compute(league))
        {
            var club = World.Clubs[row.ClubId];
            if (ManagerGenerator.AiManagerOf(World, club) is not { CurrentStation: { } st } m) continue;
            if (date.DayNumber - st.From.DayNumber < rules.MinTenureDays || st.From > seasonStart.AddDays(rules.NewcomerDays)) continue;
            var formPoints = row.Form.TakeLast(5).Sum(c => c == 'S' ? 3 : c == 'U' ? 1 : 0);
            var bottom = InDanger(league, row.Position, World.SeasonYear, rules.BottomPlaces);
            var below = row.Position - expected.GetValueOrDefault(club.Id, row.Position);
            if (bottom && below >= Math.Max(rules.MinBelow, (int)(n * rules.BelowShare)) && formPoints <= rules.MaxFormPoints) Sack(m, club, date, row.Position);
        }
    }

    /// <summary>Gefährdeter Tabellenplatz: direkte Abstiegszone oder einer der letzten <paramref name="bottomPlaces"/> Plätze.</summary>
    public static bool InDanger(League league, int position, int seasonYear, int bottomPlaces) =>
        Pyramid.DirectZone(league, position, seasonYear) == MovementDirection.Down || position > league.ClubIds.Count - bottomPlaces;

    /// <summary>Saisonende: Absteiger und deutlich enttäuschende Vereine trennen sich, erfolgreiche Trainer gewinnen an Ruf.</summary>
    public void ReviewSeason(DateOnly date)
    {
        var rules = Data.ManagerRules.SeasonEnd;
        foreach (var h in World.History.Where(h => h.SeasonYear == World.SeasonYear).OrderBy(h => h.LeagueId).ToList())
        {
            if (World.FindLeague(h.LeagueId) is not { } league || h.FinalOrder.Count < 6) continue;
            var expected = ExpectedPositions(league, World);
            var n = h.FinalOrder.Count;
            for (var i = 0; i < n; i++)
            {
                if (!World.Clubs.TryGetValue(h.FinalOrder[i], out var club) || ManagerGenerator.AiManagerOf(World, club) is not { CurrentStation: { } st } m) continue;
                var pos = i + 1;
                var below = pos - expected.GetValueOrDefault(club.Id, pos);
                var relegated = Pyramid.DirectZone(league, pos, World.SeasonYear) == MovementDirection.Down;
                if (pos == 1 || below <= -Math.Max(2, (int)(n * rules.PraiseShare))) m.Reputation = Math.Min(20, m.Reputation + 1);
                // Nur wer schon vor dieser Saison im Amt war; Absteiger mit klarer Enttäuschung oder weit hinter den Erwartungen
                if (st.From <= new DateOnly(World.SeasonYear, 7, 1)
                    && (relegated && below >= rules.RelegatedBelow || below >= Math.Max(rules.MinBelow, (int)(n * rules.BelowShare)))) Sack(m, club, date, pos);
            }
        }
        // Ältere Trainer ohne Verein gehen in den Ruhestand, der Pool wird aufgefüllt
        World.Managers.RemoveAll(m => !m.Human && m.ClubId is null && World.SeasonYear - m.Traits.BirthYear >= Data.ManagerRules.RetirementAge);
        ManagerGenerator.TopUpPool(World, Data, ManagerGenerator.RngFor(World, World.SeasonYear * 104729L + 1));
    }

    /// <summary>Entlassung: Trainer kommt in den Pool, der Verein holt sofort einen Nachfolger.</summary>
    public void Sack(Manager m, Club club, DateOnly date, int position)
    {
        ManagerGenerator.Release(m, club, date, "sacked");
        m.Traits.Sackings++;
        m.Reputation = Math.Max(1, m.Reputation - 1);
        var successor = Hire(club, date, m);
        if (World.Managers.Any(x => x.Human && x.ClubId is { } id && World.Clubs.TryGetValue(id, out var own) && own.LeagueId == club.LeagueId))
            Notify(MessageType.Press, "msg.sender.newspaper", TR("msg.managerSacked.subject", club.Name, m.Name),
                TR("msg.managerSacked.body", club.Name, m.Name, position, successor.Name, TR("managerStyle." + successor.Traits.Style)));
    }

    /// <summary>
    /// Nachfolger: vereinsloser Trainer mit dem Ruf am nächsten zum Verein (höchstens zwei darüber), sonst ein neuer.
    /// Der gerade entlassene Trainer kommt für denselben Verein nicht in Frage.
    /// </summary>
    public Manager Hire(Club club, DateOnly date, Manager? previous)
    {
        var candidate = ManagerGenerator.FreeManagers(World)
            .Where(m => m != previous && m.Reputation <= club.Reputation + 2)
            .OrderBy(m => Math.Abs(m.Reputation - club.Reputation)).ThenByDescending(m => m.Traits.Tactics).ThenBy(m => m.Id)
            .FirstOrDefault();
        if (candidate is null || Math.Abs(candidate.Reputation - club.Reputation) > 3)
            candidate = ManagerGenerator.Create(World, Data, ManagerGenerator.RngFor(World, date.DayNumber * 31L + club.Id), club.CountryCode, club.Reputation);
        ManagerGenerator.Assign(candidate, club, date);
        return candidate;
    }
}
