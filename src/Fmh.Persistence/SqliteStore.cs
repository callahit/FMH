using System.Text.Json;
using Fmh.Core.Data;
using Fmh.Core.Model;
using Fmh.Core.Persistence.Sections;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence;

/// <summary>
/// Spielstand als SQLite-Datenbank (Kapitel 12). Jede Entität hat eine eigene Tabelle, Spielerattribute
/// stehen als Spalten, damit Editor und Auswertungen direkt per SQL arbeiten können. Verschachtelte
/// Konfigurationen (Ligaformat, Taktik, Profil) liegen als JSON-Spalten daneben.
/// Die Tabellen gehören Speicherabschnitten je Modul (<see cref="ISaveSection"/>), jeder mit eigener Version.
/// </summary>
public static class SqliteStore
{
    /// <summary>Globale Schemaversion: Ausgangsversion aller Abschnitte. Ältere Stände hebt <see cref="LegacySchema"/> an.</summary>
    public const int SchemaVersion = SaveSection.BaseVersion;

    private const string SectionKey = "schema.";

    /// <summary>Speicherabschnitte in Ladereihenfolge (Spiele brauchen Ligen und Pokale, Spieler die Vereine).</summary>
    public static IReadOnlyList<ISaveSection> Sections { get; } = new ISaveSection[]
    {
        new WorldSection(), new PlayersSection(), new CupsSection(), new MatchdaySection(), new TransfersSection(),
        new FinanceSection(), new ScoutingSection(), new StaffSection(), new EventsSection(), new LicenseSection(),
    };

    // ------------------------------------------------------------------ Speichern

