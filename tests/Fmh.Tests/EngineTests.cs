using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

public class EngineTests
{
    private static (Fmh.Core.Game.GameSession s, Fixture f) Setup(ulong seed = 42)
    {
        var s = TestData.NewSession(seed);
        var f = s.World.LeagueOf(s.HumanClub).Fixtures.First(x => x.Involves(s.HumanClub.Id));
        return (s, f);
    }

    [Fact]
    public void SameSeedAndInputsGiveSameMatch()
    {
        var (s1, f1) = Setup();
        var (s2, f2) = Setup();
        var a = s1.StartMatch(f1, false);
        var b = s2.StartMatch(f2, false);
        a.RunToEnd();
        b.RunToEnd();
        Assert.Equal(a.Events.Count, b.Events.Count);
        for (var i = 0; i < a.Events.Count; i++)
        {
            Assert.Equal(a.Events[i].Type, b.Events[i].Type);
            Assert.Equal(a.Events[i].Minute, b.Events[i].Minute);
            Assert.Equal(a.Events[i].PlayerId, b.Events[i].PlayerId);
        }
        Assert.Equal(a.Home.Goals, b.Home.Goals);
        Assert.Equal(a.Away.Goals, b.Away.Goals);
    }

    [Fact]
    public void MatchHasHalvesStoppageAndSaneStatistics()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, false);
        sim.RunToEnd();
        Assert.True(sim.IsFinished);
        Assert.Single(sim.Events, e => e.Type == MatchEventType.KickOff);
        Assert.Single(sim.Events, e => e.Type == MatchEventType.HalfTime);
        Assert.Single(sim.Events, e => e.Type == MatchEventType.FullTime);
        Assert.Equal(2, sim.Events.Count(e => e.Type == MatchEventType.Stoppage));
        Assert.Equal(sim.Home.Goals + sim.Away.Goals, sim.Events.Count(e => e.Type == MatchEventType.Goal));

        var minutes = sim.Events.Select(e => e.Minute).ToList();
        Assert.All(minutes, m => Assert.InRange(m, 1, 90));
        for (var i = 1; i < minutes.Count; i++) Assert.True(minutes[i] >= minutes[i - 1] || sim.Events[i].Type == MatchEventType.SecondHalfKickOff);

        foreach (var team in new[] { sim.Home, sim.Away })
        {
            Assert.InRange(team.OnPitch.Count, 7, 11);
            Assert.True(team.SubstitutionsUsed <= sim.Setup.Substitutions);
            Assert.True(team.Stats.ShotsOnTarget <= team.Stats.Shots);
            Assert.True(team.Stats.PassesCompleted <= team.Stats.Passes);
            foreach (var mp in team.Participants)
            {
                if (mp.Grade is { } g) Assert.InRange(g, 1.0, 6.0);
                Assert.InRange(mp.Condition, 15, 100);
            }
        }
        Assert.NotNull(sim.PlayerOfTheMatchId);
    }

    [Fact]
    public void CalibrationTargetsAreRoughlyMet()
    {
        var s = TestData.NewSession(7);
        var world = s.World;
        var league = world.Leagues[0];
        double goals = 0, draws = 0, homeWins = 0, yellows = 0, reds = 0, injuries = 0;
        const int n = 600;
        var rng = new Fmh.Core.Rng(3);
        for (var i = 0; i < n; i++)
        {
            var h = league.ClubIds[rng.Next(league.ClubIds.Count)];
            int a;
            do a = league.ClubIds[rng.Next(league.ClubIds.Count)]; while (a == h);
            var sim = s.StartMatch(new Fixture { Id = 500_000 + i, CompetitionId = league.Id, HomeId = h, AwayId = a, Date = world.Date, RefereeId = world.Referees[rng.Next(world.Referees.Count)].Id }, false);
            sim.RunToEnd();
            goals += sim.Home.Goals + sim.Away.Goals;
            if (sim.Home.Goals == sim.Away.Goals) draws++;
            if (sim.Home.Goals > sim.Away.Goals) homeWins++;
            yellows += sim.Home.Stats.YellowCards + sim.Away.Stats.YellowCards;
            reds += sim.Home.Stats.RedCards + sim.Away.Stats.RedCards;
            injuries += sim.Home.Stats.Injuries + sim.Away.Stats.Injuries;
        }
        // Zielwerte aus Kapitel 6 mit großzügiger Toleranz (Stichprobe ist klein).
        Assert.InRange(goals / n, 2.4, 3.2);
        Assert.InRange(draws / n, 0.18, 0.32);
        Assert.InRange(homeWins / n, 0.37, 0.53);
        Assert.InRange(yellows / n, 3.0, 4.6);
        Assert.InRange(reds / n, 0.05, 0.4);
        Assert.InRange(injuries / (2.0 * n), 0.15, 0.45);
    }

    [Fact]
    public void HumanSubstitutionIsValidated()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, true);
        var side = f.HomeId == s.HumanClub.Id ? Side.Home : Side.Away;
        var team = sim.Team(side);
        var outId = team.OnPitch[3].Player.Id;
        var inId = team.Bench[1].Player.Id;

        Assert.False(sim.Substitute(side, inId, outId, out var err));
        Assert.Equal("sub.notOnPitch", err);
        Assert.True(sim.Substitute(side, outId, inId, out _));
        Assert.Contains(team.OnPitch, p => p.Player.Id == inId);
        Assert.Contains(team.Off, p => p.Player.Id == outId);
        Assert.False(sim.Substitute(side, inId, outId, out err));
        Assert.Equal("sub.notOnBench", err);
        Assert.Equal(1, team.SubstitutionsUsed);
        Assert.Equal(MatchEventType.Substitution, sim.Events[^1].Type);
    }

    [Fact]
    public void HumanTeamGetsNoAutomaticSubstitutions()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, true);
        var side = f.HomeId == s.HumanClub.Id ? Side.Home : Side.Away;
        sim.RunToEnd();
        Assert.Equal(0, sim.Team(side).SubstitutionsUsed);
        Assert.True(sim.Team(side.Other()).SubstitutionsUsed > 0);
    }

    [Fact]
    public void TacticChangesDuringMatchAreApplied()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, true);
        var side = f.HomeId == s.HumanClub.Id ? Side.Home : Side.Away;
        for (var i = 0; i < 20; i++) sim.Step();
        sim.ChangeMentality(side, Mentality.VeryAttacking);
        var formation = TestData.Data.Formation("352");
        sim.ChangeFormation(side, formation);
        var team = sim.Team(side);
        Assert.Equal(Mentality.VeryAttacking, team.Tactic.Mentality);
        Assert.Equal("352", team.Formation.Id);
        Assert.Equal(team.OnPitch.Count, team.OnPitch.Select(p => p.SlotIndex).Distinct().Count());
        Assert.Single(team.OnPitch, p => p.Position == Position.GK);
        sim.RunToEnd();
        Assert.True(sim.IsFinished);
    }

    [Fact]
    public void TeamTalkChangesMorale()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, true);
        var side = f.HomeId == s.HumanClub.Id ? Side.Home : Side.Away;
        var reactions = sim.TeamTalk(side, Tone.Motivating);
        Assert.Equal(sim.Team(side).OnPitch.Count + sim.Team(side).Bench.Count, reactions.Count);
        Assert.Contains(reactions, r => r.MoraleDelta > 0);
        var angry = sim.TeamTalk(side, Tone.Angry);
        Assert.True(angry.Count(r => r.MoraleDelta < 0) > angry.Count(r => r.MoraleDelta > 0));
    }

    [Fact]
    public void HalfTimeStopsUntilNextStep()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, true);
        while (!sim.IsHalfTime) sim.Step();
        Assert.InRange(sim.Minute, 45, 45);
        var next = sim.Step();
        Assert.Contains(next, e => e.Type == MatchEventType.SecondHalfKickOff);
        Assert.False(sim.IsHalfTime);
    }

    [Fact]
    public void CommentaryProducesTextForKeyEvents()
    {
        var (s, f) = Setup();
        var sim = s.StartMatch(f, false);
        sim.RunToEnd();
        var gen = new CommentaryGenerator(TestData.Data, sim);
        var lines = sim.Events.Select(e => gen.Describe(e, false)).Where(l => l is not null).ToList();
        var ticker = sim.Events.Select(e => gen.Describe(e, true)).Where(l => l is not null).ToList();
        Assert.True(lines.Count > 60, $"nur {lines.Count} Kommentarzeilen");
        Assert.True(ticker.Count < lines.Count);
        Assert.All(lines, l => Assert.DoesNotContain("{", l!.Text));
        Assert.All(lines, l => Assert.DoesNotContain("?", l!.Text.Replace("?\"", "")));
        foreach (var goal in sim.Events.Where(e => e.Type == MatchEventType.Goal))
            Assert.True(gen.Describe(goal, true)!.Highlight);
    }

    /// <summary>Fouls hängen am Wesen des Spielers, nicht an seiner Stärke: Viertligisten foulen ähnlich oft wie Erstligisten (Review F-11).</summary>
    [Fact]
    public void LowerLeaguesFoulAboutAsOftenAsTheTop()
    {
        var s = TestData.NewSession(3);
        var w = s.World;
        var rng = new Fmh.Core.Rng(3);
        double FoulsPerMatch(int level)
        {
            var leagues = w.Leagues.Where(l => l.Level == level).ToList();
            var fouls = 0;
            for (var i = 0; i < 300; i++)
            {
                var league = leagues[i % leagues.Count];
                var home = league.ClubIds[rng.Next(league.ClubIds.Count)];
                int away;
                do away = league.ClubIds[rng.Next(league.ClubIds.Count)]; while (away == home);
                var sim = s.StartMatch(new Fixture { Id = 900_000 + level * 1000 + i, CompetitionId = league.Id, HomeId = home, AwayId = away, Date = w.Date, RefereeId = w.Referees[0].Id }, humanControlled: false);
                sim.RunToEnd();
                fouls += sim.Home.Stats.Fouls + sim.Away.Stats.Fouls;
            }
            return fouls / 300.0;
        }
        var top = FoulsPerMatch(1);
        var fourth = FoulsPerMatch(4);
        Assert.True(fourth >= top * 0.75, $"Ebene 1: {top:F1}, Ebene 4: {fourth:F1} Fouls je Spiel");
    }
}
