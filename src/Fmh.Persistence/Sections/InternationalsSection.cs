using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Nationalmannschaften (AP 8): Teams, Länderspiele, Fenster der Saison, Länderspielbilanz.</summary>
public sealed class InternationalsSection : SaveSection
{
    public override string ModuleId => "internationals";

    /// <summary>
    /// Version 6: neuer Abschnitt. Ältere Spielstände legen die Tabellen per Migration an; ohne Fenster ruht der
    /// Vereinsbetrieb erst ab der nächsten Saison (die laufenden Termine bleiben unverändert).
    /// Version 7: Turniere (AP 8b); ältere Spielstände bekommen eine leere Tabelle, das nächste Turnier wird neu angelegt.
    /// Version 8: Nationaltrainer, Ziel, Vertrag, Formation und Vormerkliste je Team (AP 8c, in den Team-Daten); ältere
    /// Spielstände haben keine Trainer, das Modul besetzt die Posten am nächsten Spieltag.
    /// </summary>
    public override int Version => 8;

    private const string TournamentTable = "CREATE TABLE international_tournaments (id TEXT PRIMARY KEY, data TEXT NOT NULL);";

    private const string Tables = """
        CREATE TABLE national_teams (code TEXT PRIMARY KEY, data TEXT NOT NULL);
        CREATE TABLE international_fixtures (id INTEGER PRIMARY KEY, data TEXT NOT NULL);
        CREATE TABLE international_windows (id TEXT PRIMARY KEY, data TEXT NOT NULL);
        CREATE TABLE international_caps (player_id INTEGER PRIMARY KEY, caps INTEGER NOT NULL, goals INTEGER NOT NULL, nation TEXT NOT NULL);
        """;

    protected override string Schema => Tables + TournamentTable;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, Tables);
        if (fromVersion <= 6) Exec(c, tx, TournamentTable);
        // Version 8 ergänzt nur Felder in den Team-Daten (JSON); fehlende Felder lesen sich als „unbesetzt“, keine Tabellenänderung.
    }

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "national_teams", 2))
            foreach (var t in w.NationalTeams)
                Run(cmd, t.Code, J(t));
        using (var cmd = Insert(con, tx, "international_fixtures", 2))
            foreach (var f in w.InternationalFixtures.OrderBy(x => x.Id))
                Run(cmd, f.Id, J(f));
        using (var cmd = Insert(con, tx, "international_windows", 2))
            foreach (var win in w.InternationalWindows)
                Run(cmd, win.Id, J(win));
        using (var cmd = Insert(con, tx, "international_caps", 4))
            foreach (var (player, c) in w.Caps.OrderBy(kv => kv.Key))
                Run(cmd, player, c.Caps, c.Goals, c.Nation);
        using (var cmd = Insert(con, tx, "international_tournaments", 2))
            foreach (var t in w.Tournaments)
                Run(cmd, t.Id, J(t));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT data FROM national_teams ORDER BY rowid"))
            w.NationalTeams.Add(PJ<NationalTeam>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT data FROM international_fixtures ORDER BY id"))
            w.InternationalFixtures.Add(PJ<InternationalFixture>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT data FROM international_windows"))
            w.InternationalWindows.Add(PJ<InternationalWindowDates>(r.GetString(0))!);
        w.InternationalWindows.Sort((a, b) => a.Start.CompareTo(b.Start));
        foreach (var r in Rows(con, "SELECT player_id, caps, goals, nation FROM international_caps ORDER BY player_id"))
            w.Caps[r.GetInt32(0)] = new InternationalCareer { Caps = r.GetInt32(1), Goals = r.GetInt32(2), Nation = r.GetString(3) };
        foreach (var r in Rows(con, "SELECT data FROM international_tournaments ORDER BY rowid"))
            w.Tournaments.Add(PJ<InternationalTournament>(r.GetString(0))!);
        // Abstellungen werden nicht beim Spieler gespeichert, sondern aus den Kadern abgeleitet.
        foreach (var id in w.NationalTeams.SelectMany(t => t.Squad))
            if (w.Players.TryGetValue(id, out var p)) p.State.OnNationalDuty = true;
    }
}
