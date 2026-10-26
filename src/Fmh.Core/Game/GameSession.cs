using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

public enum StopReason { HumanMatch, Messages, Decision, SeasonEnd, NewSeason, Limit }

public sealed class AdvanceResult
{
    public StopReason Reason { get; init; }
    public Fixture? Fixture { get; init; }
    public IReadOnlyList<Message> NewMessages { get; init; } = Array.Empty<Message>();
}

/// <summary>
/// Kern-Modul: Kalender und Tagesablauf (Kapitel 3). "Weiter" berechnet Tag für Tag alle Wettbewerbe,
/// Training, Transfers und Scouting bis zum nächsten Stopp-Ereignis: eigenes Spiel, neue Nachrichten,
/// offene Pflicht-Entscheidungen, Saisonende.
/// </summary>
public sealed partial class GameSession
{
    private readonly Rng _rng;

    public GameSession(World world, GameData data, IMatchEngine engine)
    {
        World = world;
        Data = data;
        Engine = engine;
        _rng = world.RngState.Length == 4 ? Rng.FromState(world.RngState) : new Rng(world.Seed);
        EnsureCareerData();
    }

    /// <summary>Neue Spiele und ältere Spielstände: Pokale der laufenden Saison, Scouts, Budgets nachrüsten.</summary>
    private void EnsureCareerData()
    {
        if (!World.Managers.Any(m => m.Human)) return;
        WorldGenerator.InitCareer(World, Data, _rng);
        if (!World.Scouts.Any(s => s.ClubId == HumanClub.Id)) CreateScouts(World, HumanClub, Data, _rng, 2);
        if (!World.SeasonFinished && World.Cups.Count == 0 && World.Leagues.All(l => l.Fixtures.All(f => f.Result is null)))
        {
            CreateCups();
            InitialYouth();
        }
        SyncRng();
    }

    public World World { get; }
    public GameData Data { get; }
    public IMatchEngine Engine { get; }

    public Manager HumanManager => World.Managers.First(m => m.Human);
    public Club HumanClub => World.Clubs[HumanManager.ClubId!.Value];
    public Texts T => Data.Texts;

    /// <summary>Schreibt den Zufallszustand in den Spielstand (vor dem Speichern).</summary>
    public void SyncRng() => World.RngState = _rng.GetState();

    public int BenchSize(Club club) => World.Country(club.CountryCode).Squad.BenchSize;

    public IEnumerable<Fixture> AllFixtures => World.AllFixtures;

    /// <summary>Heutiges, noch nicht gespieltes Spiel des menschlichen Vereins.</summary>
    public Fixture? PendingHumanFixture =>
        AllFixtures.FirstOrDefault(f => f.Date == World.Date && f.Result is null && f.Involves(HumanClub.Id));

    public Fixture? NextFixture(Club club) =>
        AllFixtures.Where(f => f.Result is null && f.Involves(club.Id)).OrderBy(f => f.Date).FirstOrDefault();

    public IEnumerable<Message> Inbox => World.Messages.Where(m => m.ManagerId == HumanManager.Id).OrderByDescending(m => m.Id);

    /// <summary>Ungelesene Pflicht-Entscheidungen blockieren das Weiterspielen (Kapitel 3).</summary>
    public IReadOnlyList<Message> PendingDecisions => Inbox.Where(m => m.Required && !m.Resolved).ToList();

    private Message Notify(MessageType type, string senderKey, string subject, string body,
                           MessageAction action = MessageAction.None, int? refId = null, bool required = false)
    {
        var m = Messages.Add(World, HumanManager, type, T[senderKey], subject, body);
        m.Action = action;
        m.ActionRefId = refId;
        m.Required = required;
        return m;
    }

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

