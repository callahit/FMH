using System.Text.Json.Nodes;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Tabellenarten für Spieler, Personal, KI-Manager und Schiedsrichter (Felder wie in data/schema).</summary>
public static class PeopleKinds
{
    public static readonly TableKind Players = new("players", "editor.kind.players", "key",
        new[] { "key", "firstName", "lastName", "club", "position", "age", "birthDate", "level", "potentialAbility", "reputation", "weeklyWage", "contractUntil" },
        n => new JsonObject
        {
            ["key"] = $"spieler{n}", ["firstName"] = EditorState.T["editor.people.newFirstName"],
            ["lastName"] = EditorState.F("editor.people.newPlayer", n), ["position"] = "MC",
        });

    public static readonly TableKind Staff = new("staff", "editor.kind.staff", "name",
        new[] { "name", "club", "role", "age", "ability", "motivation", "ageGroup", "contractUntil" },
        n => new JsonObject { ["name"] = EditorState.F("editor.people.newStaff", n), ["role"] = "AssistantManager", ["ability"] = 10, ["age"] = 45 });

    public static readonly TableKind Managers = new("managers", "editor.kind.managers", "name",
        new[] { "name", "club", "reputation", "style", "tactics", "motivation", "age", "nationality" },
        n => new JsonObject { ["name"] = EditorState.F("editor.people.newManager", n), ["reputation"] = 8 });

    public static readonly TableKind Referees = new("referees", "editor.kind.referees", "name",
        new[] { "name", "country", "strictness" },
        n => new JsonObject { ["name"] = EditorState.F("editor.people.newReferee", n), ["strictness"] = 10 });
}
