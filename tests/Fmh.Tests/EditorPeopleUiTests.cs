using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Fmh.Core.Data;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;
using Fmh.Editor.ViewModels;
using Fmh.Editor.Views;

namespace Fmh.Tests;

/// <summary>AP 6c im Editor: Spieler- und Personaltabellen nach Schema, Baum mit Schemahilfen, Sprung aus der Fehlerliste.</summary>
[Collection("Sprache")]
public sealed class EditorPeopleUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-editor-people-" + Guid.NewGuid().ToString("N"));

    public EditorPeopleUiTests()
    {
        var source = Path.Combine(PackageLoader.FindDataRoot(), "base");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, "base", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        EditorState.Initialise(_root, "de");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, string name)
    {
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("FMH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public void PlayersAndPeopleTablesSchemaTreeAndJumpFromValidation()
    {
        var vm = new MainViewModel(_root);
        var window = new Window { Width = 1280, Height = 860, Content = new MainView { DataContext = vm } };
        window.Show();
        var start = Assert.IsType<StartViewModel>(vm.Page);
        start.NewId = "personen";
        start.CreateCommand.Execute(null);
        var ws = Assert.IsType<WorkspaceViewModel>(vm.Page);

        // Spieler: neue Datei mit dem ersten Eintrag, Werte im Typ des Schemas
        var players = ws.Players;
        ws.Selected = players;
        Pump();
        Assert.Equal("players/personen.json", players.File);
        players.AddCommand.Execute(null);
        players.AddCommand.Execute(null);
        Assert.Equal(2, players.Rows.Count);
        var row = players.Rows[0];
        row["lastName"] = "Muster";
        row["club"] = "rheinfels";
        row["position"] = "st";
        row["age"] = "27";
        var doc = (JsonArray)ws.Workspace.Document("players/personen.json")!;
        Assert.Equal("ST", doc[0]!["position"]!.GetValue<string>());
        Assert.Equal(27, doc[0]!["age"]!.GetValue<long>());
        row["age"] = "alt";
        Assert.Contains("nicht gültig", players.Message);
        Assert.Equal("27", row["age"]);
        row["position"] = "Sturm";
        Assert.Contains("GK", players.Message);
        Assert.Equal("spieler2", players.Rows[1]["key"]);
        ws.RunValidation();
        Assert.Equal("Keine Fehler.", ws.ErrorSummary);
        Shot(window, "79_editor_players");

        // CSV: Ein Import ändert Spalten und lässt übrige Felder (Attribute) stehen
        ws.Workspace.Apply(new SetValue("players/personen.json", JsonPath.Root.Then(0).Then("attributes"), new JsonObject { ["Finishing"] = 18 }));
        var csv = players.ExportCsv();
        Assert.StartsWith("key;firstName;lastName;club;position;age;", csv);
        players.ImportCsv(csv.Replace(";Muster;", ";Mustermann;"));
        Assert.Equal("2 Einträge importiert.", players.Message);
        doc = (JsonArray)ws.Workspace.Document("players/personen.json")!;
        Assert.Equal("Mustermann", doc[0]!["lastName"]!.GetValue<string>());
        Assert.Equal(18, doc[0]!["attributes"]!["Finishing"]!.GetValue<int>());

        // Personal, Manager, Schiedsrichter in einem Bereich
        var people = ws.People;
        ws.Selected = people;
        Pump();
        people.AddCommand.Execute(null);
        people.Rows[0]["club"] = "nordheide";
        people.Rows[0]["ability"] = "17";
        people.Kind = PeopleKinds.Managers;
        Pump();
        Assert.Equal("managers/personen.json", people.File);
        people.AddCommand.Execute(null);
        people.Rows[0]["club"] = "lindenau";
        people.Kind = PeopleKinds.Referees;
        people.AddCommand.Execute(null);
        people.Rows[0]["country"] = "XX";
        Pump();
        ws.RunValidation();
        Assert.Contains(ws.Validation.Errors, e => e.Contains("unbekanntes Land 'XX'"));
        Shot(window, "80_editor_people");
        people.Rows[0]["country"] = "DE";

        // Baum: Beschreibung und Feldvorschläge aus dem Schema, Werte im richtigen Typ
        ws.Selected = players;
        players.EditInTreeCommand.Execute(null);
        var files = Assert.IsType<FilesSectionViewModel>(ws.Selected);
        Assert.Equal("players/personen.json", files.File);
        var second = files.Nodes[1];
        Assert.Contains("hidden", second.FieldSuggestions);
        Assert.DoesNotContain("key", second.FieldSuggestions);
        Assert.Contains("Eindeutiger Schlüssel", second.Items.First(i => i.Name == "key").Description);
        second.NewField = "hidden";
        second.AddFieldCommand.Execute(null);
        var hidden = files.Nodes[1].Items.First(i => i.Name == "hidden");
        hidden.NewField = "loyalty";
        hidden.NewValue = "15";
        hidden.AddFieldCommand.Execute(null);
        Assert.Equal(15, ((JsonArray)ws.Workspace.Document("players/personen.json")!)[1]!["hidden"]!["loyalty"]!.GetValue<long>());

        // Tippfehler im Feldnamen: Fehlerliste meldet Datei und Pfad, „Zur Stelle“ öffnet den Baum dort
        var first = files.Nodes[0];
        first.NewField = "positon";
        first.NewValue = "ST";
        first.AddFieldCommand.Execute(null);
        ws.RunValidation();
        var item = Assert.Single(ws.Validation.Items, i => i.CanOpen);
        Assert.Contains("unbekanntes Feld „positon“", item.Text);
        Assert.Equal(("players/personen.json", "[0].positon"), (item.File, item.Path!.ToString()));
        ws.Selected = ws.Validation;
        Pump();
        Shot(window, "81_editor_validation_jump");
        item.OpenCommand.Execute(null);
        Pump();
        Assert.Same(files, ws.Selected);
        Assert.Equal("[0].positon", files.SelectedNode!.Path.ToString());
        Assert.True(files.Nodes[0].IsExpanded);
        Shot(window, "82_editor_tree_schema");
    }

    [Fact]
    public void PathsFromValidationMessagesAreParsed()
    {
        Assert.Equal("levels[0].divisions[1].teams", ValidationSectionViewModel.ParsePath("levels[0].divisions[1].teams")!.ToString());
        Assert.Equal("[3].attributes.Pace", ValidationSectionViewModel.ParsePath("[3].attributes.Pace")!.ToString());
        Assert.Null(ValidationSectionViewModel.ParsePath("Land 'DE': keine Namensliste"));
    }
}
