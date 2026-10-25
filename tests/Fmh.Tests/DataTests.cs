using System.Text.RegularExpressions;
using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Tests;

public class DataTests
{
    [Fact]
    public void BasePackageIsValid()
    {
        Assert.Empty(DataValidator.Validate(TestData.Data));
        Assert.Contains(TestData.Data.Packages, p => p.Id == "base");
        Assert.Equal(36, TestData.Data.Clubs.Count);
        Assert.True(TestData.Data.Formations.Count >= 15);
    }

    [Fact]
    public void ValidatorReportsWrongTeamCount()
    {
        var d = TestData.Data;
        var broken = new GameData
        {
            Countries = d.Countries,
            Clubs = d.Clubs.Skip(1).ToList(),
            Names = d.Names,
            Formations = d.Formations,
            PositionProfiles = d.PositionProfiles,
            Injuries = d.Injuries,
            Engine = d.Engine,
        };
        Assert.Contains(DataValidator.Validate(broken), e => e.Contains("17 statt 18"));
    }

    [Fact]
    public void LaterPackageOverridesFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "fmh-pkg-" + Guid.NewGuid());
        try
        {
            CopyDir(PackageLoader.FindDataRoot(), root);
            var mod = Path.Combine(root, "zz-mod");
            Directory.CreateDirectory(Path.Combine(mod, "lang"));
            File.WriteAllText(Path.Combine(mod, "package.json"), """{ "id": "zz-mod", "version": "1.0.0", "dependsOn": [ "base" ] }""");
            File.WriteAllText(Path.Combine(mod, "lang", "de.json"), """{ "menu.squad": "Mannschaft" }""");
            var data = PackageLoader.Load(root);
            Assert.Equal("Mannschaft", data.Texts["menu.squad"]);
            Assert.Equal("Taktik", data.Texts["menu.tactics"]);
            Assert.Equal(new[] { "base", "zz-mod" }, data.Packages.Select(p => p.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EngineJsonMatchesCalibratedDefaults()
    {
        var file = TestData.Data.Engine;
        var defaults = new EngineConfig();
        foreach (var prop in typeof(EngineConfig).GetProperties())
        {
            var a = System.Text.Json.JsonSerializer.Serialize(prop.GetValue(file));
            var b = System.Text.Json.JsonSerializer.Serialize(prop.GetValue(defaults));
            Assert.True(a == b, $"rules/engine.json weicht bei '{prop.Name}' von den Standardwerten ab: {a} vs. {b}");
        }
    }

    /// <summary>Alle im Code verwendeten Textschlüssel müssen in der Sprachdatei stehen (Kapitel 12, Lokalisierung).</summary>
    [Fact]
    public void AllTextKeysExist()
    {
        var texts = TestData.Data.Texts;
        var missing = new List<string>();
        var src = Path.Combine(TestData.RepoRoot(), "src");
        var keyPattern = new Regex("\"((?:app|menu|ui|unit|help|tip|filter|radar|toneHint|shoutHint|gameMenu|load|newGame|background|expectation|status|inbox|squad|col|player|foot|suit|suitShort|group|attr|pos|morale|injury|tactics|tactic|mentality|advice|lineup|sub|fixtures|league|club|placeholder|options|match|stat|pause|comm|msg|tone|shout|speed|weather)\\.[A-Za-z0-9.]+)\"");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match m in keyPattern.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (key.EndsWith('.')) continue;
                if (!texts.Has(key)) missing.Add($"{key} ({Path.GetFileName(file)})");
            }
        }

        var xamlKey = new Regex(@"\{s:L ([A-Za-z0-9.]+)\}");
        foreach (var file in Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories))
            foreach (Match m in xamlKey.Matches(File.ReadAllText(file)))
                if (!texts.Has(m.Groups[1].Value)) missing.Add($"{m.Groups[1].Value} ({Path.GetFileName(file)})");

        void Enum<T>(string prefix) where T : struct, System.Enum
        {
            foreach (var v in System.Enum.GetValues<T>())
                if (!texts.Has(prefix + v)) missing.Add(prefix + v);
        }
        Enum<Attr>("attr.");
        Enum<Position>("pos.");
        Enum<Morale>("morale.");
        Enum<Suitability>("suit.");
        Enum<Suitability>("suitShort.");
        Enum<PreferredFoot>("foot.");
        Enum<Mentality>("mentality.");
        Enum<PassingLength>("passing.");
        Enum<Tempo>("tempo.");
        Enum<Width>("width.");
        Enum<AttackFocus>("focus.");
        Enum<PressingHeight>("pressing.");
        Enum<DefensiveLine>("line.");
        Enum<TacklingStyle>("tackling.");
        Enum<Distribution>("distribution.");
        Enum<WageDisplay>("wage.");
        Enum<AttributeDisplay>("attrDisplay.");
        Enum<MatchDisplayMode>("matchDisplay.");
        Enum<Weather>("weather.");
        Enum<Tone>("tone.");
        Enum<Shout>("shout.");
        Enum<ConsoleColor>("color.");
        foreach (var i in TestData.Data.Injuries) if (!texts.Has("injury." + i.Key)) missing.Add("injury." + i.Key);
        for (var i = 1; i <= 5; i++) if (!texts.Has("speed." + i)) missing.Add("speed." + i);
        foreach (var f in "SUN") if (!texts.Has("form." + f)) missing.Add("form." + f);
        Assert.True(missing.Count == 0, "Fehlende Texte: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void CommentaryCoversAllEventsAndShotOutcomes()
    {
        var c = TestData.Data.Commentary;
        var keys = new List<string>
        {
            "kickoff", "secondHalf", "halfTime", "fullTime", "stoppage", "attack", "counter", "dribble", "cross", "aerialWon",
            "goal", "goal.header", "goal.longShot", "goal.freeKick", "goal.penalty", "goal.oneOnOne",
            "foul", "foul.dangerous", "yellow", "secondYellow", "red", "freeKick", "freeKick.direct", "penalty", "corner",
            "offside", "goalKick", "injury", "substitution",
        };
        foreach (var kind in new[] { "normal", "header", "longShot", "freeKick", "penalty", "oneOnOne" })
            foreach (var outcome in new[] { "wide", "saved", "blocked", "post" })
                keys.Add($"shot.{kind}.{outcome}");
        Assert.All(keys, k => Assert.True(c.ContainsKey(k) && c[k].Count > 0, $"Kommentar fehlt: {k}"));
    }

    [Fact]
    public void ClubColorsAreValidConsoleColors()
    {
        foreach (var club in TestData.Data.Clubs)
        {
            Assert.True(System.Enum.TryParse<ConsoleColor>(club.PrimaryColor, out _), club.Name);
            Assert.True(System.Enum.TryParse<ConsoleColor>(club.SecondaryColor, out _), club.Name);
        }
    }

    private static void CopyDir(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to));
    }
}
