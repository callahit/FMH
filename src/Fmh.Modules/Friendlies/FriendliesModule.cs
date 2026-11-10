using Fmh.Core.Data;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Testspiele (AP 9): Der Manager vereinbart Gegner, Datum, Ort und Einnahmenteilung. Bekannte Vereine kommen nur
/// auswärts und nur bis zu einem Rufabstand. Im Trainingslager findet das Spiel auf neutralem Platz statt.
/// </summary>
public sealed class FriendliesModule : GameModule
{
    public override string Id => "friendlies";
    public override IReadOnlyList<string> DependsOn => new[] { "matchday" };

    private FriendlyRules Rules => Data.Friendlies;

    /// <summary>Gegner, die grundsätzlich in Frage kommen: alle Vereine der Ligen außer dem eigenen und den eigenen Reserven.</summary>
    public IReadOnlyList<Club> Candidates() => World.Clubs.Values
        .Where(c => c.Id != HumanClub.Id && c.ReserveOf != HumanClub.Id && World.FindLeague(c.LeagueId) is not null)
        .OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();

    /// <summary>Freie Tage für ein Testspiel gegen <paramref name="opponent"/> in den nächsten Wochen.</summary>
    public IReadOnlyList<DateOnly> FreeDates(Club opponent, int days = 60)
    {
        var result = new List<DateOnly>();
        for (var d = World.Date.AddDays(1); d <= World.Date.AddDays(Math.Min(days, Rules.MaxDaysAhead)); d = d.AddDays(1))
            if (DateError(opponent, d) is null) result.Add(d);
        return result;
    }

    private string? DateError(Club opponent, DateOnly date)
    {
        if (date <= World.Date || date > World.Date.AddDays(Rules.MaxDaysAhead)) return "friendly.error.date";
        if (World.InInternationalWindow(date)) return "friendly.error.window";
        var rest = Rules.MinRestDays;
        bool Busy(Club c) => World.AllMatches.Any(f => f.Involves(c.Id) && Math.Abs(f.Date.DayNumber - date.DayNumber) <= rest);
        if (Busy(HumanClub)) return "friendly.error.busy";
        if (Busy(opponent)) return "friendly.error.opponentBusy";
        return null;
    }

    /// <summary>Testspiel vereinbaren. Fehlertext-Schlüssel oder null.</summary>
    public string? Arrange(Club opponent, DateOnly date, bool home, int homeSharePercent)
    {
        var own = HumanClub;
        if (opponent.Id == own.Id) return "friendly.error.opponent";
        if (!Rules.HomeShares.Contains(homeSharePercent)) return "friendly.error.share";
        if (World.Friendlies.Count(f => f.Fixture.Result is null && f.Fixture.Involves(own.Id)) >= Rules.MaxOpen) return "friendly.error.tooMany";
        if (DateError(opponent, date) is { } error) return error;
        var camp = own.Camp is { } c && c.Start <= date && c.End >= date;
        var gap = opponent.Reputation - own.Reputation;
        if (!camp && gap > Rules.AcceptReputationGap && (home || gap > 2 * Rules.AcceptReputationGap)) return "friendly.error.declined";
        var friendly = new Friendly
        {
            HomeSharePercent = homeSharePercent,
            Camp = camp,
        };
        var f = friendly.Fixture;
        f.Id = World.NextFixtureId++;
        f.Date = date;
        f.HomeId = home ? own.Id : opponent.Id;
        f.AwayId = home ? opponent.Id : own.Id;
        f.RefereeId = World.Referees.Count == 0 ? 0 : World.Referees[(int)((uint)f.Id % (uint)World.Referees.Count)].Id;
        World.Friendlies.Add(friendly);
        return null;
    }

    /// <summary>Noch nicht gespieltes Testspiel absagen.</summary>
    public void Cancel(Friendly friendly)
    {
        if (friendly.Fixture.Result is null && friendly.Fixture.Date > World.Date) World.Friendlies.Remove(friendly);
    }

    /// <summary>
    /// Täglich: Testspiele der Vorsaison verschwinden; ein offenes Testspiel, das mit einem später angesetzten Pflichtspiel
    /// kollidiert (z. B. Pokalrunde), wird abgesagt – Pflichtspiele haben Vorrang.
    /// </summary>
    public override void OnDay(DayContext day)
    {
        var start = new DateOnly(World.SeasonYear, World.Countries[0].Season.StartMonth, 1);
        World.Friendlies.RemoveAll(f => f.Fixture.Result is not null && f.Fixture.Date < start);
        var rest = Rules.MinRestDays;
        foreach (var friendly in World.Friendlies.Where(f => f.Fixture.Result is null && f.Fixture.Date >= World.Date).ToList())
        {
            var f = friendly.Fixture;
            if (!World.AllFixtures.Any(c => (c.Involves(f.HomeId) || c.Involves(f.AwayId)) && Math.Abs(c.Date.DayNumber - f.Date.DayNumber) < Math.Max(1, rest)))
                continue;
            World.Friendlies.Remove(friendly);
            if (!Board.AwaitingNewJob && f.Involves(HumanClub.Id))
            {
                var opponent = World.Clubs[f.HomeId == HumanClub.Id ? f.AwayId : f.HomeId];
                Notify(MessageType.Info, "msg.sender.assistant", TR("friendly.cancelled.subject", opponent.Name),
                    TR("friendly.cancelled.body", opponent.Name, TextArg.OfDate(f.Date)));
            }
        }
    }
}
