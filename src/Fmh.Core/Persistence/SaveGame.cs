using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Persistence;

public sealed class SaveInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public DateTime Modified { get; init; }
}

/// <summary>
/// Spielstände als komprimiertes JSON mit Versionsnummer. Ältere Versionen werden beim Laden
/// schrittweise migriert (Kapitel 12, Stabilität).
/// </summary>
public static class SaveGame
{
    public const string Extension = ".fmh";

    /// <summary>Migrationsschritte: Schlüssel = Ausgangsversion, Wert hebt das JSON um eine Version an.</summary>
    private static readonly Dictionary<int, Action<JsonObject>> Migrations = new();

    public static string DefaultDirectory()
    {
        var env = Environment.GetEnvironmentVariable("FMH_SAVE_DIR");
        if (!string.IsNullOrEmpty(env)) return env;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
        return System.IO.Path.Combine(root, "FMH", "saves");
    }

    public static void Save(World world, string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var file = File.Create(tmp))
        using (var gz = new GZipStream(file, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gz, world, Json.Compact);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static World Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        var node = JsonNode.Parse(gz) as JsonObject ?? throw new InvalidDataException("Spielstand ist beschädigt.");
        Migrate(node);
        return node.Deserialize<World>(Json.Compact) ?? throw new InvalidDataException("Spielstand ist leer.");
    }

    public static void Migrate(JsonObject root)
    {
        var version = root["saveVersion"]?.GetValue<int>() ?? 0;
        if (version > World.CurrentSaveVersion)
            throw new InvalidDataException($"Spielstand-Version {version} ist neuer als dieses Spiel ({World.CurrentSaveVersion}).");
        while (version < World.CurrentSaveVersion)
        {
            if (!Migrations.TryGetValue(version, out var step))
                throw new InvalidDataException($"Keine Migration von Spielstand-Version {version}.");
            step(root);
            version++;
            root["saveVersion"] = version;
        }
    }

    /// <summary>Autosave in rotierenden Slots (Standard 3).</summary>
    public static string Autosave(World world, string directory)
    {
        if (world.Rules.Ironman) return IronmanSave(world, directory);
        var slots = Math.Max(1, world.Rules.AutosaveSlots);
        var slot = world.AutosaveCounter % slots + 1;
        world.AutosaveCounter++;
        var path = System.IO.Path.Combine(directory, $"autosave{slot}{Extension}");
        Save(world, path);
        return path;
    }

    /// <summary>Im Ironman-Modus gibt es genau einen Spielstand.</summary>
    public static string IronmanSave(World world, string directory)
    {
        var path = System.IO.Path.Combine(directory, $"ironman-{world.Seed}{Extension}");
        Save(world, path);
        return path;
    }

    public static IReadOnlyList<SaveInfo> List(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<SaveInfo>();
        return Directory.GetFiles(directory, "*" + Extension)
            .Select(p => new SaveInfo { Path = p, Name = System.IO.Path.GetFileNameWithoutExtension(p), Modified = File.GetLastWriteTime(p) })
            .OrderByDescending(s => s.Modified)
            .ToList();
    }
}
