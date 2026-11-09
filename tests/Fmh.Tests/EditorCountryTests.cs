using System.Text.Json.Nodes;
using Fmh.Core.Data;
using Fmh.Core.Data.Editing;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 6b: Länder im Editor – Formatversionen und die Abnahme „Regionalliga auf 4 Staffeln“.</summary>
public sealed class EditorCountryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-editor-country-" + Guid.NewGuid().ToString("N"));

    public EditorCountryTests()
    {
        var source = Path.Combine(PackageLoader.FindDataRoot(), "base");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, "base", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static JsonObject Obj(JsonNode? n) => (JsonObject)n!;

    private static JsonPath Division(JsonObject country, string id)
    {
        var levels = (JsonArray)country["levels"]!;
        for (var l = 0; l < levels.Count; l++)
        {
            var divs = (JsonArray)levels[l]!["divisions"]!;
            for (var d = 0; d < divs.Count; d++)
                if (divs[d]!["id"]!.ToString() == id) return JsonPath.Root.Then("levels").Then(l).Then("divisions").Then(d);
        }
        throw new InvalidOperationException(id);
    }

    private static JsonObject Rule(string positions, string to, int? onlySeason = null, bool withoutReplacement = false)
    {
        var r = new JsonObject { ["positions"] = positions, ["to"] = to };
        if (onlySeason is { } s) r["onlySeason"] = s;
        if (withoutReplacement) r["withoutReplacement"] = true;
        return r;
    }

    [Fact]
    public void NewFormatVersionCopiesCountryAndClosesThePreviousOne()
    {
        var ws = PackageWorkspace.Create(_root, "mod", "Mod", new[] { "base" });
        var file = CountryTools.NewFormatVersion(ws, "countries/de.json", 2030);
        Assert.Equal("countries/de-2030.json", file);
        Assert.Equal(2029, (int)Obj(ws.Document("countries/de.json"))["validTo"]!);
        Assert.Equal(2030, (int)Obj(ws.Document(file))["validFrom"]!);
        Assert.Null(Obj(ws.Document(file))["validTo"]);
        Assert.Empty(ws.Validate()); // unverändert kopiert: Übergang passt
        Assert.Throws<ArgumentException>(() => CountryTools.NewFormatVersion(ws, "countries/de.json", 2026));
        ws.Undo();
        Assert.Null(ws.Document(file));
        Assert.Null(Obj(ws.Document("countries/de.json"))["validTo"]);

        var pyramid = CountryTools.Pyramid(Obj(ws.Document("countries/de.json")));
        Assert.Equal(new[] { 18, 18, 20, 90 }, pyramid.Select(l => l.Teams));
        Assert.Equal("5 × 18", pyramid[3].Divisions[0].Shape);
    }

    /// <summary>Abnahme 7.4: Regionalliga ab einer gewählten Saison mit 4 statt 5 Staffeln, allein über Bearbeitungsschritte des Editors.</summary>
    [Fact]
    public void RegionalligaWithFourGroupsAsNewFormatVersionPlaysThrough()
    {
        var ws = PackageWorkspace.Create(_root, "reform", "Regionalliga-Reform", new[] { "base" });
        const string old = "countries/de.json";
        var next = CountryTools.NewFormatVersion(ws, old, 2027);

        // Alte Version, letzte Saison 2026: 18 Vereine mehr steigen ohne Nachrücker in die Oberliga ab
        var de4 = Division(Obj(ws.Document(old)), "DE4");
        var groups = (JsonArray)de4.Then("groups").Resolve(ws.Document(old))!;
        var edits = new List<JsonEdit>();
        for (var g = 0; g < groups.Count; g++)
            if (groups[g]!["movements"] is JsonArray own && own.Count > 0)
                edits.Add(new InsertItem(old, de4.Then("groups").Then(g).Then("movements"), own.Count, Rule("13-16", "Down", 2026, true)));
        var divMoves = (JsonArray)de4.Then("movements").Resolve(ws.Document(old))!;
        edits.Add(new InsertItem(old, de4.Then("movements"), divMoves.Count, Rule("14-16", "Down", 2026, true)));
        ws.Apply(new EditBatch("Übergang", edits));

        // Neue Version ab 2027: vier Staffeln, jeder Staffelsieger steigt auf, keine Aufstiegsrunden mehr
        var nde4 = Division(Obj(ws.Document(next)), "DE4");
        var ngroups = (JsonArray)nde4.Then("groups").Resolve(ws.Document(next))!;
        var south = ngroups.Select((g, i) => (g, i)).Single(t => t.g!["id"]!.ToString() == "DE4S").i;
        ws.Apply(new RemoveItem(next, nde4.Then("groups"), south));
        for (var g = 0; g < 4; g++)
            ws.Apply(new SetValue(next, nde4.Then("groups").Then(g).Then("movements"), new JsonArray()));
        ws.Apply(new SetValue(next, nde4.Then("movements"), new JsonArray(Rule("1", "Up"), Rule("17-18", "Down"))));
        var playoffs = (JsonArray)Obj(ws.Document(next))["playoffs"]!;
        for (var i = playoffs.Count - 1; i >= 0; i--)
            if (playoffs[i]!["lowerDivision"]!.ToString() == "DE4")
                ws.Apply(new RemoveItem(next, JsonPath.Root.Then("playoffs"), i));

        Assert.Empty(ws.Validate());
        Assert.Equal(new[] { 18, 18, 20, 72 }, CountryTools.Pyramid(Obj(ws.Document(next))).Select(l => l.Teams));

        // Speichern, wie das Spiel laden und die Saison 2026 durchspielen
        ws.Save();
        var data = PackageLoader.Load(_root);
        Assert.Empty(DataValidator.Validate(data));
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 7, ClubKey = data.Clubs[0].Key, ManagerName = "Test" });
        var s = new GameSession(world, data, new TextMatchEngine());
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        Assert.Equal(2027, world.Country("DE").ValidFrom);
        var regional = world.Leagues.Where(l => l.CountryCode == "DE" && l.Level == 4).ToList();
        Assert.Equal(4, regional.Count);
        Assert.All(regional, l => Assert.Equal(18, l.ClubIds.Count));
        Assert.Equal(40 + 18, world.Clubs.Values.Count(c => c.LeagueId == "DE-AMA"));
    }

    [Fact]
    public void ReformWithoutTransitionRuleOrWithMisplacedRuleIsReported()
    {
        var ws = PackageWorkspace.Create(_root, "falsch", "Falsch", new[] { "base" });
        var next = CountryTools.NewFormatVersion(ws, "countries/de.json", 2027);
        var nde4 = Division(Obj(ws.Document(next)), "DE4");
        ws.Apply(new RemoveItem(next, nde4.Then("groups"), 4));
        Assert.Contains(ws.Validate(), e => e.Contains("Ebene 4 hat 72 Teams"));

        // Abstieg ohne Nachrücker ist nur aus der untersten Ebene erlaubt
        var de3 = Division(Obj(ws.Document("countries/de.json")), "DE3");
        ws.Apply(new SetValue("countries/de.json", de3.Then("movements"), new JsonArray(Rule("20", "Down", 2026, true))));
        Assert.Contains(ws.Validate(), e => e.Contains("ohne Nachrücker"));
    }

    /// <summary>Abnahme 7.4: neues Land mit 4 Ebenen, 64 Vereinen und Pokal ohne Texteditor anlegen und damit spielen.</summary>
    [Fact]
    public void WizardCreatesPlayableCountryWithFourLevelsSixtyFourClubsAndCup()
    {
        var ws = PackageWorkspace.Create(_root, "neuland", "Neuland", new[] { "base" });
        var files = CountryWizard.Create(ws, new NewCountrySpec("NL", "Neuland", new[] { 16, 16, 16, 16 }));
        Assert.Contains("countries/nl.json", files);
        Assert.Contains("names/nl.json", files);
        Assert.Equal(64, ((JsonArray)ws.Document("clubs/nl.json")!).Count);
        Assert.Empty(ws.Validate());
        var pyramid = CountryTools.Pyramid(Obj(ws.Document("countries/nl.json")));
        Assert.Equal(new[] { (0, 2), (2, 2), (2, 2), (2, 0) }, pyramid.Select(l => (l.Divisions[0].Up, l.Divisions[0].Down)));
        Assert.Throws<InvalidOperationException>(() => CountryWizard.Create(ws, new NewCountrySpec("NL", "Doppelt", new[] { 10 })));
        Assert.Throws<ArgumentException>(() => CountryWizard.Create(ws, new NewCountrySpec("N1", "Falsch", new[] { 10 })));

        ws.Save();
        var data = PackageLoader.Load(_root);
        Assert.Empty(DataValidator.Validate(data));
        var cup = Assert.Single(data.Countries.Single(c => c.Code == "NL").Cups);
        Assert.Equal(64, cup.Teams);
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = 11, ClubKey = "nl1-1", ManagerName = "Test", Activation = new() { ["DE"] = CountryActivation.Inactive },
        });
        Assert.Equal(64, world.Leagues.Sum(l => l.ClubIds.Count));
        var s = new GameSession(world, data, new TextMatchEngine());
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        Assert.Equal(4, world.Leagues.Count);
        Assert.All(world.Leagues, l => Assert.Equal(16, l.ClubIds.Count));
        Assert.Contains(world.Cups, c => c.Id.StartsWith("NL-CUP", StringComparison.Ordinal));
    }

    /// <summary>Das neue Feld wird nur geschrieben, wenn es gesetzt ist; ältere Spielstände ohne das Feld laden unverändert.</summary>
    [Fact]
    public void WithoutReplacementIsOnlySerializedWhenSet()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var plain = System.Text.Json.JsonSerializer.Serialize(new MovementRule { Positions = "17-18" }, options);
        Assert.DoesNotContain("withoutReplacement", plain);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<MovementRule>(plain, options)!.WithoutReplacement);
        var set = System.Text.Json.JsonSerializer.Serialize(new MovementRule { Positions = "17-18", WithoutReplacement = true }, options);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<MovementRule>(set, options)!.WithoutReplacement);
    }
}
