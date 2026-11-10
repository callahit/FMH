using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

public sealed partial class InternationalsModule
{
    private void PlayDay(InternationalWindowDates w)
    {
        foreach (var f in World.InternationalFixtures.Where(f => f.Date == World.Date && f.Result is null).OrderBy(f => f.Id).ToList())
            Play(f);
    }

    /// <summary>Länderspiel mit der Engine im Schnellmodus; eigener Seed je Saison und Spiel.</summary>
    public void Play(InternationalFixture f)
    {
        var home = NationalSquads.TeamOf(World, f.HomeCode)!;
        var away = NationalSquads.TeamOf(World, f.AwayCode)!;
        var setup = new MatchSetup
        {
            Home = SetupFor(home, -1),
            Away = SetupFor(away, -2),
            Config = Data.Engine,
            Profiles = Data.PositionProfiles,
            Roles = Data.Roles,
            InjuryTypes = Data.Injuries,
            Seed = Rng.Mix(World.Seed ^ Stream, (ulong)(World.SeasonYear * 10_000L + f.Id)),
            InjuryFrequency = World.Rules.InjuryFrequency,
            Substitutions = Data.Internationals.Substitutions,
            Floodlit = f.Date.Month is 11 or 12 or 1 or 2 or 3,
        };
        var sim = Engine.Create(setup);
        sim.RunToEnd();
        f.Result = new MatchResult
        {
            HomeGoals = sim.Home.Goals,
            AwayGoals = sim.Away.Goals,
            HomeXg = Math.Round(sim.Home.Stats.Xg, 2),
            AwayXg = Math.Round(sim.Away.Stats.Xg, 2),
            HomeShots = sim.Home.Stats.Shots,
            AwayShots = sim.Away.Stats.Shots,
        };
        foreach (var e in sim.Events.Where(e => e.Type == MatchEventType.Goal && e.PlayerId is > 0))
            f.Result.Goals.Add(new GoalRecord { Minute = e.Minute + e.Stoppage, Home = e.Side == Side.Home, ScorerId = e.PlayerId!.Value,
                AssistId = e.OtherPlayerId is > 0 ? e.OtherPlayerId : null, Penalty = e.Kind == ShotKind.Penalty, OwnGoal = e.Value == 1 });
        Record(home, sim.Home.Goals, sim.Away.Goals);
        Record(away, sim.Away.Goals, sim.Home.Goals);
        ApplyPlayers(sim.Home, home, sim.Minute);
        ApplyPlayers(sim.Away, away, sim.Minute);
    }

    private TeamSetup SetupFor(NationalTeam team, int pseudoId)
    {
        var players = team.Background
            ? NationalSquads.BackgroundSquad(World, Data, team)
            : team.Squad.Select(id => World.Players[id]).Where(p => p.IsAvailable).ToList();
        var tactic = new Tactic { FormationId = Data.Internationals.Formation };
        var formation = TacticResolver.Resolve(tactic, Data);
        tactic.Lineup = LineupSelector.Assign(formation, players, Data.PositionProfiles);
        tactic.Bench = LineupSelector.PickBench(tactic.Lineup, players, 9, Data.PositionProfiles);
        var byId = players.ToDictionary(p => p.Id);
        return new TeamSetup
        {
            // Nur für Namen in Ticker und Kommentar; das Nationalteam ist kein Verein der Welt.
            Club = new Club { Id = pseudoId, Name = team.Name, ShortName = team.Name, CountryCode = team.Code },
            Tactic = tactic,
            Formation = formation,
            Orders = TacticResolver.Orders(tactic, formation, Data),
            Starting = tactic.Lineup.Where(id => id.HasValue).Select(id => byId[id!.Value]).ToList(),
            Bench = tactic.Bench.Select(id => byId[id]).ToList(),
        };
    }

    private static void Record(NationalTeam t, int goalsFor, int goalsAgainst)
    {
        t.Played++;
        t.GoalsFor += goalsFor;
        t.GoalsAgainst += goalsAgainst;
        if (goalsFor > goalsAgainst) t.Won++;
        else if (goalsFor == goalsAgainst) t.Drawn++;
        else t.Lost++;
    }

    /// <summary>Belastung, Verletzungen und Länderspielbilanz der echten Spieler (Hintergrundkader zählen nicht).</summary>
    private void ApplyPlayers(MatchTeamState state, NationalTeam team, int matchMinutes)
    {
        if (team.Background) return;
        foreach (var mp in state.Participants)
        {
            var p = mp.Player;
            if (!World.Players.ContainsKey(p.Id)) continue;
            p.State.Condition = Math.Round(mp.Condition, 1);
            p.State.MatchPractice = Math.Min(100, p.State.MatchPractice + mp.MinutesPlayed(matchMinutes) / 90.0 * 15);
            if (!World.Caps.TryGetValue(p.Id, out var career)) World.Caps[p.Id] = career = new InternationalCareer();
            career.Caps++;
            career.Goals += mp.Goals;
            career.Nation = team.Code;
            if (mp.Injured && mp.InjuryType is { } type)
                Matchday.InjurePlayer(p, type, mp.InjuryDays, notify: !Board.AwaitingNewJob && p.ClubId == HumanClub.Id);
        }
    }
}
