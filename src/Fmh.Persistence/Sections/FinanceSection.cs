using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Finanzen: Buchungen aller Vereine (Kontostand und Kennzahlen stehen beim Verein).</summary>
public sealed class FinanceSection : SaveSection
{
    public override string ModuleId => "finance";

    /// <summary>Version 6: Fremdschlüssel auf clubs (Must-now E6-S1).</summary>
    public override int Version => 6;

    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5)
        {
            Exec(c, tx, """
                CREATE TABLE transactions_v6 (ord INTEGER NOT NULL, date TEXT NOT NULL, club_id INTEGER NOT NULL REFERENCES clubs(id), type TEXT NOT NULL,
                    amount INTEGER NOT NULL, text TEXT NOT NULL, text_ref TEXT);
                INSERT INTO transactions_v6 SELECT * FROM transactions;
                DROP TABLE transactions; ALTER TABLE transactions_v6 RENAME TO transactions;
                CREATE INDEX transactions_club ON transactions (club_id);
                """);
        }
    }

    protected override string Schema => """
        CREATE TABLE transactions (ord INTEGER NOT NULL, date TEXT NOT NULL, club_id INTEGER NOT NULL REFERENCES clubs(id), type TEXT NOT NULL,
            amount INTEGER NOT NULL, text TEXT NOT NULL, text_ref TEXT);
        CREATE INDEX transactions_club ON transactions (club_id);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using var cmd = Insert(con, tx, "transactions", 7);
        for (var i = 0; i < w.Transactions.Count; i++)
        {
            var t = w.Transactions[i];
            Run(cmd, i, D(t.Date), t.ClubId, t.Type.ToString(), t.Amount, t.TextRef?.ToString() ?? t.Text, Ref(t.TextRef));
        }
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT date, club_id, type, amount, text, text_ref FROM transactions ORDER BY ord"))
            w.Transactions.Add(new Transaction
            {
                Date = PD(r.GetString(0)), ClubId = r.GetInt32(1), Type = Enum.Parse<TransactionType>(r.GetString(2)),
                Amount = r.GetInt64(3), Text = r.GetString(4), TextRef = PRef(r, 5),
            });
    }
}
