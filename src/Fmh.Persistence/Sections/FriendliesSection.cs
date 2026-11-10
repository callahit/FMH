using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Testspiele (AP 9): vereinbarte und gespielte Testspiele der laufenden Saison.</summary>
public sealed class FriendliesSection : SaveSection
{
    public override string ModuleId => "friendlies";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabelle per Migration an und haben keine Testspiele.</summary>
    public override int Version => 6;

    private const string Tables = "CREATE TABLE friendlies (id INTEGER PRIMARY KEY, data TEXT NOT NULL);";

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "friendlies", 2);
        foreach (var f in w.Friendlies.OrderBy(x => x.Fixture.Id))
            Run(cmd, f.Fixture.Id, J(f));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM friendlies ORDER BY id"))
            w.Friendlies.Add(PJ<Friendly>(r.GetString(0))!);
    }
}
