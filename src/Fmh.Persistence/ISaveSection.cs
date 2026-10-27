using Fmh.Core.Model;
using Microsoft.Data.Sqlite;

namespace Fmh.Core.Persistence;

/// <summary>
/// Speicherabschnitt (Umsetzungskonzept 4.2): besitzt seine Tabellen und führt eine eigene Version
/// (Tabelle <c>meta</c>, Schlüssel <c>schema.&lt;modul&gt;</c>). Die globale Schemaversion 5 ist die Ausgangsversion aller Abschnitte.
/// Abweichend vom Konzept bekommen Anlegen, Migrieren und Speichern die offene Transaktion mit,
/// weil Microsoft.Data.Sqlite Befehle innerhalb einer Transaktion nur mit ihr ausführt.
/// </summary>
public interface ISaveSection
{
    string ModuleId { get; }
    int Version { get; }

    /// <summary>Legt die Tabellen in der aktuellen Version an (neuer Spielstand).</summary>
    void CreateSchema(SqliteConnection c, SqliteTransaction tx);

    /// <summary>Hebt die Tabellen von <paramref name="fromVersion"/> auf <see cref="Version"/> an.</summary>
    void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion);

    void Save(SqliteConnection c, SqliteTransaction tx, World w);
    void Load(SqliteConnection c, World w);
}

/// <summary>Abschnitt ohne eigene Migrationen seit der Ausgangsversion 5.</summary>
public abstract class SaveSection : ISaveSection
{
    public const int BaseVersion = 5;

    public abstract string ModuleId { get; }
    public virtual int Version => BaseVersion;

    /// <summary>Tabellen der aktuellen Version (CREATE TABLE/INDEX).</summary>
    protected abstract string Schema { get; }

    public void CreateSchema(SqliteConnection c, SqliteTransaction tx) => Sql.Exec(c, tx, Schema);

    public virtual void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion != Version)
            throw new InvalidDataException($"Keine Migration für Abschnitt {ModuleId} von Version {fromVersion}.");
    }

    public abstract void Save(SqliteConnection c, SqliteTransaction tx, World w);
    public abstract void Load(SqliteConnection c, World w);
}
