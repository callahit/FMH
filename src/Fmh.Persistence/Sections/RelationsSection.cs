using System.Text.Json;
using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Kabine: Spielerzufriedenheit, Versprechen, Führung und Mentoring (Must-now E3).</summary>
public sealed class RelationsSection : SaveSection
{
    public override string ModuleId => "relations";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabellen per Migration an.</summary>
    public override int Version => 6;

    private const string Tables = """
        CREATE TABLE player_relations (player_id INTEGER PRIMARY KEY, data TEXT NOT NULL);
        CREATE TABLE club_relations (club_id INTEGER PRIMARY KEY, data TEXT NOT NULL);
        CREATE TABLE promises (id INTEGER PRIMARY KEY, data TEXT NOT NULL);
        """;

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "player_relations", 2))
            foreach (var (id, r) in w.PlayerRelations.OrderBy(kv => kv.Key))
                Run(cmd, id, J(r));
        using (var cmd = Insert(con, tx, "club_relations", 2))
            foreach (var (id, r) in w.ClubRelations.OrderBy(kv => kv.Key))
                Run(cmd, id, J(r));
        using (var cmd = Insert(con, tx, "promises", 2))
            foreach (var p in w.Promises.OrderBy(x => x.Id))
                Run(cmd, p.Id, J(p));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT player_id, data FROM player_relations ORDER BY player_id"))
            w.PlayerRelations[r.GetInt32(0)] = PJ<PlayerRelations>(r.GetString(1))!;
        foreach (var r in Rows(con, "SELECT club_id, data FROM club_relations ORDER BY club_id"))
            w.ClubRelations[r.GetInt32(0)] = PJ<ClubRelations>(r.GetString(1))!;
        foreach (var r in Rows(con, "SELECT data FROM promises ORDER BY id"))
            w.Promises.Add(PJ<PlayerPromise>(r.GetString(0))!);
        w.NextPromiseId = w.Promises.Count == 0 ? 1 : w.Promises.Max(p => p.Id) + 1;
    }
}
