using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Ereignisse: offene Ereignisse und wann welches zuletzt eingetreten ist.</summary>
public sealed class EventsSection : SaveSection
{
    public override string ModuleId => "events";

    protected override string Schema => """
        CREATE TABLE events (id INTEGER PRIMARY KEY, event_id TEXT NOT NULL, date TEXT NOT NULL, chosen TEXT, data TEXT NOT NULL);
        CREATE TABLE event_history (event_id TEXT PRIMARY KEY, date TEXT NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "events", 5))
            foreach (var e in w.Events) Run(cmd, e.Id, e.EventId, D(e.Date), e.Chosen, J(e));
        using (var cmd = Insert(con, tx, "event_history", 2))
            foreach (var (id, d) in w.EventHistory) Run(cmd, id, D(d));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM events ORDER BY id"))
            w.Events.Add(PJ<PendingEvent>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT event_id, date FROM event_history"))
            w.EventHistory[r.GetString(0)] = PD(r.GetString(1));
    }
}