    /// <summary>Ein Kalendertag: Training, Spiele, Erholung, Transfers, Scouting, Wettbewerbe.</summary>
    private StopReason? ProcessDay()
    {
        var today = AllFixtures.Where(f => f.Date == World.Date).ToList();
        var playing = today.SelectMany(f => new[] { f.HomeId, f.AwayId }).ToHashSet();
        RunTraining(playing);

        foreach (var f in today.Where(f => f.Result is null))
        {
            if (f.Involves(HumanClub.Id)) continue;
            PlayInstant(f);
        }
        DailyUpdate();
        MarketDaily();
        ScoutingDaily();
        YouthDaily();

        if (!World.SeasonFinished && CompetitionsDaily())
        {
            FinishSeason();
            return StopReason.SeasonEnd;
        }
        return null;
    }

    /// <summary>Ereignisse zu Tagesbeginn (Saisonwechsel, Transferfenster, Vertragsende).</summary>
    private StopReason? StartOfDay()
    {
        if (StartNewSeasonIfDue()) return StopReason.NewSeason;
        CalendarEvents();
        return null;
    }

    /// <summary>Simuliert alle anderen Spiele des heutigen Tages (z. B. für die Ergebnisse im Nachbericht).</summary>
    public void PlayOtherFixturesToday()
    {
        foreach (var f in AllFixtures.Where(f => f.Date == World.Date && f.Result is null && !f.Involves(HumanClub.Id)).ToList())
            PlayInstant(f);
    }

    public void PlayInstant(Fixture f)
    {
        var sim = StartMatch(f, humanControlled: false);
        sim.RunToEnd();
        FinishMatch(f, sim);
    }

    /// <summary>Bereitet Aufstellungen vor und erzeugt die Simulation. Gleicher Seed + gleiche Eingaben = gleiches Spiel.</summary>
    public IMatchSimulation StartMatch(Fixture f, bool humanControlled)
    {
        var home = World.Clubs[f.HomeId];
        var away = World.Clubs[f.AwayId];
        PrepareClub(home, away, true);
        PrepareClub(away, home, false);

        var country = World.Country(home.CountryCode);
        var referee = World.Referees.FirstOrDefault(r => r.Id == f.RefereeId);
        var cup = f.Kind == CompetitionKind.League ? null : World.FindCup(f.CompetitionId);
        var isFinal = cup is not null && cup.Kind == CompetitionKind.Cup && f.Matchday == cup.Rounds.Count;
        var setup = new MatchSetup
        {
            Home = TeamSetupFor(home, humanControlled && home.Id == HumanClub.Id),
            Away = TeamSetupFor(away, humanControlled && away.Id == HumanClub.Id),
            Config = Data.Engine,
            Profiles = Data.PositionProfiles,
            Roles = Data.Roles,
            InjuryTypes = Data.Injuries,
            Seed = Rng.Mix(World.Seed, (ulong)f.Id),
            RefereeStrictness = referee?.Strictness ?? 10,
            Weather = WeatherFor(f),
            InjuryFrequency = World.Rules.InjuryFrequency,
            Substitutions = country.Squad.Substitutions,
            Knockout = cup is null ? null : CupLogic.KnockoutFor(cup, f),
            NeutralGround = isFinal && cup!.Config.NeutralFinal,
        };
        return Engine.Create(setup);
    }

    private bool IsHuman(Club club) => club.ManagerId is { } mid && World.Managers.First(m => m.Id == mid).Human;

    private void PrepareClub(Club club, Club opponent, bool home)
    {
        if (IsHuman(club))
            LineupSelector.Repair(club, World, Data, BenchSize(club));
        else
            AiTactics.Prepare(club, opponent, home, World, Data, BenchSize(club));
        EnsureEleven(club);
    }

