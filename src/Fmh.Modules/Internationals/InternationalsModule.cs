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
        foreach (var w in World.InternationalWindows.Where(w => World.Date == w.Start)) OpenWindow(w);
        EnsureTournaments();
        PlayToday();
        TournamentDay();
        foreach (var w in World.InternationalWindows.Where(w => World.Date == w.End)) CloseWindow(w);
    }

    /// <summary>
    /// Fenster beginnt: Teams anlegen, Kader nominieren, eigenen Verein informieren. Freundschaftsspiele nur in Saisons
    /// ohne Qualifikation; sonst bestreiten die Teams ihre Qualifikationsspiele.
    /// </summary>
    public void OpenWindow(InternationalWindowDates w)
    {
        NationalSquads.Ensure(World, Data);
        EnsureTournaments();
        var real = World.NationalTeams.Where(t => !t.Background).ToList();
        // Doppelte Staatsbürger: Wer schon nominiert ist (Teams in fester Reihenfolge), fehlt in den übrigen Kadern.
        var taken = new HashSet<int>();
        foreach (var team in real)
        {
            NationalSquads.Call(World, team, NationalSquads.Nominate(World, Data, team, taken));
            taken.UnionWith(team.Squad);
        }
        if (!World.InternationalFixtures.Any(f => f.TournamentId is not null && w.Contains(f.Date)))
            ScheduleFixtures(w, real.Where(t => t.Squad.Count >= MinSquad).ToList());
        NotifyCallups(TR("intl.callup.body", CallupLines(), TextArg.OfDate(w.End.AddDays(1))));
    }

    /// <summary>Mindestzahl gesunder Nationalspieler, damit ein Land antritt.</summary>
    private const int MinSquad = 16;

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

    private string CallupLines() => string.Join(", ", OwnCallups().Select(x => $"{x.player.Name} ({x.team.Name})"));

    private List<(NationalTeam team, Player player)> OwnCallups() => Board.AwaitingNewJob
        ? new()
        : World.NationalTeams.Where(t => !t.Background)
            .SelectMany(t => t.Squad.Select(id => (team: t, player: World.Players[id])))
            .Where(x => x.player.ClubId == HumanClub.Id).ToList();

    private void NotifyCallups(TextRef body)
    {
        var own = OwnCallups();
        if (own.Count == 0) return;
        Notify(MessageType.Info, "msg.sender.association", TR("intl.callup.subject", own.Count), body);
    }

    /// <summary>Fenster endet: Spieler kehren zurück, die Reise kostet Kondition.</summary>
    public void CloseWindow(InternationalWindowDates w)
    {
        foreach (var team in World.NationalTeams.Where(t => !t.Background)) NationalSquads.Release(World, Data, team);
    }

    /// <summary>Ist der Spieler gerade beim Nationalteam?</summary>
    public bool OnDuty(Player p) => World.NationalTeams.Any(t => t.Squad.Contains(p.Id));
}
