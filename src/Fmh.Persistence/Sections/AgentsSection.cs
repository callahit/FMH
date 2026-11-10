using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Berater (AP 7): Spielerberater und ihre Mandanten.</summary>
public sealed class AgentsSection : SaveSection
{
    public override string ModuleId => "agents";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabellen per Migration an, die Berater entstehen beim Laden.</summary>
    public override int Version => 6;

    private const string Tables = """
        CREATE TABLE agents (id INTEGER PRIMARY KEY, data TEXT NOT NULL);
        CREATE TABLE agent_clients (player_id INTEGER PRIMARY KEY, agent_id INTEGER NOT NULL);
        """;

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "agents", 2))
            foreach (var a in w.Agents.OrderBy(x => x.Id))
                Run(cmd, a.Id, J(a));
        using (var cmd = Insert(con, tx, "agent_clients", 2))
            foreach (var (player, agent) in w.PlayerAgents.OrderBy(kv => kv.Key))
                Run(cmd, player, agent);
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM agents ORDER BY id"))
            w.Agents.Add(PJ<Agent>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT player_id, agent_id FROM agent_clients ORDER BY player_id"))
            w.PlayerAgents[r.GetInt32(0)] = r.GetInt32(1);
    }
}
