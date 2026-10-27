using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Modul Transfers: Transferhistorie, Ratenzahlungen, laufende Verhandlungen und Sperren.</summary>
public sealed class TransfersSection : SaveSection
{
    public override string ModuleId => "transfers";

    protected override string Schema => """
        CREATE TABLE transfers (ord INTEGER NOT NULL, date TEXT NOT NULL, player_id INTEGER NOT NULL, from_club INTEGER, to_club INTEGER,
            fee INTEGER NOT NULL, loan INTEGER NOT NULL, season_year INTEGER NOT NULL);
        CREATE INDEX transfers_player ON transfers (player_id);
        CREATE TABLE installments (ord INTEGER NOT NULL, from_club INTEGER NOT NULL, to_club INTEGER NOT NULL, due TEXT NOT NULL,
            amount INTEGER NOT NULL, text TEXT NOT NULL, text_ref TEXT);
        CREATE TABLE negotiations (id INTEGER PRIMARY KEY, player_id INTEGER NOT NULL, buyer_id INTEGER NOT NULL, seller_id INTEGER,
            type TEXT NOT NULL, stage TEXT NOT NULL, data TEXT NOT NULL);
        CREATE TABLE negotiation_locks (key TEXT PRIMARY KEY, until TEXT NOT NULL);
        """;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "transfers", 8))
            for (var i = 0; i < w.Transfers.Count; i++)
            {
                var t = w.Transfers[i];
                Run(cmd, i, D(t.Date), t.PlayerId, t.FromClubId, t.ToClubId, t.Fee, t.Loan, t.SeasonYear);
            }
        using (var cmd = Insert(con, tx, "installments", 7))
            for (var i = 0; i < w.Installments.Count; i++)
            {
                var t = w.Installments[i];
                Run(cmd, i, t.FromClubId, t.ToClubId, D(t.Due), t.Amount, t.TextRef?.ToString() ?? t.Text, Ref(t.TextRef));
            }
        using (var cmd = Insert(con, tx, "negotiations", 7))
            foreach (var n in w.Negotiations) Run(cmd, n.Id, n.PlayerId, n.BuyerId, n.SellerId, n.Type.ToString(), n.Stage.ToString(), J(n));
        using (var cmd = Insert(con, tx, "negotiation_locks", 2))
            foreach (var (k, until) in w.NegotiationLocks) Run(cmd, k, D(until));
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT date, player_id, from_club, to_club, fee, loan, season_year FROM transfers ORDER BY ord"))
            w.Transfers.Add(new TransferRecord
            {
                Date = PD(r.GetString(0)), PlayerId = r.GetInt32(1), FromClubId = NInt(r, 2), ToClubId = NInt(r, 3),
                Fee = r.GetInt64(4), Loan = r.GetInt32(5) == 1, SeasonYear = r.GetInt32(6),
            });
        foreach (var r in Rows(con, "SELECT from_club, to_club, due, amount, text, text_ref FROM installments ORDER BY ord"))
            w.Installments.Add(new Installment
            {
                FromClubId = r.GetInt32(0), ToClubId = r.GetInt32(1), Due = PD(r.GetString(2)), Amount = r.GetInt64(3), Text = r.GetString(4), TextRef = PRef(r, 5),
            });
        foreach (var r in Rows(con, "SELECT data FROM negotiations ORDER BY id"))
            w.Negotiations.Add(PJ<Negotiation>(r.GetString(0))!);
        foreach (var r in Rows(con, "SELECT key, until FROM negotiation_locks"))
            w.NegotiationLocks[r.GetString(0)] = PD(r.GetString(1));
    }
}
