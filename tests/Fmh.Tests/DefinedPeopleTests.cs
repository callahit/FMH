using System.Text.Json.Nodes;
using Fmh.Core.Data;
using Fmh.Core.Data.Editing;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 6c: vorgegebene Spieler, Personal, KI-Manager und Schiedsrichter aus Datenpaketen.</summary>
public sealed class DefinedPeopleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-people-" + Guid.NewGuid().ToString("N"));

    public DefinedPeopleTests()
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

    private GameData Package(Dictionary<string, string> files)
    {
        var ws = PackageWorkspace.Create(_root, "personen", "Personen", new[] { "base" });
        foreach (var (file, json) in files) ws.Apply(new SetValue(file, JsonPath.Root, JsonNode.Parse(json)));
        ws.Save();
        return PackageLoader.Load(_root);
    }

    private const string Players = """
        [
          { "key": "t-1", "firstName": "Arno", "lastName": "Vorgabe", "club": "rheinfels", "position": "ST", "level": 18, "age": 27,
            "attributes": { "Finishing": 20, "pace": 19 }, "hidden": { "professionalism": 20 }, "potentialAbility": 190,
            "foot": "Left", "height": 191, "contractUntil": 2030, "positions": { "AMC": "Good" } },
          { "key": "t-2", "firstName": "Bert", "lastName": "Ohneverein", "position": "GK", "birthDate": "2004-03-15" },
          { "key": "t-3", "firstName": "Carl", "lastName": "Rahmen", "club": "lindenau", "position": "DC" }
        ]
        """;

    [Fact]
    public void DefinedPlayersReplaceGeneratedOnesAndKeepTheFrame()
    {
        var plain = WorldGenerator.Create(PackageLoader.Load(_root), new NewGameOptions { Seed = 5, ClubKey = "nordheide" });
        var data = Package(new() { ["players/test.json"] = Players });
        Assert.Empty(DataValidator.Validate(data));
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 5, ClubKey = "nordheide" });

        var club = world.Clubs.Values.Single(c => c.Key == "rheinfels");
        Assert.Equal(plain.Clubs.Values.Single(c => c.Key == "rheinfels").PlayerIds.Count, club.PlayerIds.Count);
        var p = world.Players.Values.Single(x => x.LastName == "Vorgabe");
        Assert.Equal(club.Id, p.ClubId);
        Assert.Contains(p.Id, club.PlayerIds);
        Assert.Equal(Position.ST, p.MainPosition);
        Assert.Equal(Suitability.Natural, p.SuitabilityFor(Position.ST));
        Assert.Equal(Suitability.Good, p.SuitabilityFor(Position.AMC));
        Assert.Equal(20, p.Attributes[Attr.Finishing]);
        Assert.Equal(19, p.Attributes[Attr.Pace]);
        Assert.Equal(20, p.Hidden.Professionalism);
        Assert.Equal(190, p.Hidden.PotentialAbility);
        Assert.Equal(Ratings.CurrentAbility(p, data.PositionProfiles), p.Hidden.CurrentAbility);
        Assert.Equal(27, p.Age(world.Date));
        Assert.Equal(PreferredFoot.Left, p.Foot);
        Assert.Equal(191, p.Height);
        Assert.Equal(new DateOnly(2030, 6, 30), p.Contract!.Until);
        Assert.True(Ratings.PositionRating(p, Position.ST, data.PositionProfiles) > 15);

        var free = world.Players.Values.Single(x => x.LastName == "Ohneverein");
        Assert.Null(free.ClubId);
        Assert.Null(free.Contract);
        Assert.Equal(new DateOnly(2004, 3, 15), free.BirthDate);
        Assert.Equal(7, Assert.Single(free.Registration.Formative).Value); // sieben Ausbildungsjahre (15–21) für die Meldequoten

        // Nur der Rahmen (Verein und Position): Rest erzeugt, Stärke nach Verein
        var frame = world.Players.Values.Single(x => x.LastName == "Rahmen");
        Assert.Equal(Position.DC, frame.MainPosition);
        Assert.InRange(frame.Hidden.CurrentAbility, 1, 200);
        Assert.True(frame.Hidden.PotentialAbility >= frame.Hidden.CurrentAbility);
    }

    [Fact]
    public void DefinedStaffManagersAndRefereesEnterTheWorld()
    {
        var data = Package(new()
        {
            ["staff/test.json"] = """
                [ { "name": "Dora Kotrainer", "club": "nordheide", "role": "AssistantManager", "ability": 19, "age": 51 },
                  { "name": "Emil Markt", "role": "Physio", "ability": 17 } ]
                """,
            ["managers/test.json"] = """
                [ { "name": "Franz Lindner", "club": "lindenau", "reputation": 14 },
                  { "name": "Gerd Heide", "club": "nordheide" } ]
                """,
            ["referees/test.json"] = """
                [ { "name": "Hanna Pfiff", "country": "DE", "strictness": 18 }, { "name": "Ina Karte", "strictness": 4 },
                  { "name": "Jan Fremd", "country": "ENG" } ]
                """,
        });
        Assert.Contains(DataValidator.Validate(data), e => e.Contains("unbekanntes Land 'ENG'"));
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 9, ClubKey = "nordheide" });
        var session = new GameSession(world, data, new TextMatchEngine());

        Assert.Equal(new[] { "Hanna Pfiff", "Ina Karte" }, world.Referees.Select(r => r.Name));
        var human = world.Clubs.Values.Single(c => c.Key == "nordheide");
        var assistant = Assert.Single(world.Staff, s => s.ClubId == human.Id && s.Role == StaffRole.AssistantManager);
        Assert.Equal(("Dora Kotrainer", 19, 51), (assistant.Name, assistant.Ability, assistant.Age));
        Assert.Contains(world.Staff, s => s.ClubId is null && s.Name == "Emil Markt" && s.Role == StaffRole.Physio);

        var lindenau = world.Clubs.Values.Single(c => c.Key == "lindenau");
        var lindner = world.Managers.Single(m => m.Name == "Franz Lindner");
        Assert.Equal((lindenau.Id, lindner.Id, 14), (lindner.ClubId!.Value, lindenau.ManagerId!.Value, lindner.Reputation));
        Assert.Null(world.Managers.Single(m => m.Name == "Gerd Heide").ClubId); // Verein des Spielers

        // Wechselt der Spieler zu Lindenau, übernimmt der KI-Manager seinen bisherigen Verein
        var offer = new JobOffer { ClubId = lindenau.Id, Expires = world.Date.AddDays(14), WeeklyWage = 5000 };
        world.Managers.Single(m => m.Human).Career.Offers.Add(offer);
        Assert.Null(session.AcceptJob(offer));
        Assert.Equal(human.Id, lindner.ClubId);
        Assert.Equal(lindner.Id, human.ManagerId);
    }

    [Fact]
    public void InconsistentPeopleAreReported()
    {
        var data = Package(new()
        {
            ["players/test.json"] = """
                [ { "key": "x", "firstName": "A", "lastName": "B", "club": "gibtsnicht", "position": "ST", "attributes": { "Pace": 25 } },
                  { "key": "x", "firstName": "C", "lastName": "", "position": "GK", "age": 20, "birthDate": "2005-01-01",
                    "positions": { "GK": "Good" } } ]
                """,
            ["staff/test.json"] = """[ { "name": "D", "club": "nordheide", "role": "Physio", "ageGroup": "U17" } ]""",
            ["managers/test.json"] = """[ { "name": "E", "club": "lindenau" }, { "name": "F", "club": "lindenau" } ]""",
        });
        var errors = DataValidator.Validate(data);
        Assert.Contains(errors, e => e.Contains("Spielerschlüssel 'x' ist mehrfach"));
        Assert.Contains(errors, e => e.Contains("unbekannter Verein 'gibtsnicht'"));
        Assert.Contains(errors, e => e.Contains("Attribut Pace = 25"));
        Assert.Contains(errors, e => e.Contains("Vor- und Nachname"));
        Assert.Contains(errors, e => e.Contains("entweder Geburtsdatum oder Alter"));
        Assert.Contains(errors, e => e.Contains("Hauptposition GK"));
        Assert.Contains(errors, e => e.Contains("nur für Jugendtrainer"));
        Assert.Contains(errors, e => e.Contains("mehr als einen Manager"));
    }
}
