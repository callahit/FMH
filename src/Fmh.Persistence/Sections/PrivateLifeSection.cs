using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Privatleben je Manager (AP 13b).</summary>
public sealed class PrivateLifeSection : SaveSection
{
    public override string ModuleId => "privateLife";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabelle an, das Privatleben beginnt neu.</summary>
    public override int Version => 6;

    private const string Tables = "CREATE TABLE private_lives (manager_id INTEGER PRIMARY KEY, data TEXT NOT NULL);";

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "private_lives", 2);
        foreach (var (id, life) in w.PrivateLives.OrderBy(kv => kv.Key)) Run(cmd, id, J(life));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT manager_id, data FROM private_lives ORDER BY manager_id"))
            w.PrivateLives[r.GetInt32(0)] = PJ<PrivateLife>(r.GetString(1)) ?? new PrivateLife();
    }
}
