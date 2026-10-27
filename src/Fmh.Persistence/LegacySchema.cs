using Fmh.Core.Model;
using Fmh.Core.Persistence.Sections;
using Microsoft.Data.Sqlite;

namespace Fmh.Core.Persistence;

/// <summary>
/// Spielstände vor der Modularisierung (globale Schemaversion 1 bis 5). Ältere Stände werden hier bis zur
/// Ausgangsversion 5 angehoben; danach migriert jeder Speicherabschnitt seine Tabellen selbst.
/// </summary>
public static class LegacySchema
{
    private static string AttrCol(Attr a) => PlayersSection.AttrCol(a);
    private static string[] HiddenCols => PlayersSection.HiddenCols;

    /// <summary>Migrationsschritte: Schlüssel = Ausgangsversion; das SQL hebt die Datenbank um eine Version an.</summary>
    public static readonly Dictionary<int, string> Migrations = new()
    {
        // Phase 2: Pokal, Relegation, Transfers, Verträge, Training, Scouting, Jugend.
        [1] = """
            ALTER TABLE fixtures RENAME COLUMN league_id TO competition_id;
            ALTER TABLE fixtures ADD COLUMN kind TEXT NOT NULL DEFAULT 'League';
            ALTER TABLE fixtures ADD COLUMN tie_id INTEGER;
            ALTER TABLE fixtures ADD COLUMN leg INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fixtures ADD COLUMN extra_time INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fixtures ADD COLUMN pens_home INTEGER;
            ALTER TABLE fixtures ADD COLUMN pens_away INTEGER;
            ALTER TABLE clubs ADD COLUMN balance INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE clubs ADD COLUMN transfer_budget INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE clubs ADD COLUMN wage_budget INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE clubs ADD COLUMN coach_quality INTEGER NOT NULL DEFAULT 10;
            ALTER TABLE clubs ADD COLUMN youth_facilities INTEGER NOT NULL DEFAULT 10;
            ALTER TABLE clubs ADD COLUMN youth_recruitment INTEGER NOT NULL DEFAULT 10;
            ALTER TABLE clubs ADD COLUMN saved_tactics TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN familiarity TEXT NOT NULL DEFAULT '{}';
            ALTER TABLE clubs ADD COLUMN training TEXT;
            ALTER TABLE clubs ADD COLUMN camp TEXT;
            ALTER TABLE clubs ADD COLUMN youth_ids TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN shortlist TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN player_ids TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE players ADD COLUMN contract TEXT;
            ALTER TABLE players ADD COLUMN is_youth INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE players ADD COLUMN retired INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE players ADD COLUMN transfer_listed INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE players ADD COLUMN asking_price INTEGER;
            ALTER TABLE players ADD COLUMN transfer TEXT;
            ALTER TABLE players ADD COLUMN loan TEXT;
            ALTER TABLE players ADD COLUMN training TEXT;
            ALTER TABLE players ADD COLUMN pre_contract TEXT;
            ALTER TABLE messages ADD COLUMN action TEXT NOT NULL DEFAULT 'None';
            ALTER TABLE messages ADD COLUMN action_ref INTEGER;
            ALTER TABLE messages ADD COLUMN required INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE messages ADD COLUMN resolved INTEGER NOT NULL DEFAULT 0;
            CREATE TABLE cups (id TEXT PRIMARY KEY, ord INTEGER NOT NULL, name TEXT NOT NULL, country TEXT NOT NULL, kind TEXT NOT NULL,
                season_year INTEGER NOT NULL, winner_id INTEGER, data TEXT NOT NULL);
            CREATE TABLE transfers (ord INTEGER NOT NULL, date TEXT NOT NULL, player_id INTEGER NOT NULL, from_club INTEGER, to_club INTEGER,
                fee INTEGER NOT NULL, loan INTEGER NOT NULL, season_year INTEGER NOT NULL);
            CREATE INDEX transfers_player ON transfers (player_id);
            CREATE TABLE transactions (ord INTEGER NOT NULL, date TEXT NOT NULL, club_id INTEGER NOT NULL, type TEXT NOT NULL,
                amount INTEGER NOT NULL, text TEXT NOT NULL);
            CREATE INDEX transactions_club ON transactions (club_id);
            CREATE TABLE installments (ord INTEGER NOT NULL, from_club INTEGER NOT NULL, to_club INTEGER NOT NULL, due TEXT NOT NULL,
                amount INTEGER NOT NULL, text TEXT NOT NULL);
            CREATE TABLE negotiations (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL, buyer_id INTEGER NOT NULL, seller_id INTEGER,
                type TEXT NOT NULL, stage TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE negotiation_locks (key TEXT PRIMARY KEY, until TEXT NOT NULL);
            CREATE TABLE scouts (id INTEGER PRIMARY KEY, club_id INTEGER NOT NULL, name TEXT NOT NULL, judging_ability INTEGER NOT NULL,
                judging_potential INTEGER NOT NULL, region_knowledge INTEGER NOT NULL, region TEXT NOT NULL);
            CREATE TABLE scout_assignments (id INTEGER PRIMARY KEY, scout_id INTEGER NOT NULL, task TEXT NOT NULL, player_id INTEGER,
                league_id TEXT, position TEXT, max_age INTEGER NOT NULL, start_date TEXT NOT NULL, end_date TEXT NOT NULL);
            CREATE TABLE scout_reports (player_id INTEGER PRIMARY KEY, date TEXT NOT NULL, strength REAL NOT NULL, potential REAL NOT NULL,
                recommendation TEXT NOT NULL, summary TEXT NOT NULL);
            CREATE TABLE knowledge (player_id INTEGER PRIMARY KEY, value INTEGER NOT NULL);
            """,
        // Phase 3, Mehrsprachigkeit: Texte als sprachneutrale Verweise (Schlüssel + Argumente, JSON).
        [2] = """
            ALTER TABLE messages ADD COLUMN sender_key TEXT;
            ALTER TABLE messages ADD COLUMN subject_ref TEXT;
            ALTER TABLE messages ADD COLUMN body_ref TEXT;
            ALTER TABLE transactions ADD COLUMN text_ref TEXT;
            ALTER TABLE installments ADD COLUMN text_ref TEXT;
            ALTER TABLE scout_reports ADD COLUMN summary_ref TEXT;
            """,
        // Phase 3: Finanzen, Stadion, Gelände, Sponsoren, Vorstand, Fans, Personal, Ereignisse, Managerkarriere.
        [3] = """
            ALTER TABLE clubs ADD COLUMN finance TEXT;
            ALTER TABLE clubs ADD COLUMN stadium_data TEXT;
            ALTER TABLE clubs ADD COLUMN grounds TEXT;
            ALTER TABLE clubs ADD COLUMN sponsors TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN sponsor_offers TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE clubs ADD COLUMN board TEXT;
            ALTER TABLE clubs ADD COLUMN fans TEXT;
            ALTER TABLE clubs ADD COLUMN rivals TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE managers ADD COLUMN career TEXT;
            ALTER TABLE leagues ADD COLUMN deductions TEXT NOT NULL DEFAULT '{}';
            CREATE TABLE staff (id INTEGER PRIMARY KEY, club_id INTEGER, name TEXT NOT NULL, role TEXT NOT NULL, ability INTEGER NOT NULL,
                data TEXT NOT NULL);
            CREATE TABLE events (id INTEGER PRIMARY KEY, event_id TEXT NOT NULL, date TEXT NOT NULL, chosen TEXT, data TEXT NOT NULL);
            CREATE TABLE event_history (event_id TEXT PRIMARY KEY, date TEXT NOT NULL);
            """,
        // Nachwuchsleistungszentrum, Lizenzakte mit FFP, Europapokalteilnehmer.
        [4] = """
            ALTER TABLE clubs ADD COLUMN academy TEXT;
            ALTER TABLE clubs ADD COLUMN license TEXT;
            CREATE TABLE european_entries (club_id INTEGER NOT NULL, competition TEXT NOT NULL, season_year INTEGER NOT NULL,
                stage INTEGER NOT NULL, eliminated INTEGER NOT NULL, replacement INTEGER NOT NULL);
            """,
    };