    /// <summary>Notfall: zu wenige einsatzfähige Spieler. Erst Talente aus der Jugend, dann Spieler aus dem Umfeld des Vereins.</summary>
    private void EnsureEleven(Club club)
    {
        var guard = 0;
        while (club.Tactic.Lineup.Any(id => id is null) && guard++ < 11)
        {
            var youth = YouthOf(club).Where(p => p.IsAvailable).OrderByDescending(Rating).FirstOrDefault();
            Player p;
            if (youth is not null)
            {
                PromoteYouthInternal(club, youth);
                p = youth;
            }
            else
            {
                var level = PlayerGenerator.ClubLevel(club.Reputation) - 2;
                p = PlayerGenerator.Create(World, Data, _rng, NeededPosition(club), Math.Max(3, level), _rng.Next(19, 30), club.CountryCode);
                p.ClubId = club.Id;
                p.Contract!.Until = new DateOnly(World.SeasonYear + 1, 6, 30);
                p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
                World.Players[p.Id] = p;
                club.PlayerIds.Add(p.Id);
            }
            if (club.Id == HumanClub.Id)
                Notify(MessageType.Info, "msg.sender.assistant", T.Format("msg.emergency.subject", p.Name), T.Format("msg.emergency.body", p.Name));
            LineupSelector.Repair(club, World, Data, BenchSize(club));
        }
    }

    private TeamSetup TeamSetupFor(Club club, bool human)
    {
        var formation = TacticResolver.Resolve(club.Tactic, Data);
        return new TeamSetup
        {
            Club = club,
            Tactic = club.Tactic.Clone(),
            Formation = formation,
            Orders = TacticResolver.Orders(club.Tactic, formation, Data),
            Familiarity = Familiarity(club, club.Tactic.FormationId),
            Starting = club.Tactic.Lineup.Select(id => World.Players[id!.Value]).ToList(),
            Bench = club.Tactic.Bench.Select(id => World.Players[id]).Where(p => p.IsAvailable).ToList(),
            HumanControlled = human,
        };
    }

    /// <summary>Wetter nach Jahreszeit, deterministisch je Spiel.</summary>
    public Weather WeatherFor(Fixture f)
    {
        var rng = new Rng(Rng.Mix(World.Seed ^ 0x5EA7_7E57UL, (ulong)f.Id));
        var winter = f.Date.Month is 12 or 1 or 2;
        var w = winter ? new[] { 0.2, 0.35, 0.28, 0.12, 0.05 } : new[] { 0.42, 0.3, 0.18, 0.0, 0.1 };
        return (Weather)rng.WeightedIndex(w);
    }

