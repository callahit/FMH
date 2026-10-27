using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;
using Fmh.Core.Simulation;

namespace Fmh.Core.Game;

public sealed class AdvanceResult
{
    public StopReason Reason { get; init; }
    public Fixture? Fixture { get; init; }
    public IReadOnlyList<Message> NewMessages { get; init; } = Array.Empty<Message>();
}

/// <summary>
/// Fassade über den Spielmodulen (Umsetzungskonzept 4): Kalender und Tagesschleife. "Weiter" rechnet Tag für Tag
/// alle Module in fester Reihenfolge bis zum nächsten Stopp-Ereignis: eigenes Spiel, neue Nachrichten,
/// offene Pflicht-Entscheidungen, Saisonende. Oberflächen sprechen nur mit dieser Klasse.
/// </summary>
public sealed partial class GameSession
{
    private readonly GameKernel _kernel;
    private readonly TrainingModule _training = new();
    private readonly MatchdayModule _matchday = new();
    private readonly TransfersModule _transfers = new();
    private readonly ScoutingModule _scouting = new();
    private readonly YouthModule _youth = new();
    private readonly FacilitiesModule _facilities = new();
    private readonly FinanceModule _finance = new();
    private readonly LicenseModule _license = new();
    private readonly EconomyModule _economy = new();
    private readonly CupsModule _cups = new();
    private readonly BoardModule _board = new();
    private readonly PressModule _press = new();
    private readonly EventsModule _events = new();
    private readonly SponsorsModule _sponsors = new();
    private readonly StaffModule _staff = new();
    private readonly StadiumPlansModule _stadiumPlans = new();
    private readonly SeasonModule _season = new();

    public GameSession(World world, GameData data, IMatchEngine engine)
    {
        var rng = world.RngState.Length == 4 ? Rng.FromState(world.RngState) : new Rng(world.Seed);
        _kernel = new GameKernel(world, data, engine, rng);
        IGameModule[] modules = { _training, _matchday, _transfers, _scouting, _youth, _facilities, _finance, _license, _economy, _cups, _board, _press, _events, _sponsors, _staff, _stadiumPlans, _season };
        var ctx = new ModuleContext(_kernel);
        foreach (var m in modules) m.Register(ctx);
        Modules = ModuleRegistry.Order(modules);
        EnsureCareerData();
    }

    /// <summary>Module in der Reihenfolge ihres Tagesschritts.</summary>
    public IReadOnlyList<IGameModule> Modules { get; }

    /// <summary>Neue Spiele und ältere Spielstände: Pokale der laufenden Saison, Scouts, Budgets nachrüsten.</summary>
    private void EnsureCareerData()
    {
        if (!World.Managers.Any(m => m.Human)) return;
        WorldGenerator.InitCareer(World, Data, _kernel.Rng);
        if (!World.Scouts.Any(s => s.ClubId == HumanClub.Id)) ScoutGenerator.CreateScouts(World, HumanClub, Data, _kernel.Rng, 2);
        _economy.InitEconomy();
        if (!World.SeasonFinished && World.Cups.Count == 0 && World.Leagues.All(l => l.Fixtures.All(f => f.Result is null)))
        {
            _cups.CreateCups();
            _youth.InitialYouth();
        }
        SyncRng();
    }

    public World World => _kernel.World;
    public GameData Data => _kernel.Data;
    public IMatchEngine Engine => _kernel.Engine;

    public Manager HumanManager => _matchday.HumanManager;
    public Club HumanClub => _matchday.HumanClub;
    public Texts T => Data.Texts;

    /// <summary>Schreibt den Zufallszustand in den Spielstand (vor dem Speichern).</summary>
    public void SyncRng() => World.RngState = _kernel.Rng.GetState();

    public int BenchSize(Club club) => _matchday.BenchSize(club);

    public IEnumerable<Fixture> AllFixtures => World.AllFixtures;

    public Fixture? NextFixture(Club club) => _matchday.NextFixture(club);

    public IEnumerable<Message> Inbox => _matchday.Inbox;

    /// <summary>Ungelesene Pflicht-Entscheidungen blockieren das Weiterspielen (Kapitel 3).</summary>
    public IReadOnlyList<Message> PendingDecisions => _matchday.PendingDecisions;

    public static string SeasonLabel(int year) => GameModule.SeasonLabel(year);

    /// <summary>"Weiter": rechnet bis zum nächsten Stopp-Ereignis.</summary>
    public AdvanceResult Advance(int maxDays = 400)
    {
        var lastMessage = World.NextMessageId;
        for (var day = 0; day < maxDays; day++)
        {
            if (PendingDecisions.Count > 0)
                return new AdvanceResult { Reason = StopReason.Decision, NewMessages = PendingDecisions };
            if (PendingHumanFixture is { } fixture)
                return new AdvanceResult { Reason = StopReason.HumanMatch, Fixture = fixture };

            var stop = ProcessDay();
            World.Date = World.Date.AddDays(1);
            stop ??= StartOfDay();

            var newMessages = World.Messages.Where(m => m.Id >= lastMessage && m.ManagerId == HumanManager.Id).ToList();
            if (stop is { } reason)
                return new AdvanceResult { Reason = reason, NewMessages = newMessages };
            if (newMessages.Count > 0)
                return new AdvanceResult { Reason = newMessages.Any(m => m.Required) ? StopReason.Decision : StopReason.Messages, NewMessages = newMessages };
        }
        return new AdvanceResult { Reason = StopReason.Limit };
    }

    /// <summary>Ein Kalendertag: jedes Modul rechnet seinen Tagesschritt (Training, Spiele, Transfers, Scouting, Wettbewerbe …).</summary>
    private StopReason? ProcessDay()
    {
        var day = new DayContext(World.Date, AllFixtures.Where(f => f.Date == World.Date).ToList());
        foreach (var m in Modules) m.OnDay(day);
        return day.Stop;
    }

    /// <summary>Ereignisse zu Tagesbeginn (Saisonwechsel, Transferfenster, Vertragsende).</summary>
    private StopReason? StartOfDay()
    {
        var day = new DayContext(World.Date, Array.Empty<Fixture>());
        _kernel.Bus.Publish(new DayStarted(day));
        return day.Stop;
    }
}
