using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Transfers: Transferhistorie, Ratenzahlungen, erfolgsabhängige Zahlungen, laufende Verhandlungen und Sperren.</summary>
public sealed class TransfersSection : SaveSection
{
    public override string ModuleId => "transfers";

    /// <summary>Version 6: Zusatzzahlungen. Version 7: Deal-Id/Vertragshistorie. Version 8: Klauseln, Fremdschlüssel, Vertragsterme.</summary>
    public override int Version => 8;

    /// <summary>
    /// 5 → 6: leere Tabelle für erfolgsabhängige Zahlungen. 6 → 7: Deal-Id und Tabelle <c>contracts</c>.
    /// 7 → 8: Klauseln, Fremdschlüssel, Spalte <c>terms</c> am Vertrag (Must-now E1-S3/E6).
    /// </summary>
    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5) Exec(c, tx, ConditionsTableV6);
        if (fromVersion <= 6)
        {
            Exec(c, tx, "ALTER TABLE transfers ADD COLUMN deal_id INTEGER NOT NULL DEFAULT 0");
            Exec(c, tx, "ALTER TABLE installments ADD COLUMN deal_id INTEGER NOT NULL DEFAULT 0");
            Exec(c, tx, "ALTER TABLE transfer_conditions ADD COLUMN deal_id INTEGER NOT NULL DEFAULT 0");
            Exec(c, tx, ContractsTableV7);
        }
        if (fromVersion <= 7)
        {
            Exec(c, tx, ClausesTable);
            Exec(c, tx, "CREATE TABLE IF NOT EXISTS preemption_offers (clause_id INTEGER NOT NULL, negotiation_id INTEGER NOT NULL, player_id INTEGER NOT NULL, beneficiary INTEGER NOT NULL, fee INTEGER NOT NULL, expires TEXT NOT NULL)");
            try { Exec(c, tx, "ALTER TABLE contracts ADD COLUMN terms TEXT"); } catch (SqliteException) { /* schon vorhanden */ }
            // Fremdschlüssel: Tabellen neu anlegen, Daten übernehmen.
            Exec(c, tx, """
                CREATE TABLE transfers_v8 (ord INTEGER NOT NULL, date TEXT NOT NULL, player_id INTEGER NOT NULL REFERENCES players(id), from_club INTEGER REFERENCES clubs(id), to_club INTEGER REFERENCES clubs(id),
                    fee INTEGER NOT NULL, loan INTEGER NOT NULL, season_year INTEGER NOT NULL, deal_id INTEGER NOT NULL DEFAULT 0);
                INSERT INTO transfers_v8 SELECT ord, date, player_id, from_club, to_club, fee, loan, season_year, deal_id FROM transfers;
                DROP TABLE transfers; ALTER TABLE transfers_v8 RENAME TO transfers;
                CREATE INDEX transfers_player ON transfers (player_id);
                CREATE TABLE installments_v8 (ord INTEGER NOT NULL, from_club INTEGER NOT NULL REFERENCES clubs(id), to_club INTEGER NOT NULL REFERENCES clubs(id), due TEXT NOT NULL,
                    amount INTEGER NOT NULL, text TEXT NOT NULL, text_ref TEXT, deal_id INTEGER NOT NULL DEFAULT 0);
                INSERT INTO installments_v8 SELECT ord, from_club, to_club, due, amount, text, text_ref, deal_id FROM installments;
                DROP TABLE installments; ALTER TABLE installments_v8 RENAME TO installments;
                CREATE TABLE transfer_conditions_v8 (ord INTEGER NOT NULL, player_id INTEGER NOT NULL REFERENCES players(id), payer INTEGER NOT NULL REFERENCES clubs(id), payee INTEGER NOT NULL REFERENCES clubs(id),
                    kind TEXT NOT NULL, amount INTEGER NOT NULL, until TEXT NOT NULL, count INTEGER NOT NULL, paid INTEGER NOT NULL, deal_id INTEGER NOT NULL DEFAULT 0);
                INSERT INTO transfer_conditions_v8 SELECT ord, player_id, payer, payee, kind, amount, until, count, paid, deal_id FROM transfer_conditions;
                DROP TABLE transfer_conditions; ALTER TABLE transfer_conditions_v8 RENAME TO transfer_conditions;
                """);
            // Laufende Verträge aus players.contract in die Historie übernehmen (Must-now E6-S2).
            foreach (var r in Rows(c, """
                SELECT p.id, p.club_id, p.contract, p.contract_wage, p.contract_until
                FROM players p
                WHERE p.contract IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM contracts c2 WHERE c2.player_id = p.id AND c2.current = 1)
                """))
            {
                var playerId = r.GetInt32(0);
                var clubId = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                var terms = r.GetString(2);
                var wage = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                var until = r.IsDBNull(4) ? "2000-01-01" : r.GetString(4);
                Exec(c, tx, "INSERT INTO contracts (player_id, club_id, deal_id, start, end, wage, current, superseded_by, terms) VALUES ("
                    + playerId + ", " + clubId + ", 0, " + SqlQuote(until) + ", " + SqlQuote(until) + ", " + wage + ", 1, NULL, " + SqlQuote(terms) + ")");
            }
            // Bestehende Historienzeilen: Terms nachziehen, wo noch leer.
            Exec(c, tx, """
                UPDATE contracts SET terms = (
                    SELECT p.contract FROM players p WHERE p.id = contracts.player_id)
                WHERE current = 1 AND terms IS NULL
                  AND EXISTS (SELECT 1 FROM players p WHERE p.id = contracts.player_id AND p.contract IS NOT NULL)
                """);
        }
    }

    private static string SqlQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private const string ConditionsTableV6 = """
        CREATE TABLE transfer_conditions (ord INTEGER NOT NULL, player_id INTEGER NOT NULL, payer INTEGER NOT NULL, payee INTEGER NOT NULL,
            kind TEXT NOT NULL, amount INTEGER NOT NULL, until TEXT NOT NULL, count INTEGER NOT NULL, paid INTEGER NOT NULL);
        """;

    private const string ContractsTableV7 = """
        CREATE TABLE contracts (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL REFERENCES players(id), club_id INTEGER NOT NULL REFERENCES clubs(id), deal_id INTEGER NOT NULL,
            start TEXT NOT NULL, end TEXT NOT NULL, wage INTEGER NOT NULL, current INTEGER NOT NULL, superseded_by INTEGER);
        CREATE INDEX contracts_player ON contracts (player_id);
        """;

    private const string ContractsTable = """
        CREATE TABLE contracts (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL REFERENCES players(id), club_id INTEGER NOT NULL REFERENCES clubs(id), deal_id INTEGER NOT NULL,
            start TEXT NOT NULL, end TEXT NOT NULL, wage INTEGER NOT NULL, current INTEGER NOT NULL, superseded_by INTEGER, terms TEXT);
        CREATE INDEX contracts_player ON contracts (player_id);
        """;

    private const string ClausesTable = """
        CREATE TABLE clauses (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL REFERENCES players(id), contract_id INTEGER, deal_id INTEGER,
            beneficiary INTEGER NOT NULL, kind TEXT NOT NULL, amount INTEGER NOT NULL, percent INTEGER NOT NULL, until TEXT, active INTEGER NOT NULL);
        CREATE INDEX clauses_player ON clauses (player_id);
        """;

    protected override string Schema => """
        CREATE TABLE transfers (ord INTEGER NOT NULL, date TEXT NOT NULL, player_id INTEGER NOT NULL REFERENCES players(id), from_club INTEGER REFERENCES clubs(id), to_club INTEGER REFERENCES clubs(id),
            fee INTEGER NOT NULL, loan INTEGER NOT NULL, season_year INTEGER NOT NULL, deal_id INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX transfers_player ON transfers (player_id);
        CREATE TABLE installments (ord INTEGER NOT NULL, from_club INTEGER NOT NULL REFERENCES clubs(id), to_club INTEGER NOT NULL REFERENCES clubs(id), due TEXT NOT NULL,
            amount INTEGER NOT NULL, text TEXT NOT NULL, text_ref TEXT, deal_id INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE negotiations (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL, buyer_id INTEGER NOT NULL, seller_id INTEGER,
            type TEXT NOT NULL, stage TEXT NOT NULL, data TEXT NOT NULL);
        CREATE TABLE negotiation_locks (key TEXT PRIMARY KEY, until TEXT NOT NULL);
        CREATE TABLE transfer_conditions (ord INTEGER NOT NULL, player_id INTEGER NOT NULL REFERENCES players(id), payer INTEGER NOT NULL REFERENCES clubs(id), payee INTEGER NOT NULL REFERENCES clubs(id),
            kind TEXT NOT NULL, amount INTEGER NOT NULL, until TEXT NOT NULL, count INTEGER NOT NULL, paid INTEGER NOT NULL,
            deal_id INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE preemption_offers (clause_id INTEGER NOT NULL, negotiation_id INTEGER NOT NULL, player_id INTEGER NOT NULL, beneficiary INTEGER NOT NULL, fee INTEGER NOT NULL, expires TEXT NOT NULL);
        """ + ContractsTable + ClausesTable;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        // Must-now E6-S2: laufende Verträge immer in der Tabelle führen (auch Welterzeugung ohne Historieeintrag).
        EnsureCurrentContracts(w);

        using (var cmd = Insert(con, tx, "transfers", 9))
            for (var i = 0; i < w.Transfers.Count; i++)
            {
                var t = w.Transfers[i];
                Run(cmd, i, D(t.Date), t.PlayerId, t.FromClubId, t.ToClubId, t.Fee, t.Loan, t.SeasonYear, t.DealId);
            }
        using (var cmd = Insert(con, tx, "installments", 8))
            for (var i = 0; i < w.Installments.Count; i++)
            {
                var t = w.Installments[i];
                Run(cmd, i, t.FromClubId, t.ToClubId, D(t.Due), t.Amount, t.TextRef?.ToString() ?? t.Text, Ref(t.TextRef), t.DealId);
            }
        using (var cmd = Insert(con, tx, "transfer_conditions", 10))
            for (var i = 0; i < w.TransferConditions.Count; i++)
            {
                var c = w.TransferConditions[i];
                Run(cmd, i, c.PlayerId, c.PayerId, c.PayeeId, c.Kind.ToString(), c.Amount, D(c.Until), c.Count, c.Paid, c.DealId);
            }
        using (var cmd = Insert(con, tx, "contracts", 10))
            foreach (var c in w.ContractHistory)
                Run(cmd, c.Id, c.PlayerId, c.ClubId, c.DealId, D(c.Start), D(c.End), c.WeeklyWage, c.Current, c.SupersededBy, c.Terms is null ? null : J(c.Terms));
        using (var cmd = Insert(con, tx, "clauses", 10))
            foreach (var c in w.Clauses)
                Run(cmd, c.Id, c.PlayerId, c.ContractId, c.DealId, c.BeneficiaryClubId, c.Kind.ToString(), c.Amount, c.Percent, c.Until is { } u ? D(u) : null, c.Active);
        using (var cmd = Insert(con, tx, "preemption_offers", 6))
            foreach (var o in w.PreemptionOffers)
                Run(cmd, o.ClauseId, o.NegotiationId, o.PlayerId, o.BeneficiaryClubId, o.Fee, D(o.Expires));
        using (var cmd = Insert(con, tx, "negotiations", 7))
            foreach (var n in w.Negotiations) Run(cmd, n.Id, n.PlayerId, n.BuyerId, n.SellerId, n.Type.ToString(), n.Stage.ToString(), J(n));
        using (var cmd = Insert(con, tx, "negotiation_locks", 2))
            foreach (var (k, until) in w.NegotiationLocks) Run(cmd, k, D(until));
    }

    /// <summary>Stellt sicher, dass jeder laufende Spielervertrag einen aktuellen Historieeintrag mit Terms hat.</summary>
    private static void EnsureCurrentContracts(World w)
    {
        foreach (var p in w.Players.Values)
        {
            if (p.Contract is null) continue;
            var current = w.ContractHistory.LastOrDefault(c => c.PlayerId == p.Id && c.Current);
            if (current is null)
            {
                current = new ContractRecord
                {
                    Id = w.NextContractId++, PlayerId = p.Id, ClubId = p.ClubId ?? 0,
                    Start = p.Contract.Start, End = p.Contract.Until, WeeklyWage = p.Contract.WeeklyWage,
                    Current = true, Terms = p.Contract,
                };
                w.ContractHistory.Add(current);
            }
            else
            {
                current.ClubId = p.ClubId ?? current.ClubId;
                current.Start = p.Contract.Start;
                current.End = p.Contract.Until;
                current.WeeklyWage = p.Contract.WeeklyWage;
                current.Terms = p.Contract;
            }
        }
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT date, player_id, from_club, to_club, fee, loan, season_year, deal_id FROM transfers ORDER BY ord"))
            w.Transfers.Add(new TransferRecord
            {
                Date = PD(r.GetString(0)), PlayerId = r.GetInt32(1), FromClubId = NInt(r, 2), ToClubId = NInt(r, 3),
                Fee = r.GetInt64(4), Loan = r.GetInt32(5) == 1, SeasonYear = r.GetInt32(6), DealId = r.GetInt32(7),
            });
        foreach (var r in Rows(con, "SELECT from_club, to_club, due, amount, text, text_ref, deal_id FROM installments ORDER BY ord"))
            w.Installments.Add(new Installment
            {
                FromClubId = r.GetInt32(0), ToClubId = r.GetInt32(1), Due = PD(r.GetString(2)), Amount = r.GetInt64(3), Text = r.GetString(4), TextRef = PRef(r, 5),
                DealId = r.GetInt32(6),
            });
        foreach (var r in Rows(con, "SELECT player_id, payer, payee, kind, amount, until, count, paid, deal_id FROM transfer_conditions ORDER BY ord"))
            w.TransferConditions.Add(new TransferCondition
            {
                PlayerId = r.GetInt32(0), PayerId = r.GetInt32(1), PayeeId = r.GetInt32(2), Kind = Enum.Parse<TransferConditionKind>(r.GetString(3)),
                Amount = r.GetInt64(4), Until = PD(r.GetString(5)), Count = r.GetInt32(6), Paid = r.GetInt64(7), DealId = r.GetInt32(8),
            });
        try
        {
            foreach (var r in Rows(con, "SELECT id, player_id, club_id, deal_id, start, end, wage, current, superseded_by, terms FROM contracts ORDER BY id"))
            {
                var rec = new ContractRecord
                {
                    Id = r.GetInt32(0), PlayerId = r.GetInt32(1), ClubId = r.GetInt32(2), DealId = r.GetInt32(3),
                    Start = PD(r.GetString(4)), End = PD(r.GetString(5)), WeeklyWage = r.GetInt32(6), Current = r.GetInt32(7) == 1,
                    SupersededBy = NInt(r, 8), Terms = r.FieldCount > 9 && !r.IsDBNull(9) ? PJ<Contract>(r.GetString(9)) : null,
                };
                w.ContractHistory.Add(rec);
            }
        }
        catch (SqliteException)
        {
            foreach (var r in Rows(con, "SELECT id, player_id, club_id, deal_id, start, end, wage, current, superseded_by FROM contracts ORDER BY id"))
                w.ContractHistory.Add(new ContractRecord
                {
                    Id = r.GetInt32(0), PlayerId = r.GetInt32(1), ClubId = r.GetInt32(2), DealId = r.GetInt32(3),
                    Start = PD(r.GetString(4)), End = PD(r.GetString(5)), WeeklyWage = r.GetInt32(6), Current = r.GetInt32(7) == 1,
                    SupersededBy = NInt(r, 8),
                });
        }
        // Must-now E6-S2: laufenden Vertrag aus der Tabelle ableiten (JSON an players entfällt).
        foreach (var rec in w.ContractHistory.Where(c => c.Current))
        {
            if (!w.Players.TryGetValue(rec.PlayerId, out var p)) continue;
            if (rec.Terms is { } terms) p.Contract = terms;
            else if (p.Contract is null)
                p.Contract = new Contract { WeeklyWage = rec.WeeklyWage, Until = rec.End, Start = rec.Start };
        }
        try
        {
            foreach (var r in Rows(con, "SELECT id, player_id, contract_id, deal_id, beneficiary, kind, amount, percent, until, active FROM clauses ORDER BY id"))
                w.Clauses.Add(new Clause
                {
                    Id = r.GetInt32(0), PlayerId = r.GetInt32(1), ContractId = NInt(r, 2), DealId = NInt(r, 3),
                    BeneficiaryClubId = r.GetInt32(4), Kind = Enum.Parse<ClauseKind>(r.GetString(5)), Amount = r.GetInt64(6),
                    Percent = r.GetInt32(7), Until = r.IsDBNull(8) ? null : PD(r.GetString(8)), Active = r.GetInt32(9) == 1,
                });
            foreach (var r in Rows(con, "SELECT clause_id, negotiation_id, player_id, beneficiary, fee, expires FROM preemption_offers"))
                w.PreemptionOffers.Add(new PreemptionOffer
                {
                    ClauseId = r.GetInt32(0), NegotiationId = r.GetInt32(1), PlayerId = r.GetInt32(2),
                    BeneficiaryClubId = r.GetInt32(3), Fee = r.GetInt64(4), Expires = PD(r.GetString(5)),
                });
        }
        catch (SqliteException)
        {
            // Ältere Abschnitte ohne Klauseltabelle: Migration legt sie beim Speichern an.
        }
        foreach (var r in Rows(con, "SELECT data FROM negotiations ORDER BY id"))
            w.Negotiations.Add(PJ<Negotiation>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT key, until FROM negotiation_locks"))
            w.NegotiationLocks[r.GetString(0)] = PD(r.GetString(1));
    }
}