    public static void Save(World world, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var con = Open(tmp))
        {
            using var tx = con.BeginTransaction();
            Exec(con, tx, "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
            foreach (var s in Sections) s.CreateSchema(con, tx);
            WriteMeta(con, tx, world);
            foreach (var s in Sections) s.Save(con, tx, world);
            tx.Commit();
        }
        SqliteConnection.ClearAllPools();
        File.Move(tmp, path, overwrite: true);
    }

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var con = new SqliteConnection(cs);
        con.Open();
        return con;
    }

    private static void WriteMeta(SqliteConnection con, SqliteTransaction tx, World w)
    {
        var club = w.Managers.FirstOrDefault(m => m.Human) is { ClubId: { } cid } && w.Clubs.TryGetValue(cid, out var c) ? c : null;
        var meta = new Dictionary<string, string>
        {
            ["schema_version"] = SchemaVersion.ToString(Inv),
            ["save_version"] = w.SaveVersion.ToString(Inv),
            ["seed"] = w.Seed.ToString(Inv),
            ["rng_state"] = JsonSerializer.Serialize(w.RngState),
            ["date"] = D(w.Date),
            ["season_year"] = w.SeasonYear.ToString(Inv),
            ["season_finished"] = w.SeasonFinished ? "1" : "0",
            ["market_levels"] = JsonSerializer.Serialize(w.MarketLevels),
            ["market_bases"] = JsonSerializer.Serialize(w.MarketBases),
            ["autosave_counter"] = w.AutosaveCounter.ToString(Inv),
            ["next_player_id"] = w.NextPlayerId.ToString(Inv),
            ["next_fixture_id"] = w.NextFixtureId.ToString(Inv),
            ["next_message_id"] = w.NextMessageId.ToString(Inv),
            ["next_tie_id"] = w.NextTieId.ToString(Inv),
            ["next_negotiation_id"] = w.NextNegotiationId.ToString(Inv),
            ["next_scout_id"] = w.NextScoutId.ToString(Inv),
            ["next_assignment_id"] = w.NextAssignmentId.ToString(Inv),
            ["next_staff_id"] = w.NextStaffId.ToString(Inv),
            ["next_building_id"] = w.NextBuildingId.ToString(Inv),
            ["next_project_id"] = w.NextProjectId.ToString(Inv),
            ["next_sponsor_id"] = w.NextSponsorId.ToString(Inv),
            ["next_loan_id"] = w.NextLoanId.ToString(Inv),
            ["next_event_id"] = w.NextEventId.ToString(Inv),
            ["next_deal_id"] = w.NextDealId.ToString(Inv),
            ["next_contract_id"] = w.NextContractId.ToString(Inv),
            ["pending_press"] = w.PendingPress is null ? "" : J(w.PendingPress),
            ["rules"] = JsonSerializer.Serialize(w.Rules, Json.Compact),
            ["packages"] = JsonSerializer.Serialize(w.Packages),
            ["saved_at"] = DateTime.Now.ToString("O", Inv),
            ["manager_name"] = w.Managers.FirstOrDefault(m => m.Human)?.Name ?? "",
            ["club_name"] = club?.Name ?? "",
            ["club_key"] = club?.Key ?? "",
        };
        foreach (var s in Sections) meta[SectionKey + s.ModuleId] = s.Version.ToString(Inv);
        using var cmd = Insert(con, tx, "meta", 2);
        foreach (var (k, v) in meta) Run(cmd, k, v);
    }

    // ------------------------------------------------------------------ Laden

    public static World Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Spielstand nicht gefunden.", path);
        using (var con = Open(path))
        {
            Migrate(con);
        }
        using var db = Open(path, readOnly: true);
        var w = ReadMeta(ReadMetaTable(db));
        foreach (var s in Sections) s.Load(db, w);
        SqliteConnection.ClearAllPools();
        return w;
    }

    /// <summary>
    /// Hebt einen Spielstand an: zuerst global bis zur Ausgangsversion 5, danach jeden Abschnitt auf seine Version.
    /// Ein Abschnitt ohne Eintrag in <c>meta</c> steht auf der Ausgangsversion.
    /// </summary>
    public static void Migrate(SqliteConnection con)
    {
        var version = ReadSchemaVersion(con);
        if (version > SchemaVersion)
            throw new InvalidDataException($"Spielstand-Version {version} ist neuer als dieses Spiel ({SchemaVersion}).");
        if (version < SchemaVersion) LegacySchema.MigrateToBase(con, version);

        var meta = ReadMetaTable(con);
        var unknown = meta.Keys.Where(k => k.StartsWith(SectionKey) && Sections.All(s => SectionKey + s.ModuleId != k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException($"Spielstand enthält unbekannte Abschnitte ({string.Join(", ", unknown)}) und ist neuer als dieses Spiel.");
        using var tx = con.BeginTransaction();
        foreach (var s in Sections)
        {
            var key = SectionKey + s.ModuleId;
            var from = meta.TryGetValue(key, out var v) ? int.Parse(v, Inv) : SaveSection.BaseVersion;
            if (from > s.Version)
                throw new InvalidDataException($"Abschnitt {s.ModuleId} hat Version {from} und ist neuer als dieses Spiel ({s.Version}).");
            if (from < s.Version) s.Migrate(con, tx, from);
            if (from != s.Version || v is null)
                Exec(con, tx, $"INSERT OR REPLACE INTO meta (key, value) VALUES ('{key}', '{s.Version.ToString(Inv)}')");
        }
        tx.Commit();
    }

    private static int ReadSchemaVersion(SqliteConnection con)
    {
        try
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
            return cmd.ExecuteScalar() is string s ? int.Parse(s, Inv) : 0;
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("Datei ist kein FMH-Spielstand.", ex);
        }
    }

    public static Dictionary<string, string> ReadMetaTable(string path)
    {
        using var con = Open(path, readOnly: true);
        var meta = ReadMetaTable(con);
        SqliteConnection.ClearAllPools();
        return meta;
    }

    private static Dictionary<string, string> ReadMetaTable(SqliteConnection con)
    {
        var meta = new Dictionary<string, string>();
        foreach (var r in Rows(con, "SELECT key, value FROM meta")) meta[r.GetString(0)] = r.GetString(1);
        return meta;
    }

    private static World ReadMeta(Dictionary<string, string> meta)
    {
        int I(string k) => int.Parse(meta[k], Inv);
        int Next(string k) => meta.ContainsKey(k) ? I(k) : 1;
        return new World
        {
            SaveVersion = I("save_version"),
            Seed = ulong.Parse(meta["seed"], Inv),
            RngState = JsonSerializer.Deserialize<ulong[]>(meta["rng_state"]) ?? Array.Empty<ulong>(),
            Date = PD(meta["date"]),
            SeasonYear = I("season_year"),
            SeasonFinished = meta["season_finished"] == "1",
            MarketLevels = meta.TryGetValue("market_levels", out var ml) ? JsonSerializer.Deserialize<Dictionary<string, double>>(ml) ?? new() : new(),
            MarketBases = meta.TryGetValue("market_bases", out var mb) ? JsonSerializer.Deserialize<Dictionary<string, double>>(mb) ?? new() : new(),
            AutosaveCounter = I("autosave_counter"),
            NextPlayerId = I("next_player_id"),
            NextFixtureId = I("next_fixture_id"),
            NextMessageId = I("next_message_id"),
            NextTieId = Next("next_tie_id"),
            NextNegotiationId = Next("next_negotiation_id"),
            NextScoutId = Next("next_scout_id"),
            NextAssignmentId = Next("next_assignment_id"),
            NextStaffId = Next("next_staff_id"),
            NextBuildingId = Next("next_building_id"),
            NextProjectId = Next("next_project_id"),
            NextSponsorId = Next("next_sponsor_id"),
            NextLoanId = Next("next_loan_id"),
            NextEventId = Next("next_event_id"),
            NextDealId = Next("next_deal_id"),
            NextContractId = Next("next_contract_id"),
            PendingPress = meta.TryGetValue("pending_press", out var press) && press.Length > 0 ? PJ<PressConference>(press) : null,
            Rules = JsonSerializer.Deserialize<WorldRules>(meta["rules"], Json.Compact) ?? new WorldRules(),
            Packages = JsonSerializer.Deserialize<List<string>>(meta["packages"]) ?? new List<string>(),
        };
    }
}
