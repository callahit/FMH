using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Spielerberater (AP 7): Neue Profis bekommen jede Woche einen Berater; einflussreiche Berater bringen bei Mandanten,
/// die für ihren Verein zu gut sind, einen Wechsel ins Gespräch (weniger Zufriedenheit, beim eigenen Verein eine Meldung).
/// </summary>
public sealed class AgentsModule : GameModule
{
    public override string Id => "agents";
    public override IReadOnlyList<string> DependsOn => new[] { "relations" };

    public override void OnDay(DayContext day)
    {
        if (World.Date.DayOfWeek != DayOfWeek.Monday) return;
        AgentGenerator.Ensure(World, Data);
        Unsettle();
    }

    /// <summary>Wechsel ins Gespräch bringen: eigener Zufallsstrom je Woche und Spieler, der Spielzufall bleibt gleich.</summary>
    public void Unsettle()
    {
        var rules = Data.Agents.Unsettle;
        foreach (var club in World.Clubs.Values.Where(c => World.FindLeague(c.LeagueId) is not null).OrderBy(c => c.Id))
        {
            foreach (var p in World.SquadOf(club).Where(p => !p.IsYouth && p.Reputation >= club.Reputation + rules.MinReputationGap).OrderBy(p => p.Id))
            {
                if (AgentGenerator.AgentOf(World, p) is not { } agent || agent.Influence < rules.MinInfluence) continue;
                var rng = new Rng(Rng.Mix(World.Seed ^ 0x554E5345544C45UL, (ulong)(World.Date.DayNumber * 100_003L + p.Id)));
                if (!rng.Chance(rules.WeeklyChance)) continue;
                if (!World.PlayerRelations.TryGetValue(p.Id, out var rel)) continue;
                rel.Satisfaction = Math.Clamp(rel.Satisfaction - rules.SatisfactionHit, 0, 100);
                if (club.Id == HumanClub.Id && !rel.WantsTransfer)
                    Notify(MessageType.Transfer, "msg.sender.agent", TR("agent.unsettle.subject", p.Name),
                        TR("agent.unsettle.body", agent.Name, p.Name, club.Name));
            }
        }
    }
}
