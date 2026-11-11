using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Anstoß-Schalter je Manager (AP 13); nur Abweichungen von der Voreinstellung „alles an“.</summary>
public sealed class AnstossSection : SaveSection
{
    public override string ModuleId => "anstoss";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabelle per Migration an, alles ist an.</summary>
    public override int Version => 6;

    private const string Tables = "CREATE TABLE anstoss_settings (manager_id INTEGER PRIMARY KEY, data TEXT NOT NULL);";

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "anstoss_settings", 2);
        foreach (var (id, s) in w.Anstoss.OrderBy(kv => kv.Key)) Run(cmd, id, J(s));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT manager_id, data FROM anstoss_settings ORDER BY manager_id"))
            w.Anstoss[r.GetInt32(0)] = PJ<AnstossSettings>(r.GetString(1)) ?? new AnstossSettings();
    }
}
