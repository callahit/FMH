using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Personal: Mitarbeiter der Vereine und des Arbeitsmarkts.</summary>
public sealed class StaffSection : SaveSection
{
    public override string ModuleId => "staff";

    protected override string Schema => """
        CREATE TABLE staff (id INTEGER PRIMARY KEY, club_id INTEGER, name TEXT NOT NULL, role TEXT NOT NULL, ability INTEGER NOT NULL,
            data TEXT NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "staff", 6);
        foreach (var s in w.Staff) Run(cmd, s.Id, s.ClubId, s.Name, s.Role.ToString(), s.Ability, J(s));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM staff ORDER BY id"))
            w.Staff.Add(PJ<StaffMember>(r.GetString(0))!);
    }
}
