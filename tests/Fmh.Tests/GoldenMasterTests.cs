using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Model;
using Xunit.Abstractions;

namespace Fmh.Tests;

/// <summary>
/// Golden Master (Umsetzungskonzept 4.5): Welt mit festem Seed über drei Saisons und 10.000 Engine-Spiele.
/// Der Hash muss bei jedem Umbau identisch bleiben; neu gesetzt wird er nur in PRs, die eine Regeländerung ankündigen
/// (Umgebungsvariable FMH_GOLDEN_UPDATE=1). Gleitkommarechnungen können sich zwischen Betriebssystemen unterscheiden,
/// deshalb gibt es je Laufzeitumgebung (z. B. linux-x64, win-x64) einen eigenen Eintrag.
/// </summary>
public class GoldenMasterTests
{
    private const ulong WorldSeed = 4242;
    private const int Seasons = 3;
    private const int EngineMatches = 10_000;

    private readonly ITestOutputHelper _out;

    public GoldenMasterTests(ITestOutputHelper output) => _out = output;

    private static string GoldenFile => Path.Combine(TestData.RepoRoot(), "tests", "Fmh.Tests", "GoldenMaster", "golden.json");

    [Fact]
    public void WorldOverThreeSeasonsIsUnchanged()
    {
        var s = TestData.NewSession(WorldSeed);
        var target = s.World.SeasonYear + Seasons;
        TestData.PlayUntil(s, _ => s.World.SeasonYear >= target, 50_000);
        s.SyncRng();
        Check("world", WorldFingerprint.Sections(s.World));
    }

    [Fact]
    public void EngineOverTenThousandMatchesIsUnchanged()
    {
        var s = TestData.NewSession(1);
        var world = s.World;
        var rng = new Rng(1 ^ 0xC0FFEE);
        var sections = new Dictionary<string, StringBuilder> { ["events"] = new(), ["players"] = new(), ["results"] = new() };
        for (var i = 0; i < EngineMatches; i++)
        {
            var league = world.Leagues[i % world.Leagues.Count];
            var home = league.ClubIds[rng.Next(league.ClubIds.Count)];
            int away;
            do away = league.ClubIds[rng.Next(league.ClubIds.Count)]; while (away == home);
            var fixture = new Fixture { Id = 1_000_000 + i, CompetitionId = league.Id, HomeId = home, AwayId = away, Date = world.Date, RefereeId = world.Referees[rng.Next(world.Referees.Count)].Id };
            var sim = s.StartMatch(fixture, humanControlled: false);
            sim.RunToEnd();
            foreach (var e in sim.Events) sections["events"].Append(JsonSerializer.Serialize(e, Json.Compact)).Append('\n');
            foreach (var team in new[] { sim.Home, sim.Away })
                foreach (var p in team.Participants)
                    sections["players"].Append($"{i}:{p.Player.Id}:{p.Grade?.ToString("R")}:{p.Condition:R}:{p.Goals}:{p.Assists}:{p.Yellow}:{p.Red}:{p.Injured}\n");
            sections["results"].Append($"{i}:{sim.Home.Goals}:{sim.Away.Goals}:{sim.PenaltiesHome}:{sim.PenaltiesAway}:{sim.PlayerOfTheMatchId}\n");
        }
        Check("engine", sections.ToDictionary(kv => kv.Key, kv => Hash(kv.Value.ToString())));
    }

