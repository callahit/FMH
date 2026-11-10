using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Nationalmannschaften (AP 8): In jedem Länderspielfenster nominiert die KI je Land einen Kader, die Länderspiele
/// laufen mit der Engine im Schnellmodus. Danach kehren die Spieler mit Reisebelastung zu ihren Vereinen zurück.
/// Der Vereinsbetrieb ruht in den Fenstern (Liga- und Pokaltermine weichen aus).
/// </summary>
public sealed partial class InternationalsModule : GameModule
{
    public override string Id => "internationals";
    public override IReadOnlyList<string> DependsOn => new[] { "matchday" };

    private const ulong Stream = 0x4C414E444552UL;

    public override void OnDay(DayContext day)
    {
        foreach (var w in World.InternationalWindows)
        {
            if (World.Date == w.Start) OpenWindow(w);
            if (w.MatchDates.Contains(World.Date)) PlayDay(w);
            if (World.Date == w.End) CloseWindow(w);
        }
    }

    /// <summary>Fenster beginnt: Teams anlegen, Spiele ansetzen, Kader nominieren, eigenen Verein informieren.</summary>
    public void OpenWindow(InternationalWindowDates w)
    {
        NationalSquads.Ensure(World, Data);
        var real = World.NationalTeams.Where(t => !t.Background).ToList();
        // Doppelte Staatsbürger: Wer schon nominiert ist (Teams in fester Reihenfolge), fehlt in den übrigen Kadern.
        var taken = new HashSet<int>();
        foreach (var team in real)
        {
            team.Squad = NationalSquads.Nominate(World, Data, team, taken);
            taken.UnionWith(team.Squad);
        }
        ScheduleFixtures(w, real.Where(t => t.Squad.Count >= 16).ToList());
        NotifyCallups();
    }

    /// <summary>
    /// Spiele je Spieltag des Fensters: Länder mit Spielern treten gegeneinander an, bei ungerader Zahl gegen ein
    /// Hintergrundland; ohne zweites Land nur gegen Hintergrundländer. Gegner wechseln je Spieltag.
    /// </summary>
    private void ScheduleFixtures(InternationalWindowDates w, List<NationalTeam> teams)
    {
        var background = World.NationalTeams.Where(t => t.Background).ToList();
        if (teams.Count == 0 || teams.Count == 1 && background.Count == 0) return;
        for (var k = 0; k < w.MatchDates.Count; k++)
        {
            var rng = new Rng(Rng.Mix(World.Seed ^ Stream, (ulong)w.MatchDates[k].DayNumber));
            var order = teams.OrderBy(_ => rng.NextDouble()).ToList();
            for (var i = 0; i < order.Count; i += 2)
            {
                var home = order[i];
                var away = i + 1 < order.Count ? order[i + 1] : background.Count > 0 ? rng.Pick(background) : null;
                if (away is null) continue;
                if (rng.Chance(0.5) && !away.Background) (home, away) = (away, home);
                World.InternationalFixtures.Add(new InternationalFixture
                {
                    Id = World.InternationalFixtures.Count == 0 ? 1 : World.InternationalFixtures.Max(f => f.Id) + 1,
                    WindowId = w.Id,
                    Date = w.MatchDates[k],
                    HomeCode = home.Code,
                    AwayCode = away.Code,
                });
            }
        }
    }

    private void NotifyCallups()
    {
        if (Board.AwaitingNewJob) return;
        var own = World.NationalTeams.Where(t => !t.Background)
            .SelectMany(t => t.Squad.Select(id => (team: t, player: World.Players[id])))
            .Where(x => x.player.ClubId == HumanClub.Id).ToList();
        if (own.Count == 0) return;
        var lines = string.Join(", ", own.Select(x => $"{x.player.Name} ({x.team.Name})"));
        Notify(MessageType.Info, "msg.sender.association", TR("intl.callup.subject", own.Count),
            TR("intl.callup.body", lines, TextArg.OfDate(World.InternationalWindows.First(w => w.Start == World.Date).End.AddDays(1))));
    }

    /// <summary>Fenster endet: Spieler kehren zurück, die Reise kostet Kondition.</summary>
    public void CloseWindow(InternationalWindowDates w)
    {
        foreach (var team in World.NationalTeams.Where(t => !t.Background))
        {
            foreach (var id in team.Squad)
                if (World.Players.TryGetValue(id, out var p))
                    p.State.Condition = Math.Max(0, Math.Round(p.State.Condition - Data.Internationals.TravelFatigue, 1));
            team.Squad.Clear();
        }
    }

    /// <summary>Ist der Spieler gerade beim Nationalteam?</summary>
    public bool OnDuty(Player p) => World.NationalTeams.Any(t => t.Squad.Contains(p.Id));
}
