using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.MatchEngine;

namespace Fmh.Tests;

internal static class TestData
{
    /// <summary>Grundwelt (Deutschland); die meisten Tests brauchen keine weiteren Länder und bleiben so schnell.</summary>
    private static readonly Lazy<GameData> Lazy = new(() => PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base" }));

    /// <summary>Alle ausgelieferten Pakete (Deutschland, England, …).</summary>
    private static readonly Lazy<GameData> AllLazy = new(() => PackageLoader.Load(PackageLoader.FindDataRoot()));

    public static GameData Data => Lazy.Value;

    public static GameData AllData => AllLazy.Value;

    /// <summary>Eigene Kopie aller Pakete für Tests, die Länderdaten verändern oder unverändert brauchen (die geteilten
    /// Daten nutzen parallel laufende Tests; Welt und Daten teilen sich die Länderkonfiguration).</summary>
    public static GameData LoadAll() => PackageLoader.Load(PackageLoader.FindDataRoot());

    public static GameSession NewSession(ulong seed = 42, string club = "nordheide")
    {
        var world = WorldGenerator.Create(Data, new NewGameOptions { Seed = seed, ClubKey = club, ManagerName = "Test" });
        return new GameSession(world, Data, new TextMatchEngine());
    }

    /// <summary>Spielt bis zum Ende der aktuellen Saison; eigene Spiele werden sofort berechnet.</summary>
    public static void PlayUntil(GameSession s, Func<AdvanceResult, bool> stop, int maxSteps = 5000)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            var r = s.Advance();
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
            if (stop(r)) return;
        }
        throw new InvalidOperationException("Abbruchbedingung nicht erreicht.");
    }

    /// <summary>Quelltextverzeichnis des Repositories (für Prüfungen der Sprachschlüssel).</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FMH.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FMH.sln nicht gefunden.");
    }
}