    private void Check(string name, Dictionary<string, string> actual)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var file = File.Exists(GoldenFile) ? JsonNode.Parse(File.ReadAllText(GoldenFile))!.AsObject() : new JsonObject();
        var key = $"{name}.{rid}";
        _out.WriteLine($"{key}: {JsonSerializer.Serialize(actual)}");
        if (Environment.GetEnvironmentVariable("FMH_GOLDEN_UPDATE") == "1")
        {
            file[key] = JsonSerializer.SerializeToNode(new SortedDictionary<string, string>(actual));
            var sorted = new JsonObject();
            foreach (var kv in file.OrderBy(kv => kv.Key)) sorted[kv.Key] = kv.Value?.DeepClone();
            File.WriteAllText(GoldenFile, sorted.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }
        // Für eine neue Laufzeitumgebung gibt es noch keinen eigenen Referenzwert: mit einer anderen vergleichen.
        // Weicht sie ab, nennt die Meldung die Hashes, damit sie als eigener Eintrag nachgetragen werden können.
        var reference = file[key] is JsonObject own ? (key, own)
            : file.Where(kv => kv.Key.StartsWith(name + ".") && kv.Value is JsonObject).Select(kv => (kv.Key, (JsonObject)kv.Value!)).FirstOrDefault();
        if (reference.Item2 is not { } expected)
        {
            _out.WriteLine($"Kein Golden Master für {key} hinterlegt.");
            return;
        }
        var diff = actual.Where(kv => expected[kv.Key]?.GetValue<string>() != kv.Value).Select(kv => kv.Key)
            .Concat(expected.Select(kv => kv.Key).Where(k => !actual.ContainsKey(k))).ToList();
        Assert.True(diff.Count == 0,
            $"Golden Master {key} (Referenz {reference.Item1}) weicht ab in: {string.Join(", ", diff)}; Ist: {JsonSerializer.Serialize(actual)}");
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
}

/// <summary>
/// Fachlicher Fingerabdruck des Spielzustands, nach Bereichen getrennt (damit eine Abweichung sofort zeigt, wo).
/// Ändert ein Umbau nur die Ablage (z. B. Verein und Mannschaft getrennt), wird hier die alte Form wiederhergestellt.
/// </summary>
internal static class WorldFingerprint
{
    public static Dictionary<string, string> Sections(World w)
    {
        string J(object? o) => JsonSerializer.Serialize(o, Json.Compact);
        var sections = new Dictionary<string, string>
        {
            ["calendar"] = J(new { w.Date, w.SeasonYear, w.SeasonFinished, w.RngState }),
            ["ids"] = J(new { w.NextPlayerId, w.NextFixtureId, w.NextMessageId, w.NextTieId, w.NextNegotiationId, w.NextScoutId, w.NextAssignmentId,
                w.NextStaffId, w.NextBuildingId, w.NextProjectId, w.NextSponsorId, w.NextLoanId, w.NextEventId }),
            ["managers"] = J(w.Managers),
            ["clubs"] = Legacy(w.Clubs.Values.OrderBy(c => c.Id), ClubFieldsSinceAp3, "format", null),
            ["players"] = J(w.Players.Values.OrderBy(p => p.Id)),
            ["leagues"] = Legacy(w.Leagues, null, "format", FormatFieldsSinceAp3),
            ["cups"] = Legacy(w.Cups, null, "config", CupFieldsSinceAp4),
            ["messages"] = J(w.Messages),
            ["transfers"] = J(new { w.Transfers, w.Transactions, w.Installments, w.Negotiations, w.NegotiationLocks }),
            ["scouting"] = J(new { w.Scouts, w.ScoutAssignments, w.ScoutReports, w.Knowledge }),
            ["environment"] = J(new { w.Staff, w.Events, w.EventHistory, w.PendingPress, w.EuropeanEntries }),
            ["history"] = J(w.History),
        };
        return sections.ToDictionary(kv => kv.Key, kv => GoldenMasterTests.Hash(kv.Value));
    }

    /// <summary>Felder, die AP 3 ergänzt hat (zweite Mannschaften, Staffeln, Bewegungsregeln); für den Vergleich wird die alte Form hergestellt.</summary>
    private static readonly string[] ClubFieldsSinceAp3 = { "reserveOf", "region", "latitude", "longitude" };
    private static readonly string[] FormatFieldsSinceAp3 = { "groups", "assignment", "movements", "phases" };

    /// <summary>Felder, die AP 4 an Pokalformaten ergänzt hat (eigene Spielzahl im Finale).</summary>
    private static readonly string[] CupFieldsSinceAp4 = { "finalLegs" };

    private static string Legacy<T>(IEnumerable<T> items, string[]? remove, string nested, string[]? removeFromNested)
    {
        var array = JsonSerializer.SerializeToNode(items, Json.Compact)!.AsArray();
        foreach (var item in array.OfType<JsonObject>())
        {
            foreach (var key in remove ?? Array.Empty<string>()) item.Remove(key);
            if (item[nested] is JsonObject inner)
                foreach (var key in removeFromNested ?? Array.Empty<string>()) inner.Remove(key);
        }
        return array.ToJsonString(Json.Compact);
    }
}
