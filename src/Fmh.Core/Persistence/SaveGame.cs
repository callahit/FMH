using System.Globalization;
using Fmh.Core.Model;

namespace Fmh.Core.Persistence;

public sealed class SaveInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public DateTime Modified { get; init; }
    public string ManagerName { get; init; } = "";
    public string ClubName { get; init; } = "";
    public string ClubKey { get; init; } = "";
    public DateOnly? GameDate { get; init; }
    public bool Valid { get; init; } = true;
}

/// <summary>Spielstände als SQLite-Datenbanken mit Autosave in rotierenden Slots und Ironman-Modus.</summary>
public static class SaveGame
{
    public const string Extension = ".fmh";

    public static string DefaultDirectory()
    {
        var env = Environment.GetEnvironmentVariable("FMH_SAVE_DIR");
        if (!string.IsNullOrEmpty(env)) return env;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
        return System.IO.Path.Combine(root, "FMH", "saves");
    }

    public static void Save(World world, string path) => SqliteStore.Save(world, path);

    public static World Load(string path) => SqliteStore.Load(path);

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
        var result = new List<SaveInfo>();
        foreach (var p in Directory.GetFiles(directory, "*" + Extension))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(p);
            var modified = File.GetLastWriteTime(p);
            try
            {
                var meta = SqliteStore.ReadMetaTable(p);
                result.Add(new SaveInfo
                {
                    Path = p, Name = name, Modified = modified,
                    ManagerName = meta.GetValueOrDefault("manager_name", ""),
                    ClubName = meta.GetValueOrDefault("club_name", ""),
                    ClubKey = meta.GetValueOrDefault("club_key", ""),
                    GameDate = meta.TryGetValue("date", out var d) ? DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                });
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidDataException or FormatException)
            {
                result.Add(new SaveInfo { Path = p, Name = name, Modified = modified, Valid = false });
            }
        }
        return result.OrderByDescending(s => s.Modified).ToList();
    }
}
