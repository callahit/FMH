using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Spielmodus (AP 12a): Schnellsaison oder Herausforderung mit Ziel und Ergebnis; leer bei einer Karriere.</summary>
public sealed class ModeSection : SaveSection
{
    public override string ModuleId => "challenge";

    /// <summary>Version 6: neuer Abschnitt; ältere Spielstände legen die Tabelle per Migration an und sind Karrieren.</summary>
    public override int Version => 6;

    private const string Tables = "CREATE TABLE game_mode (id INTEGER PRIMARY KEY, data TEXT NOT NULL);";

    protected override string Schema => Tables;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        if (w.Mode is null) return;
        using var cmd = Insert(con, tx, "game_mode", 2);
        Run(cmd, 1, J(w.Mode));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM game_mode WHERE id = 1"))
            w.Mode = PJ<GameModeState>(r.GetString(0));
    }
}
