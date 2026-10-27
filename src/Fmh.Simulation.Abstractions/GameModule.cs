using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Simulation;

/// <summary>
/// Basis aller Spielmodule: Zugriff auf den gemeinsamen Kern (Spielstand, Daten, Zufall), den Posteingang des
/// menschlichen Managers und die Dienste der anderen Module. Eigener Zustand eines Moduls liegt im Spielstand.
/// </summary>
public abstract class GameModule : IGameModule
{
    private GameKernel? _kernel;

    public abstract string Id { get; }
    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public virtual void Register(ModuleContext ctx)
    {
        _kernel = ctx.Kernel;
        ctx.Services.Add(this);
    }

    public virtual void OnDay(DayContext day) { }

    protected GameKernel Kernel => _kernel ?? throw new InvalidOperationException($"Modul {Id} ist nicht registriert.");
    public World World => Kernel.World;
    public GameData Data => Kernel.Data;
    protected IMatchEngine Engine => Kernel.Engine;
    protected Rng Rng => Kernel.Rng;
    protected EventBus Bus => Kernel.Bus;

    // ------------------------------------------------------------ Posteingang und Texte (zentrale Dienste)

    public Manager HumanManager => World.Managers.First(m => m.Human);
    public Club HumanClub => World.Clubs[HumanManager.ClubId!.Value];
    public Texts T => Data.Texts;

    public int BenchSize(Club club) => World.Country(club.CountryCode).Squad.BenchSize;

    public IEnumerable<Fixture> AllFixtures => World.AllFixtures;

    public Fixture? NextFixture(Club club) =>
        AllFixtures.Where(f => f.Result is null && f.Involves(club.Id)).OrderBy(f => f.Date).FirstOrDefault();

    public IEnumerable<Message> Inbox => World.Messages.Where(m => m.ManagerId == HumanManager.Id).OrderByDescending(m => m.Id);

    /// <summary>Ungelesene Pflicht-Entscheidungen blockieren das Weiterspielen (Kapitel 3).</summary>
    public IReadOnlyList<Message> PendingDecisions => Inbox.Where(m => m.Required && !m.Resolved).ToList();

    protected static TextRef TR(string key, params object?[] args) => TextRef.Of(key, args);

    /// <summary>Wettbewerbsname (Liga, Pokal, Relegation) als übersetzbarer Text.</summary>
    protected TextArg Comp(string id) => Messages.Competition(World, id);

    /// <summary>Rundenname eines Pokals als übersetzbarer Text.</summary>
    protected static TextArg Round(CupRound r) => r.NameKey is { Length: > 0 } k ? TextArg.Of(TR(k, r.Number)) : TextArg.Literal(r.Name);

    protected Message Notify(MessageType type, string senderKey, TextRef subject, TextRef body,
                             MessageAction action = MessageAction.None, int? refId = null, bool required = false)
    {
        var m = Messages.Add(World, HumanManager, type, senderKey, subject, body);
        m.Action = action;
        m.ActionRefId = refId;
        m.Required = required;
        return m;
    }

    protected bool IsHuman(Club club) => club.ManagerId is { } mid && World.Managers.First(m => m.Id == mid).Human;

    /// <summary>Spielwährung (immer Euro; die Anzeige rechnet in die gewählte Währung um).</summary>
    protected static string Currency => CurrencyConfig.Base;

    public static string SeasonLabel(int year) => $"{year}/{(year + 1) % 100:00}";

    // ------------------------------------------------------------ Dienste der anderen Module

    private IMatchdayService? _matchday;
    private ISeasonService? _season;
    private ICupsService? _cups;
    private ITransfersService? _transfers;
    private ITrainingService? _training;
    private IScoutingService? _scouting;
    private IYouthService? _youth;
    private IFinanceService? _finance;
    private IEconomyService? _economy;
    private IFacilitiesService? _facilities;
    private IStadiumPlansService? _stadiumPlans;
    private IStaffService? _staff;
    private ISponsorsService? _sponsors;
    private IBoardService? _board;
    private IPressService? _press;
    private IEventsService? _events;
    private ILicenseService? _license;

    protected IMatchdayService Matchday => _matchday ??= Kernel.Services.Get<IMatchdayService>();
    protected ISeasonService Season => _season ??= Kernel.Services.Get<ISeasonService>();
    protected ICupsService Cups => _cups ??= Kernel.Services.Get<ICupsService>();
    protected ITransfersService Transfers => _transfers ??= Kernel.Services.Get<ITransfersService>();
    protected ITrainingService Training => _training ??= Kernel.Services.Get<ITrainingService>();
    protected IScoutingService Scouting => _scouting ??= Kernel.Services.Get<IScoutingService>();
    protected IYouthService Youth => _youth ??= Kernel.Services.Get<IYouthService>();
    protected IFinanceService Finance => _finance ??= Kernel.Services.Get<IFinanceService>();
    protected IEconomyService Economy => _economy ??= Kernel.Services.Get<IEconomyService>();
    protected IFacilitiesService Facilities => _facilities ??= Kernel.Services.Get<IFacilitiesService>();
    protected IStadiumPlansService StadiumPlans => _stadiumPlans ??= Kernel.Services.Get<IStadiumPlansService>();
    protected IStaffService Staff => _staff ??= Kernel.Services.Get<IStaffService>();
    protected ISponsorsService Sponsors => _sponsors ??= Kernel.Services.Get<ISponsorsService>();
    protected IBoardService Board => _board ??= Kernel.Services.Get<IBoardService>();
    protected IPressService Press => _press ??= Kernel.Services.Get<IPressService>();
    protected IEventsService Events => _events ??= Kernel.Services.Get<IEventsService>();
    protected ILicenseService License => _license ??= Kernel.Services.Get<ILicenseService>();
}
