using System.Globalization;
using System.Text.Json;
using Fmh.Core.Data;
using Microsoft.Data.Sqlite;

namespace Fmh.Core.Persistence;

/// <summary>Hilfen für die Speicherabschnitte: Befehle, Zeilen, JSON-Spalten, Datumsformat.</summary>
internal static class Sql
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Exec(SqliteConnection con, SqliteTransaction? tx, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static string J<T>(T value) => JsonSerializer.Serialize(value, Json.Compact);
    public static string? Ref(TextRef? r) => r is null ? null : J(r);
    public static TextRef? PRef(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : PJ<TextRef>(r.GetString(i));
    public static string? NStr(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static T? PJ<T>(string s) => JsonSerializer.Deserialize<T>(s, Json.Compact);
    public static T? Opt<T>(SqliteDataReader r, int i) where T : class => r.IsDBNull(i) ? null : PJ<T>(r.GetString(i));

    public static string D(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    public static DateOnly PD(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", Inv);
    public static int? NInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    /// <summary>Vorbereiteter Befehl mit Parametern <c>$p0</c> … in der Reihenfolge der Werte.</summary>
    public static SqliteCommand Cmd(SqliteConnection con, SqliteTransaction tx, string sql, params string[] parameters)
    {
        var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter { ParameterName = p });
        cmd.Prepare();
        return cmd;
    }

    /// <summary>INSERT mit <paramref name="count"/> Platzhaltern.</summary>
    public static SqliteCommand Insert(SqliteConnection con, SqliteTransaction tx, string table, int count)
    {
        var names = Enumerable.Range(0, count).Select(i => "$p" + i).ToArray();
        return Cmd(con, tx, $"INSERT INTO {table} VALUES ({string.Join(",", names)})", names);
    }

    public static void Run(SqliteCommand cmd, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            cmd.Parameters[i].Value = values[i] switch
            {
                null => DBNull.Value,
                bool b => b ? 1 : 0,
                var v => v,
            };
        }
        cmd.ExecuteNonQuery();
    }

    public static IEnumerable<SqliteDataReader> Rows(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return r;
    }
}
