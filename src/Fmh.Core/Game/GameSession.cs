using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

public enum StopReason { HumanMatch, Messages, SeasonEnd, NewSeason, Limit }

public sealed class AdvanceResult
{
    public StopReason Reason { get; init; }
    public Fixture? Fixture { get; init; }
    public IReadOnlyList<Message> NewMessages { get; init; } = Array.Empty<Message>();
}

/// <summary>
/// Kern-Modul: Kalender und Tagesablauf (Kapitel 3). "Weiter" berechnet Tag für Tag alle Ligen
/// bis zum nächsten Stopp-Ereignis: eigener Spieltag, neue Nachrichten, Saisonende.
/// </summary>
public sealed class GameSession
{
    private readonly Rng _rng;

    public GameSession(World world, GameData data, IMatchEngine engine)
    {
        World = world;
        Data = data;
        Engine = engine;
        _rng = world.RngState.Length == 4 ? Rng.FromState(world.RngState) : new Rng(world.Seed);
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

    public IEnumerable<Fixture> AllFixtures => World.Leagues.SelectMany(l => l.Fixtures);

    /// <summary>Heutiges, noch nicht gespieltes Spiel des menschlichen Vereins.</summary>
    public Fixture? PendingHumanFixture =>
        AllFixtures.FirstOrDefault(f => f.Date == World.Date && f.Result is null && f.Involves(HumanClub.Id));

    public Fixture? NextFixture(Club club) =>
        AllFixtures.Where(f => f.Result is null && f.Involves(club.Id)).OrderBy(f => f.Date).FirstOrDefault();

    public IEnumerable<Message> Inbox => World.Messages.Where(m => m.ManagerId == HumanManager.Id).OrderByDescending(m => m.Id);

    /// <summary>"Weiter": rechnet bis zum nächsten Stopp-Ereignis.</summary>
    public AdvanceResult Advance(int maxDays = 400)
    {
        var lastMessage = World.NextMessageId;
        for (var day = 0; day < maxDays; day++)
        {
            if (PendingHumanFixture is { } fixture)
                return new AdvanceResult { Reason = StopReason.HumanMatch, Fixture = fixture };

            var stop = ProcessDay();
            World.Date = World.Date.AddDays(1);

            if (StartNewSeasonIfDue())
                stop ??= StopReason.NewSeason;

            var newMessages = World.Messages.Where(m => m.Id >= lastMessage && m.ManagerId == HumanManager.Id).ToList();
            if (stop is { } reason)
                return new AdvanceResult { Reason = reason, NewMessages = newMessages };
            if (newMessages.Count > 0)
                return new AdvanceResult { Reason = StopReason.Messages, NewMessages = newMessages };
        }
        return new AdvanceResult { Reason = StopReason.Limit };
    }

    /// <summary>Simuliert alle übrigen Spiele des Tages und die Tagesroutine.</summary>
    private StopReason? ProcessDay()
    {
        foreach (var f in AllFixtures.Where(f => f.Date == World.Date && f.Result is null).ToList())
        {
            if (f.Involves(HumanClub.Id)) continue;
            PlayInstant(f);
        }
        DailyUpdate();

        if (!World.SeasonFinished && World.Leagues.All(l => l.IsComplete))
        {
            FinishSeason();
            return StopReason.SeasonEnd;
        }
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
        var setup = new MatchSetup
        {
            Home = TeamSetupFor(home, humanControlled && home.Id == HumanClub.Id),
            Away = TeamSetupFor(away, humanControlled && away.Id == HumanClub.Id),
            Config = Data.Engine,
            Profiles = Data.PositionProfiles,
            InjuryTypes = Data.Injuries,
            Seed = Rng.Mix(World.Seed, (ulong)f.Id),
            RefereeStrictness = referee?.Strictness ?? 10,
            Weather = WeatherFor(f),
            InjuryFrequency = World.Rules.InjuryFrequency,
            Substitutions = country.Squad.Substitutions,
        };
        return Engine.Create(setup);
    }

    private void PrepareClub(Club club, Club opponent, bool home)
    {
        if (club.ManagerId is { } mid && World.Managers.First(m => m.Id == mid).Human)
            LineupSelector.Repair(club, World, Data, BenchSize(club));
        else
            AiTactics.Prepare(club, opponent, home, World, Data, BenchSize(club));
    }

    private TeamSetup TeamSetupFor(Club club, bool human)
    {
        var formation = Data.Formation(club.Tactic.FormationId);
        return new TeamSetup
        {
            Club = club,
            Tactic = club.Tactic.Clone(),
            Formation = formation,
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
        var demand = 0.5 + home.Reputation * 0.02 + away.Reputation * 0.012 + rng.Range(-0.05, 0.05)
                     - (result.Weather is Weather.Rain or Weather.Snow ? 0.06 : 0);
        result.Attendance = (int)(home.StadiumCapacity * Math.Clamp(demand, 0.3, 1.0));
        f.Result = result;

        ServeSuspensions(home);
        ServeSuspensions(away);
        ApplyPlayers(sim.Home, home, sim.Home.Goals - sim.Away.Goals);
        ApplyPlayers(sim.Away, away, sim.Away.Goals - sim.Home.Goals);
    }

    private void ServeSuspensions(Club club)
    {
        foreach (var p in World.SquadOf(club))
        {
            if (p.State.SuspendedMatches <= 0) continue;
            p.State.SuspendedMatches--;
            if (p.State.SuspendedMatches == 0 && club.Id == HumanClub.Id)
                Messages.Add(World, HumanManager, MessageType.Discipline, T["msg.sender.assistant"],
                    T.Format("msg.suspensionOver.subject", p.Name), T.Format("msg.suspensionOver.body", p.Name));
        }
    }

    private void ApplyPlayers(MatchTeamState team, Club club, int goalDiff)
    {
        var discipline = World.Country(club.CountryCode).Discipline;
        var participants = team.Participants.Select(mp => mp.Player.Id).ToHashSet();
        var human = club.Id == HumanClub.Id;

        foreach (var mp in team.Participants)
        {
            var p = mp.Player;
            var s = EnsureSeasonStats(p, club);
            var minutes = mp.MinutesPlayed(90);
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
                    Messages.Add(World, HumanManager, MessageType.Discipline, T["msg.sender.association"],
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
                        Messages.Add(World, HumanManager, MessageType.Discipline, T["msg.sender.association"],
                            T.Format("msg.yellowBan.subject", p.Name), T.Format("msg.yellowBan.body", p.Name, discipline.YellowCardsForSuspension));
                }
            }

            if (mp.Injured && mp.InjuryType is { } type)
            {
                p.State.Injury = new Injury { Type = type, DaysRemaining = mp.InjuryDays, TotalDays = mp.InjuryDays };
                if (human)
                    Messages.Add(World, HumanManager, MessageType.Medical, T["msg.sender.doctor"],
                        T.Format("msg.injury.subject", p.Name), T.Format("msg.injury.body", p.Name, T["injury." + type], mp.InjuryDays));
            }
        }

        foreach (var p in World.SquadOf(club).Where(p => !participants.Contains(p.Id)))
            p.State.MatchPractice = Math.Max(0, p.State.MatchPractice - 4);
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
                    if (p.ClubId == HumanClub.Id)
                        Messages.Add(World, HumanManager, MessageType.Medical, T["msg.sender.doctor"],
                            T.Format("msg.fit.subject", p.Name), T.Format("msg.fit.body", p.Name));
                }
            }
            else
            {
                st.Condition = Math.Min(100, st.Condition + 4 + p.Attributes[Attr.NaturalFitness] * 0.3);
            }

