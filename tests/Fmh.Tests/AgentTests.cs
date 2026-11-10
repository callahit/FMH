using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Modules;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 7: Spielerberater mit Mandanten, Gier, Einfluss und Spielstand.</summary>
public sealed class AgentTests
{
    [Fact]
    public void EveryProHasAnAgentFromThePoolAndTheWorldIsReproducible()
    {
        var s = TestData.NewSession(301);
        var w = s.World;
        var pros = w.Players.Values.Where(p => !p.IsYouth && !p.Retired).ToList();
        Assert.All(pros, p => Assert.NotNull(AgentGenerator.AgentOf(w, p)));
        var de = w.Agents.Where(a => a.Country == "DE").ToList();
        Assert.True(de.Count >= s.Data.Agents.MinPerCountry);
        Assert.InRange(pros.Count / (double)de.Count, s.Data.Agents.PlayersPerAgent * 0.8, s.Data.Agents.PlayersPerAgent * 1.25);
        Assert.Equal(de.Count, de.Select(a => a.Id).Distinct().Count());

        // Gute Spieler eher bei großen Beratern
        double MeanAgentRep(IEnumerable<Player> ps) => ps.Average(p => AgentGenerator.AgentOf(w, p)!.Reputation);
        var ranked = pros.OrderByDescending(p => p.Reputation).ToList();
        Assert.True(MeanAgentRep(ranked.Take(100)) > MeanAgentRep(ranked.TakeLast(100)));

        var again = TestData.NewSession(301).World;
        Assert.Equal(w.PlayerAgents, again.PlayerAgents);
        Assert.Equal(w.Agents.Select(a => (a.Name, a.Greed, a.Influence)), again.Agents.Select(a => (a.Name, a.Greed, a.Influence)));
    }

    [Fact]
    public void GreedRaisesWageDemandAndFeeAndInfluenceRaisesTransferRequests()
    {
        var s = TestData.NewSession(302);
        var w = s.World;
        var p = w.Players.Values.First(x => x.ClubId is not null && x.ClubId != s.HumanClub.Id && !x.IsYouth && x.Reputation >= 8);
        var agent = AgentGenerator.AgentOf(w, p)!;
        int Demand(int greed) { agent.Greed = greed; return Negotiations.WageDemand(p, s.HumanClub, SquadStatus.Regular, w, s.Data); }
        long Fee(int greed) { agent.Greed = greed; return Negotiations.AgentFee(p, 10_000_000, w, s.Data); }
        Assert.True(Demand(20) > Demand(1));
        Assert.True(Fee(20) > Fee(10));
        Assert.True(Fee(10) > Fee(1));
        agent.Influence = 20;
        Assert.True(AgentGenerator.RequestFactor(agent, s.Data.Agents) > 1);
        agent.Influence = 1;
        Assert.True(AgentGenerator.RequestFactor(agent, s.Data.Agents) < 1);
    }

    [Fact]
    public void InfluentialAgentsUnsettleClientsWhoAreTooGoodForTheirClub()
    {
        var data = PackageLoader.Load(PackageLoader.FindDataRoot(), enabledPackages: new[] { "base" });
        data.Agents.Unsettle.WeeklyChance = 1; // jede Woche, damit der Test kurz bleibt
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 303, ClubKey = "nordheide", ManagerName = "Test" });
        var s = new GameSession(world, data, new TextMatchEngine());
        var club = s.HumanClub;
        var star = world.SquadOf(club).First(p => !p.IsYouth);
        star.Reputation = Math.Min(20, club.Reputation + data.Agents.Unsettle.MinReputationGap + 2);
        AgentGenerator.AgentOf(world, star)!.Influence = 20;
        var rel = s.PlayerRelationsOf(star);
        rel.Satisfaction = 70;
        s.Modules.OfType<AgentsModule>().Single().Unsettle();
        Assert.Equal(70 - data.Agents.Unsettle.SatisfactionHit, rel.Satisfaction);
        Assert.Contains(s.Inbox, m => m.SubjectRef?.Key == "agent.unsettle.subject");

        // Nicht zu gut für den Verein: kein Einfluss
        var ordinary = world.SquadOf(club).First(p => !p.IsYouth && p.Id != star.Id && p.Reputation < club.Reputation + data.Agents.Unsettle.MinReputationGap);
        var before = s.PlayerRelationsOf(ordinary).Satisfaction;
        s.Modules.OfType<AgentsModule>().Single().Unsettle();
        Assert.Equal(before, s.PlayerRelationsOf(ordinary).Satisfaction);
    }

    [Fact]
    public void AgentsSurviveSavingAndOldSavesGetThemOnLoad()
    {
        var s = TestData.NewSession(304);
        var path = Path.Combine(Path.GetTempPath(), $"fmh-agents-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            Assert.Equal(s.World.PlayerAgents, loaded.PlayerAgents);
            Assert.Equal(s.World.Agents.Select(a => (a.Id, a.Name, a.Greed)), loaded.Agents.Select(a => (a.Id, a.Name, a.Greed)));

            // Spielstand ohne Abschnitt „agents“ (vor AP 7b): lädt, die Berater entstehen beim Start der Sitzung
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE agents; DROP TABLE agent_clients; DELETE FROM meta WHERE key = 'schema.agents';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Empty(old.Agents);
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.agents"]);
            var session = new GameSession(old, TestData.Data, new TextMatchEngine());
            Assert.All(old.Players.Values.Where(p => !p.IsYouth && !p.Retired), p => Assert.NotNull(AgentGenerator.AgentOf(old, p)));
            Assert.NotNull(session);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
