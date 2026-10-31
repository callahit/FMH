using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Spielbetrieb (Kapitel 3): Spiele vorbereiten, berechnen und auswerten, tägliche Erholung.</summary>
public sealed partial class MatchdayModule : GameModule, IMatchdayService
{
    public override string Id => "matchday";
    public override IReadOnlyList<string> DependsOn => new[] { "training" };

    /// <summary>Heutiges, noch nicht gespieltes Spiel des menschlichen Vereins.</summary>
    public Fixture? PendingHumanFixture =>
        Board.AwaitingNewJob ? null : AllFixtures.FirstOrDefault(f => f.Date == World.Date && f.Result is null && f.Involves(HumanClub.Id));

    /// <summary>Simuliert alle anderen Spiele des heutigen Tages (z. B. für die Ergebnisse im Nachbericht).</summary>
    public void PlayOtherFixturesToday()
    {
        foreach (var f in AllFixtures.Where(f => f.Date == World.Date && f.Result is null && !HumanPlays(f)).ToList())
            PlayInstant(f);
    }

    public void PlayInstant(Fixture f)
    {
        var sim = StartMatch(f, humanControlled: false);
        sim.RunToEnd();
        FinishMatch(f, sim);
    }

    /// <summary>Bereitet Aufstellungen vor und erzeugt die Simulation. Gleicher Seed + gleiche Eingaben = gleiches Spiel.</summary>
    public IMatchSimulation StartMatch(Fixture f, bool humanControlled, bool sceneText = false)
    {
        var home = World.Clubs[f.HomeId];
        var away = World.Clubs[f.AwayId];
        PrepareClub(home, away, true);
        PrepareClub(away, home, false);

        var country = World.Country(home.CountryCode);
        var referee = World.Referees.FirstOrDefault(r => r.Id == f.RefereeId);
        var cup = f.Kind == CompetitionKind.League ? null : World.FindCup(f.CompetitionId);
        var isFinal = cup is not null && f.Matchday == cup.Rounds.Count
                      && cup.Kind is CompetitionKind.Cup or CompetitionKind.Europe;
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
            UseSceneText = sceneText,
            RefereeName = referee?.Name,
            Floodlit = f.Date.Month is 11 or 12 or 1 or 2,
        };
        var sim = Engine.Create(setup);
        // Mannschaftsstimmung wirkt messbar auf Tagesform und Moral (Must-now E3-S6).
        Relations.ApplyMatchMood(sim.Home);
        Relations.ApplyMatchMood(sim.Away);
        return sim;
    }

    /// <summary>Spiel, das der Mensch selbst bestreitet (nicht während er ohne Verein ist).</summary>
    private bool HumanPlays(Fixture f) => !Board.AwaitingNewJob && f.Involves(HumanClub.Id);

    private void PrepareClub(Club club, Club opponent, bool home)
    {
        if (IsHuman(club))
            LineupSelector.Repair(club, World, Data, BenchSize(club), AiTactics.MatchEligibility(club, World, Data));
        else
            AiTactics.Prepare(club, opponent, home, World, Data, BenchSize(club));
        EnsureEleven(club);
        AiTactics.EnsureYoungPlayers(club, World, Data);
    }

    /// <summary>Notfall: zu wenige einsatzfähige Spieler. Erst Talente aus der Jugend, dann Spieler aus dem Umfeld des Vereins.</summary>
    private void EnsureEleven(Club club)
    {
        var guard = 0;
        while (club.Tactic.Lineup.Any(id => id is null) && guard++ < 11)
        {
            var youth = Youth.YouthOf(club).Where(p => p.IsAvailable).OrderByDescending(Transfers.Rating).FirstOrDefault();
            Player p;
            if (youth is not null)
            {
                Youth.PromoteYouthInternal(club, youth);
                p = youth;
            }
            else
            {
                var level = PlayerGenerator.ClubLevel(club.Reputation) - 2;
                p = PlayerGenerator.Create(World, Data, Rng, Season.NeededPosition(club), Math.Max(3, level), Rng.Next(19, 30), club.CountryCode);
                p.ClubId = club.Id;
                p.Contract!.Until = new DateOnly(World.SeasonYear + 1, 6, 30);
                p.Contract.WeeklyWage = ClubRules.LeagueWage(World, club, p.Contract.WeeklyWage);
                p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
                World.Players[p.Id] = p;
                club.PlayerIds.Add(p.Id);
                Registration.InitFormative(p, World, null, Rng);
            }
            // Notfall: wird sofort nachgemeldet.
            p.Registration.ClubId = club.Id;
            if (club.Id == HumanClub.Id)
                Notify(MessageType.Info, "msg.sender.assistant", TR("msg.emergency.subject", p.Name), TR("msg.emergency.body", p.Name));
            LineupSelector.Repair(club, World, Data, BenchSize(club), AiTactics.MatchEligibility(club, World, Data));
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
            Familiarity = Training.Familiarity(club, club.Tactic.FormationId),
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
                result.Goals.Add(new GoalRecord { Minute = e.Minute + e.Stoppage, Home = e.Side == Side.Home, ScorerId = scorer, AssistId = e.OtherPlayerId, Penalty = e.Kind == ShotKind.Penalty, OwnGoal = e.Value == 1 });
            if (e.Type is MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard && e.PlayerId is { } carded)
                result.Cards.Add(new CardRecord { Minute = e.Minute + e.Stoppage, Home = e.Side == Side.Home, PlayerId = carded, Red = e.Type != MatchEventType.YellowCard });
        }

        var (standing, seating, boxes) = Finance.Attendance(home, away, f, result.Weather);
        result.Attendance = standing + seating;
        f.Result = result;
        Finance.MatchdayFinances(f, result, standing, seating, boxes);

        // Stimmungszeile aus dem vor dem Spiel geltenden Squad Mood; danach Zufriedenheit fortschreiben.
        foreach (var club in new[] { home, away })
        {
            if (Relations.MoodReportKey(club) is { } moodKey)
                result.NoteKeys.Add(moodKey);
            Relations.RecalculateClub(club);
        }

        ServeSuspensions(home);
        ServeSuspensions(away);
        var minutes = sim.Minute;
        ApplyPlayers(sim.Home, home, sim.Home.Goals - sim.Away.Goals, minutes);
        ApplyPlayers(sim.Away, away, sim.Away.Goals - sim.Home.Goals, minutes);
        Training.AddFamiliarity(home, home.Tactic.FormationId, 2.5);
        Training.AddFamiliarity(away, away.Tactic.FormationId, 2.5);

        var participants = sim.Home.Participants.Concat(sim.Away.Participants).Select(mp => mp.Player.Id).ToHashSet();
        Bus.Publish(new MatchPlayed(f, f.Involves(HumanClub.Id) && !Board.AwaitingNewJob, participants));
    }

    private void ServeSuspensions(Club club)
    {
        foreach (var p in World.SquadOf(club))
        {
            if (p.State.SuspendedMatches <= 0) continue;
            p.State.SuspendedMatches--;
            if (p.State.SuspendedMatches == 0 && club.Id == HumanClub.Id)
                Notify(MessageType.Discipline, "msg.sender.assistant",
                    TR("msg.suspensionOver.subject", p.Name), TR("msg.suspensionOver.body", p.Name));
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
            if (goalDiff > 0 && Rng.Chance(0.5)) moraleDelta++;
            if (goalDiff < 0 && Rng.Chance(0.5)) moraleDelta--;
            if (mp.Grade is <= 2.0) moraleDelta++;
            if (mp.Grade is >= 5.0) moraleDelta--;
            p.State.Morale = (Morale)Math.Clamp((int)p.State.Morale + Math.Sign(moraleDelta), 0, 6);

            if (mp.Red)
            {
                var secondYellow = mp.Yellow;
                p.State.SuspendedMatches += secondYellow ? discipline.SecondYellowBan : Rng.Next(discipline.RedCardBanMin, discipline.RedCardBanMax);
                if (human)
                    Notify(MessageType.Discipline, "msg.sender.association",
                        TR("msg.suspension.subject", p.Name), TR("msg.suspension.body", p.Name, p.State.SuspendedMatches));
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
                            TR("msg.yellowBan.subject", p.Name), TR("msg.yellowBan.body", p.Name, discipline.YellowCardsForSuspension));
                }
            }

            if (mp.Injured && mp.InjuryType is { } type)
                InjurePlayer(p, type, mp.InjuryDays, human);
        }

        foreach (var p in World.SquadOf(club).Where(p => !participants.Contains(p.Id)))
            p.State.MatchPractice = Math.Max(0, p.State.MatchPractice - 4);
    }

    public void InjurePlayer(Player p, string type, int days, bool notify)
    {
        if (p.State.Injury is { } existing && existing.DaysRemaining >= days) return;
        // Medizinische Abteilung und Physiotherapie verkürzen Ausfallzeiten.
        if (p.ClubId is { } cid && World.Clubs.TryGetValue(cid, out var club))
        {
            var medical = Math.Min(0.45, Facilities.Effect(club, "medical.recovery") + Staff.StaffAbility(club, StaffRole.Physio) * 0.006 + Staff.StaffAbility(club, StaffRole.Doctor) * 0.006);
            days = Math.Max(1, (int)Math.Round(days * (1 - medical)));
        }
        p.State.Injury = new Injury { Type = type, DaysRemaining = days, TotalDays = days };
        if (notify)
            Notify(MessageType.Medical, "msg.sender.doctor",
                TR("msg.injury.subject", p.Name), TR("msg.injury.body", p.Name, TR("injury." + type), days));
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
                        if (Rng.Chance(0.5)) p.Attributes[Attr.Acceleration]--;
                        if (Rng.Chance(0.5)) p.Attributes[Attr.Pace]--;
                    }
                    if (p.ClubId == HumanClub.Id && !p.IsYouth)
                        Notify(MessageType.Medical, "msg.sender.doctor",
                            TR("msg.fit.subject", p.Name), TR("msg.fit.body", p.Name));
                }
            }
            else
            {
                var extra = p.ClubId is { } cid && World.Clubs.TryGetValue(cid, out var club) ? Facilities.Effect(club, "medical.condition") : 0;
                st.Condition = Math.Min(100, st.Condition + 3 + p.Attributes[Attr.NaturalFitness] * 0.25 + extra);
            }

            if (weekly && st.Morale != Morale.Okay && Rng.Chance(0.25))
                st.Morale += st.Morale > Morale.Okay ? -1 : 1;
        }
    }
}