            if (weekly && st.Morale != Morale.Okay && _rng.Chance(0.25))
                st.Morale += st.Morale > Morale.Okay ? -1 : 1;
        }
    }

    /// <summary>Saisonabschluss: Meister, Endtabellen, Bilanz für den Manager.</summary>
    private void FinishSeason()
    {
        World.SeasonFinished = true;
        foreach (var league in World.Leagues)
        {
            var table = LeagueTable.Compute(league);
            var champion = World.Clubs[table[0].ClubId];
            World.History.Add(new SeasonRecord
            {
                SeasonYear = World.SeasonYear,
                LeagueId = league.Id,
                LeagueName = league.Name,
                ChampionId = champion.Id,
                ChampionName = champion.Name,
                FinalOrder = table.Select(r => r.ClubId).ToList(),
            });
        }

        var own = HumanClub;
        var ownLeague = World.LeagueOf(own);
        var ownTable = LeagueTable.Compute(ownLeague);
        var row = ownTable.First(r => r.ClubId == own.Id);
        var champ = World.Clubs[ownTable[0].ClubId];
        var verdictKey = row.Position <= ownLeague.Format.Promotion ? "msg.seasonEnd.promoted"
            : row.Position > ownTable.Count - ownLeague.Format.Relegation ? "msg.seasonEnd.relegated"
            : row.Position == 1 ? "msg.seasonEnd.champion" : "msg.seasonEnd.neutral";
        Messages.Add(World, HumanManager, MessageType.Season, T["msg.sender.league"],
            T.Format("msg.seasonEnd.subject", ownLeague.Name, SeasonLabel(World.SeasonYear)),
            T.Format("msg.seasonEnd.body", champ.Name, row.Position, row.Points, T.Format(verdictKey, own.Name)));
    }

    public static string SeasonLabel(int year) => $"{year}/{(year + 1) % 100:00}";

    /// <summary>Zum Saisonstart: Auf- und Abstieg, Entwicklung, Karriereenden, Nachwuchs, neuer Spielplan.</summary>
    private bool StartNewSeasonIfDue()
    {
        if (!World.SeasonFinished) return false;
        var startMonth = World.Countries[0].Season.StartMonth;
        if (World.Date != new DateOnly(World.SeasonYear + 1, startMonth, 1)) return false;

        PromoteAndRelegate();
        World.SeasonYear++;
        World.SeasonFinished = false;

        foreach (var club in World.Clubs.Values)
            RenewSquad(club);

        foreach (var league in World.Leagues)
            ScheduleGenerator.CreateFixtures(World, league, World.Country(league.CountryCode), _rng);

        foreach (var club in World.Clubs.Values)
        {
            if (club.Id != HumanClub.Id) club.Tactic.FormationId = AiTactics.BestFormation(club, World, Data).Id;
            LineupSelector.Repair(club, World, Data, BenchSize(club));
        }

        Messages.Add(World, HumanManager, MessageType.Board, T["msg.sender.board"],
            T.Format("msg.newSeason.subject", SeasonLabel(World.SeasonYear)),
            T.Format("msg.newSeason.body", HumanClub.Name, World.LeagueOf(HumanClub).Name));
        return true;
    }

    private void PromoteAndRelegate()
    {
        foreach (var country in World.Countries)
        {
            var leagues = World.Leagues.Where(l => l.CountryCode == country.Code).OrderBy(l => l.Level).ToList();
            for (var i = 0; i < leagues.Count - 1; i++)
            {
                var upper = leagues[i];
                var lower = leagues[i + 1];
                var count = Math.Min(upper.Format.Relegation, lower.Format.Promotion);
                if (count <= 0) continue;
                var down = LeagueTable.Compute(upper).TakeLast(count).Select(r => r.ClubId).ToList();
                var up = LeagueTable.Compute(lower).Take(count).Select(r => r.ClubId).ToList();
                foreach (var id in down) { upper.ClubIds.Remove(id); lower.ClubIds.Add(id); World.Clubs[id].LeagueId = lower.Id; }
                foreach (var id in up) { lower.ClubIds.Remove(id); upper.ClubIds.Add(id); World.Clubs[id].LeagueId = upper.Id; }
            }
        }
    }

    private void RenewSquad(Club club)
    {
        var today = World.Date;
        var squad = World.SquadOf(club).ToList();
        foreach (var p in squad)
        {
            Development.ApplySeason(p, today, Data.PositionProfiles, _rng);
            if (p.SeasonStats.Appearances > 0) p.History.Add(p.SeasonStats);
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
            p.State.YellowCardsAccumulated = 0;
            p.State.Condition = Math.Max(p.State.Condition, 92);
            p.State.Morale = Morale.Okay;
            // Ohne Transfermarkt (Phase 2) verlängern Verträge automatisch.
            if (p.Contract is { } c && c.Until < today)
                c.Until = new DateOnly(World.SeasonYear + _rng.Next(1, 3), 6, 30);
        }

        var retiring = squad.Where(p => _rng.Chance(Development.RetirementChance(p, today))).ToList();
        foreach (var p in retiring)
        {
            club.PlayerIds.Remove(p.Id);
            p.ClubId = null;
            if (club.Id == HumanClub.Id)
                Messages.Add(World, HumanManager, MessageType.Info, T["msg.sender.assistant"],
                    T.Format("msg.retired.subject", p.Name), T.Format("msg.retired.body", p.Name, p.Age(today)));
        }

        // Nachwuchs: Abgänge positionsgleich durch Talente aus der eigenen Jugend ersetzen.
        var level = PlayerGenerator.ClubLevel(club.Reputation);
        foreach (var gone in retiring)
        {
            var age = _rng.Next(17, 19);
            var p = PlayerGenerator.Create(World, Data, _rng, gone.MainPosition,
                Math.Clamp(level - 2.5 + _rng.Normal(0, 1.2), 3, 18), age, club.CountryCode);
            p.ClubId = club.Id;
            World.Players[p.Id] = p;
            club.PlayerIds.Add(p.Id);
            if (club.Id == HumanClub.Id)
                Messages.Add(World, HumanManager, MessageType.Info, T["msg.sender.youth"],
                    T.Format("msg.youth.subject", p.Name), T.Format("msg.youth.body", p.Name, age, T[PositionInfo.TextKey(p.MainPosition)]));
        }
        // Spieler im Ruhestand bleiben in der Welt, damit Historie und Torschützen auflösbar bleiben.
    }
}
