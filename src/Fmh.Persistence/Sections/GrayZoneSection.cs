using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Grauzonen-Akten je Manager (AP 13c).</summary>
public sealed class GrayZoneSection : SaveSection
{
    public override string ModuleId => "grayZones";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabelle an, die Akte beginnt leer.</summary>
    public override int Version => 6;

    private const string Tables = "CREATE TABLE gray_zones (manager_id INTEGER PRIMARY KEY, data TEXT NOT NULL);";

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "gray_zones", 2);
        foreach (var (id, file) in w.GrayZones.OrderBy(kv => kv.Key)) Run(cmd, id, J(file));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT manager_id, data FROM gray_zones ORDER BY manager_id"))
            w.GrayZones[r.GetInt32(0)] = PJ<GrayZoneFile>(r.GetString(1)) ?? new GrayZoneFile();
    }
}
