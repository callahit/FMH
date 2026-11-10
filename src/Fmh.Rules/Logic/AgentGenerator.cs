using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Spielerberater (AP 7): Berater je spielbarem Land, jeder Profi hat einen. Alles nutzt einen eigenen, aus dem Weltseed
/// und der Spieler-Id abgeleiteten Zufallsstrom; der Spielzufall bleibt unberührt.
/// </summary>
public static class AgentGenerator
{
    private const ulong Stream = 0x4245524154455221UL;

    private static Rng RngFor(World world, long salt) => new(Rng.Mix(world.Seed ^ Stream, (ulong)salt));

    /// <summary>Berater des Spielers; null, wenn (noch) keiner zugeordnet ist.</summary>
    public static Agent? AgentOf(World world, Player p) =>
        world.PlayerAgents.TryGetValue(p.Id, out var id) ? world.Agents.FirstOrDefault(a => a.Id == id) : null;

    /// <summary>
    /// Berater anlegen (je spielbarem Land nach Zahl der Profis) und allen Profis ohne Berater einen zuordnen.
    /// Wiederholbar: bestehende Zuordnungen bleiben; neue Spieler (Jugend, Neuzugänge) bekommen einen.
    /// </summary>
    public static void Ensure(World world, GameData data)
    {
        var rules = data.Agents;
        foreach (var country in world.Countries.Select(c => c.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal))
        {
            if (world.Agents.Any(a => a.Country == country)) continue;
            var pros = world.Players.Values.Count(p => !p.IsYouth && !p.Retired && p.ClubId is { } cid
                                                       && world.Clubs.TryGetValue(cid, out var c) && c.CountryCode == country);
            var count = Math.Max(rules.MinPerCountry, pros / Math.Max(1, rules.PlayersPerAgent));
            // Stabiler Hash (string.GetHashCode ist je Prozess verschieden)
            var rng = RngFor(world, country.Aggregate(17L, (h, ch) => h * 31 + ch));
            var names = data.Names.TryGetValue(country, out var n) ? n : data.Names.Values.First();
            for (var i = 0; i < count; i++)
            {
                // Wenige große, viele kleine Berater
                var reputation = (int)Math.Clamp(Math.Round(20 - Math.Pow(rng.NextDouble(), 0.6) * 18), 1, 20);
                world.Agents.Add(new Agent
                {
                    Id = world.Agents.Count == 0 ? 1 : world.Agents.Max(a => a.Id) + 1,
                    Name = $"{rng.Pick(names.FirstNames)} {rng.Pick(names.LastNames)}",
                    Country = country,
                    Reputation = reputation,
                    Greed = (int)Math.Clamp(Math.Round(rng.Normal(10, 3.5)), 1, 20),
                    Influence = (int)Math.Clamp(Math.Round(reputation * 0.5 + rng.Normal(5, 2.5)), 1, 20),
                });
            }
        }
        if (world.Agents.Count == 0) return;
        foreach (var p in world.Players.Values.Where(p => !p.IsYouth && !p.Retired && !world.PlayerAgents.ContainsKey(p.Id)).OrderBy(p => p.Id))
            Assign(world, p);
    }

    /// <summary>
    /// Berater wählen: aus dem Land des Vereins (sonst der Nationalität), gewichtet nach Nähe des Rufs zum Spieler –
    /// gute Spieler landen eher bei großen Beratern. Fest je Spieler (eigener Zufall aus der Spieler-Id).
    /// </summary>
    public static void Assign(World world, Player p)
    {
        var country = p.ClubId is { } cid && world.Clubs.TryGetValue(cid, out var club) ? club.CountryCode : p.Nationalities.FirstOrDefault() ?? "";
        var pool = world.Agents.Where(a => a.Country == country).ToList();
        if (pool.Count == 0) pool = world.Agents;
        if (pool.Count == 0) return;
        var target = Math.Clamp(p.Reputation + 2, 1, 20);
        var weights = pool.Select(a => 1.0 / (1 + Math.Abs(a.Reputation - target))).ToList();
        world.PlayerAgents[p.Id] = pool[RngFor(world, p.Id).WeightedIndex(weights)].Id;
    }

    /// <summary>Faktor auf das Beraterhonorar nach Gier (1,0 bei Gier 10).</summary>
    public static double FeeFactor(Agent? agent, AgentRules rules) => agent is null ? 1 : Math.Max(0.5, 1 + (agent.Greed - 10) * rules.FeePerGreed);

    /// <summary>Faktor auf die Gehaltsforderung nach Gier (1,0 bei Gier 10).</summary>
    public static double WageFactor(Agent? agent, AgentRules rules) => agent is null ? 1 : Math.Max(0.8, 1 + (agent.Greed - 10) * rules.WagePerGreed);

    /// <summary>Faktor auf die Chance eines Wechselwunschs nach Einfluss (1,0 bei Einfluss 10).</summary>
    public static double RequestFactor(Agent? agent, AgentRules rules) => agent is null ? 1 : Math.Max(0.5, 1 + (agent.Influence - 10) * rules.RequestPerInfluence);
}