    /// <summary>Überträgt das Spielergebnis in Tabelle, Statistiken, Spielerzustände, Sperren und Verletzungen.</summary>
    public void FinishMatch(Fixture f, IMatchSimulation sim)
    {
        if (!sim.IsFinished) throw new InvalidOperationException("Spiel ist noch nicht beendet.");
        var home = World.Clubs[f.HomeId];
        var away = World.Clubs[f.AwayId];
        var result = new MatchResult
        {
            HomeGoals = sim.Home.Goals,
            AwayGoals = sim.Away.Goals,
            ExtraTime = sim.Events.Any(e => e.Type == MatchEventType.ExtraTimeStart),
            PenaltiesHome = sim.PenaltiesHome,
            PenaltiesAway = sim.PenaltiesAway,
            Weather = sim.Setup.Weather,
            HomeXg = Math.Round(sim.Home.Stats.Xg, 2),
            AwayXg = Math.Round(sim.Away.Stats.Xg, 2),
            HomeShots = sim.Home.Stats.Shots,
            AwayShots = sim.Away.Stats.Shots,
            PlayerOfTheMatchId = sim.PlayerOfTheMatchId,
        };
        var total = sim.Home.Stats.PossessionSeconds + sim.Away.Stats.PossessionSeconds;
        result.HomePossession = total <= 0 ? 50 : (int)Math.Round(100 * sim.Home.Stats.PossessionSeconds / total);

        var halfTime = sim.Events.FirstOrDefault(e => e.Type == MatchEventType.HalfTime);
        result.HalfTimeHome = halfTime?.HomeScore ?? 0;
        result.HalfTimeAway = halfTime?.AwayScore ?? 0;

        foreach (var e in sim.Events)
        {
            if (e.Type == MatchEventType.Goal && e.PlayerId is { } scorer)
                result.Goals.Add(new GoalRecord { Minute = e.Minute + e.Stoppage, Home = e.Side == Side.Home, ScorerId = scorer, AssistId = e.OtherPlayerId, Penalty = e.Kind == ShotKind.Penalty });
            if (e.Type is MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard && e.PlayerId is { } carded)
                result.Cards.Add(new CardRecord { Minute = e.Minute + e.Stoppage, Home = e.Side == Side.Home, PlayerId = carded, Red = e.Type != MatchEventType.YellowCard });
        }

        var rng = new Rng(Rng.Mix(sim.Setup.Seed, 0xA77E_17DAUL));
        var bigGame = f.Kind != CompetitionKind.League ? 0.12 : 0;
        var demand = 0.5 + home.Reputation * 0.02 + away.Reputation * 0.012 + rng.Range(-0.05, 0.05) + bigGame
                     - (result.Weather is Weather.Rain or Weather.Snow ? 0.06 : 0);
        result.Attendance = (int)(home.StadiumCapacity * Math.Clamp(demand, 0.3, 1.0));
        f.Result = result;

        ServeSuspensions(home);
        ServeSuspensions(away);
        var minutes = sim.Minute;
        ApplyPlayers(sim.Home, home, sim.Home.Goals - sim.Away.Goals, minutes);
        ApplyPlayers(sim.Away, away, sim.Away.Goals - sim.Home.Goals, minutes);
        AddFamiliarity(home, home.Tactic.FormationId, 2.5);
        AddFamiliarity(away, away.Tactic.FormationId, 2.5);

        if (f.Kind != CompetitionKind.League && World.FindCup(f.CompetitionId) is { } cup)
            CupMatchPlayed(cup, f);
    }

    private void ServeSuspensions(Club club)
    {
        foreach (var p in World.SquadOf(club))
        {
            if (p.State.SuspendedMatches <= 0) continue;
            p.State.SuspendedMatches--;
            if (p.State.SuspendedMatches == 0 && club.Id == HumanClub.Id)
                Notify(MessageType.Discipline, "msg.sender.assistant",
                    T.Format("msg.suspensionOver.subject", p.Name), T.Format("msg.suspensionOver.body", p.Name));
        }
    }

    private void ApplyPlayers(MatchTeamState team, Club club, int goalDiff, int matchMinutes)
    {
        var discipline = World.Country(club.CountryCode).Discipline;
        var participants = team.Participants.Select(mp => mp.Player.Id).ToHashSet();
        var human = club.Id == HumanClub.Id;

        foreach (var mp in team.Participants)
        {
            var p = mp.Player;
            var s = EnsureSeasonStats(p, club);
            var minutes = mp.MinutesPlayed(matchMinutes);
            s.Appearances++;
            s.Minutes += minutes;
            s.Goals += mp.Goals;
            s.Assists += mp.Assists;
            if (mp.Yellow && !mp.Red) s.YellowCards++;
            if (mp.Red) s.RedCards++;
            if (mp.Grade is { } g)
            {
                s.GradedMatches++;
                s.GradeSum += g;
                p.State.AddGrade(g);
            }
            p.State.Condition = Math.Round(mp.Condition, 1);
            p.State.MatchPractice = Math.Min(100, p.State.MatchPractice + minutes / 90.0 * 22);

            // Moral nach Ergebnis und eigener Leistung.
            var moraleDelta = 0;
            if (goalDiff > 0 && _rng.Chance(0.5)) moraleDelta++;
            if (goalDiff < 0 && _rng.Chance(0.5)) moraleDelta--;
            if (mp.Grade is <= 2.0) moraleDelta++;
            if (mp.Grade is >= 5.0) moraleDelta--;
            p.State.Morale = (Morale)Math.Clamp((int)p.State.Morale + Math.Sign(moraleDelta), 0, 6);

            if (mp.Red)
            {
                var secondYellow = mp.Yellow;
                p.State.SuspendedMatches += secondYellow ? discipline.SecondYellowBan : _rng.Next(discipline.RedCardBanMin, discipline.RedCardBanMax);
                if (human)
                    Notify(MessageType.Discipline, "msg.sender.association",
                        T.Format("msg.suspension.subject", p.Name), T.Format("msg.suspension.body", p.Name, p.State.SuspendedMatches));
            }
            else if (mp.Yellow)
            {
                p.State.YellowCardsAccumulated++;
                if (discipline.YellowCardsForSuspension > 0 && p.State.YellowCardsAccumulated >= discipline.YellowCardsForSuspension)
                {
                    p.State.YellowCardsAccumulated = 0;
                    p.State.SuspendedMatches += 1;
                    if (human)
                        Notify(MessageType.Discipline, "msg.sender.association",
                            T.Format("msg.yellowBan.subject", p.Name), T.Format("msg.yellowBan.body", p.Name, discipline.YellowCardsForSuspension));
                }
            }

            if (mp.Injured && mp.InjuryType is { } type)
                InjurePlayer(p, type, mp.InjuryDays, human);
        }

        foreach (var p in World.SquadOf(club).Where(p => !participants.Contains(p.Id)))
            p.State.MatchPractice = Math.Max(0, p.State.MatchPractice - 4);
    }