    public static string V1Schema() => $"""
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE countries (code TEXT PRIMARY KEY, config TEXT NOT NULL);
        CREATE TABLE referees (id INTEGER PRIMARY KEY, name TEXT NOT NULL, strictness INTEGER NOT NULL);
        CREATE TABLE managers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, human INTEGER NOT NULL, club_id INTEGER,
            reputation INTEGER NOT NULL, profile TEXT NOT NULL);
        CREATE TABLE clubs (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, name TEXT NOT NULL, short_name TEXT NOT NULL,
            city TEXT NOT NULL, country TEXT NOT NULL, reputation INTEGER NOT NULL, primary_color TEXT NOT NULL,
            secondary_color TEXT NOT NULL, stadium TEXT NOT NULL, capacity INTEGER NOT NULL, league_id TEXT NOT NULL,
            manager_id INTEGER, tactic TEXT NOT NULL);
        CREATE TABLE players (id INTEGER PRIMARY KEY, first_name TEXT NOT NULL, last_name TEXT NOT NULL, birth_date TEXT NOT NULL,
            nationalities TEXT NOT NULL, height INTEGER NOT NULL, weight INTEGER NOT NULL, left_foot INTEGER NOT NULL,
            right_foot INTEGER NOT NULL, main_position TEXT NOT NULL, club_id INTEGER, reputation INTEGER NOT NULL,
            contract_wage INTEGER, contract_until TEXT,
            {string.Join(", ", AttrInfo.All.Select(a => AttrCol(a) + " INTEGER NOT NULL"))},
            {string.Join(", ", HiddenCols.Select(c => c + " INTEGER NOT NULL"))},
            condition REAL NOT NULL, match_practice REAL NOT NULL, morale TEXT NOT NULL, recent_grades TEXT NOT NULL,
            injury_type TEXT, injury_days INTEGER, injury_total INTEGER, suspended INTEGER NOT NULL, yellow_acc INTEGER NOT NULL);
        CREATE INDEX players_club ON players (club_id);
        CREATE TABLE player_positions (player_id INTEGER NOT NULL, position TEXT NOT NULL, suitability TEXT NOT NULL,
            PRIMARY KEY (player_id, position));
        CREATE TABLE player_seasons (player_id INTEGER NOT NULL, current INTEGER NOT NULL, ord INTEGER NOT NULL,
            season_year INTEGER NOT NULL, club_id INTEGER, apps INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL,
            assists INTEGER NOT NULL, yellow INTEGER NOT NULL, red INTEGER NOT NULL, graded INTEGER NOT NULL, grade_sum REAL NOT NULL);
        CREATE INDEX player_seasons_player ON player_seasons (player_id);
        CREATE TABLE leagues (id TEXT PRIMARY KEY, ord INTEGER NOT NULL, name TEXT NOT NULL, country TEXT NOT NULL,
            level INTEGER NOT NULL, format TEXT NOT NULL);
        CREATE TABLE league_clubs (league_id TEXT NOT NULL, club_id INTEGER NOT NULL, ord INTEGER NOT NULL);
        CREATE TABLE fixtures (id INTEGER PRIMARY KEY, league_id TEXT NOT NULL, matchday INTEGER NOT NULL, date TEXT NOT NULL,
            home_id INTEGER NOT NULL, away_id INTEGER NOT NULL, referee_id INTEGER NOT NULL,
            played INTEGER NOT NULL, home_goals INTEGER, away_goals INTEGER, ht_home INTEGER, ht_away INTEGER, attendance INTEGER,
            weather TEXT, home_xg REAL, away_xg REAL, home_possession INTEGER, home_shots INTEGER, away_shots INTEGER, potm INTEGER);
        CREATE INDEX fixtures_league ON fixtures (league_id, matchday);
        CREATE TABLE goals (fixture_id INTEGER NOT NULL, ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
            scorer_id INTEGER NOT NULL, assist_id INTEGER, penalty INTEGER NOT NULL, own_goal INTEGER NOT NULL);
        CREATE TABLE cards (fixture_id INTEGER NOT NULL, ord INTEGER NOT NULL, minute INTEGER NOT NULL, home INTEGER NOT NULL,
            player_id INTEGER NOT NULL, red INTEGER NOT NULL);
        CREATE TABLE messages (id INTEGER PRIMARY KEY, manager_id INTEGER NOT NULL, date TEXT NOT NULL, type TEXT NOT NULL,
            sender TEXT NOT NULL, subject TEXT NOT NULL, body TEXT NOT NULL, read INTEGER NOT NULL);
        CREATE TABLE history (ord INTEGER NOT NULL, season_year INTEGER NOT NULL, league_id TEXT NOT NULL, league_name TEXT NOT NULL,
            champion_id INTEGER NOT NULL, champion_name TEXT NOT NULL, final_order TEXT NOT NULL);
        """;

    /// <summary>Schema nach globaler Version <paramref name="version"/> (Version 1 plus Migrationen), für Tests der Abschnitte.</summary>
    public static string Schema(int version) =>
        V1Schema() + string.Concat(Enumerable.Range(1, version - 1).Select(v => Migrations[v]));

    /// <summary>Hebt einen alten Spielstand schrittweise bis zur Ausgangsversion an.</summary>
    public static void MigrateToBase(SqliteConnection con, int version)
    {
        while (version < SaveSection.BaseVersion)
        {
            if (!Migrations.TryGetValue(version, out var sql))
                throw new InvalidDataException($"Keine Migration von Spielstand-Version {version}.");
            using var tx = con.BeginTransaction();
            Sql.Exec(con, tx, sql);
            version++;
            Sql.Exec(con, tx, $"UPDATE meta SET value = '{version}' WHERE key = 'schema_version'");
            tx.Commit();
        }
    }
}