    private void InjurePlayer(Player p, string type, int days, bool notify)
    {
        if (p.State.Injury is { } existing && existing.DaysRemaining >= days) return;
        p.State.Injury = new Injury { Type = type, DaysRemaining = days, TotalDays = days };
        if (notify)
            Notify(MessageType.Medical, "msg.sender.doctor",
                T.Format("msg.injury.subject", p.Name), T.Format("msg.injury.body", p.Name, T["injury." + type], days));
    }

    private PlayerSeasonStats EnsureSeasonStats(Player p, Club club)
    {
        if (p.SeasonStats.SeasonYear != World.SeasonYear || p.SeasonStats.ClubId != club.Id)
        {
            if (p.SeasonStats.Appearances > 0) p.History.Add(p.SeasonStats);
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
        }
        return p.SeasonStats;
    }

    /// <summary>Tagesroutine: Erholung, Heilung, Moral pendelt langsam zur Mitte.</summary>
    private void DailyUpdate()
    {
        var weekly = World.Date.DayOfWeek == DayOfWeek.Monday;
        foreach (var p in World.Players.Values)
        {
            if (p.Retired) continue;
            var st = p.State;
            if (st.Injury is { } inj)
            {
                inj.DaysRemaining--;
                st.Condition = Math.Min(100, st.Condition + 1);
                if (inj.DaysRemaining <= 0)
                {
                    st.Injury = null;
                    st.Condition = Math.Min(st.Condition, 80);
                    // Schwere Verletzungen (über 3 Monate) können Antritt und Schnelligkeit dauerhaft senken.
                    if (inj.TotalDays > 90)
                    {
                        if (_rng.Chance(0.5)) p.Attributes[Attr.Acceleration]--;
                        if (_rng.Chance(0.5)) p.Attributes[Attr.Pace]--;
                    }
                    if (p.ClubId == HumanClub.Id && !p.IsYouth)
                        Notify(MessageType.Medical, "msg.sender.doctor",
                            T.Format("msg.fit.subject", p.Name), T.Format("msg.fit.body", p.Name));
                }
            }
            else
            {
                st.Condition = Math.Min(100, st.Condition + 3 + p.Attributes[Attr.NaturalFitness] * 0.25);
            }

            if (weekly && st.Morale != Morale.Okay && _rng.Chance(0.25))
                st.Morale += st.Morale > Morale.Okay ? -1 : 1;
        }
    }

    public static string SeasonLabel(int year) => $"{year}/{(year + 1) % 100:00}";
}
