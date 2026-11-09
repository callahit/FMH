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

/// <summary>AP 6: Editor-Oberfläche ohne Bildschirm – Paket anlegen, Vereine, Regeln, Sprachen, Prüfung, Unterschiede.</summary>
[Collection("Sprache")]
public sealed class EditorUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-editor-ui-" + Guid.NewGuid().ToString("N"));

    public EditorUiTests()
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

    private static IEnumerable<string> Texts(Window w) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void CreatePackageEditClubsUndoSaveAndBrowseAllSections()
    {
        var vm = new MainViewModel(_root);
        var window = new Window { Width = 1280, Height = 820, Content = new MainView { DataContext = vm } };
        window.Show();
        Pump();
        var start = Assert.IsType<StartViewModel>(vm.Page);
        Assert.Contains(start.Packages, p => p.Id == "base" && p.Base && !p.Editable);
        Assert.DoesNotContain(Texts(window), t => t.StartsWith("[editor.", StringComparison.Ordinal));
        Shot(window, "70_editor_start");

        start.NewId = "neue-welt";
        start.NewName = "Neue Welt";
        start.CreateCommand.Execute(null);
        Pump();
        var ws = Assert.IsType<WorkspaceViewModel>(vm.Page);
        Assert.Equal("Keine Fehler.", ws.ErrorSummary);

        // Vereine: Zelle bearbeiten, rückgängig, wiederholen
        var clubs = ws.Sections.OfType<ClubsSectionViewModel>().Single();
        ws.Selected = clubs;
        Pump();
        var oldName = clubs.Rows[0].Name;
        clubs.Rows[0].Name = "FC Neue Welt";
        Assert.Equal("FC Neue Welt", clubs.Rows[0].Name);
        Assert.True(ws.Dirty);
        Shot(window, "71_editor_clubs");
        ws.UndoCommand.Execute(null);
        Assert.Equal(oldName, clubs.Rows[0].Name);
        ws.RedoCommand.Execute(null);
        clubs.Rows[0].Capacity = "viele"; // keine Zahl: bleibt als Text und fällt in der Prüfung auf
        ws.RunValidation();
        Assert.NotEqual("Keine Fehler.", ws.ErrorSummary);
        ws.UndoCommand.Execute(null);
        ws.RunValidation();
        Assert.Equal("Keine Fehler.", ws.ErrorSummary);

        // Neuer Verein und CSV-Import als ein rückgängig machbarer Schritt
        var count = clubs.Rows.Count;
        clubs.AddCommand.Execute(null);
        Assert.Equal(count + 1, clubs.Rows.Count);
        var csv = clubs.ExportCsv();
        clubs.ImportCsv(csv);
        Assert.Equal(count + 1, clubs.Rows.Count);

        // Regeln und Inhalte: Wert im Baum ändern, Typ bleibt Zahl
        var files = ws.Sections.OfType<FilesSectionViewModel>().Single();
        ws.Selected = files;
        Pump();
        Assert.Equal("rules/engine.json", files.File);
        var leaf = files.Nodes.First(n => n.IsLeaf && double.TryParse(n.Value, System.Globalization.CultureInfo.InvariantCulture, out _));
        leaf.Value = "0.5";
        Assert.Equal("0.5", leaf.Value);
        leaf.Value = "keine Zahl";
        Assert.Equal("0.5", leaf.Value);
        Shot(window, "72_editor_files");

        // Sprachen: Text ändern, Filter
        var langs = ws.Sections.OfType<LanguagesSectionViewModel>().Single();
        ws.Selected = langs;
        Pump();
        Assert.Contains("de", langs.Languages);
        langs.Filter = "editor.title";
        var row = Assert.Single(langs.Rows);
        row["de"] = "Mein Editor";
        Assert.Equal("Mein Editor", row["de"]);
        Shot(window, "73_editor_languages");

        ws.Selected = ws.Sections.OfType<DiffSectionViewModel>().Single();
        Pump();
        var diff = (DiffSectionViewModel)ws.Selected;
        Assert.Contains(diff.Lines, l => l.File == "clubs/de.json");
        Assert.Contains(diff.Lines, l => l.File == "lang/de.json" && l.Path == "editor.title");
        Shot(window, "74_editor_diff");

        ws.SaveCommand.Execute(null);
        Assert.False(ws.Dirty);
        Assert.True(File.Exists(Path.Combine(_root, "neue-welt", "package.json")));
        Assert.True(File.Exists(Path.Combine(_root, "neue-welt", "clubs", "de.json")));
        var data = PackageLoader.Load(_root);
        Assert.Contains(data.Clubs, c => c.Name == "FC Neue Welt");
        Assert.Equal("Mein Editor", data.Texts["editor.title"]);

        ws.Selected = ws.Sections.OfType<PackageSectionViewModel>().Single();
        Pump();
        Shot(window, "75_editor_package");
        ws.CloseCommand.Execute(null);
        var again = Assert.IsType<StartViewModel>(vm.Page);
        Assert.Contains(again.Packages, p => p.Id == "neue-welt" && p.Editable);
    }

    [AvaloniaFact]
    public void NewClubKeysStayUniqueManifestEditsAreUndoableAndCloseWarnsWhenUnsaved()
    {
        var vm = new MainViewModel(_root);
        var start = Assert.IsType<StartViewModel>(vm.Page);
        start.NewId = "pruefung";
        start.CreateCommand.Execute(null);
        var ws = Assert.IsType<WorkspaceViewModel>(vm.Page);

        // Zwei neue Vereine, den ersten löschen, wieder einen anlegen: Schlüssel bleiben eindeutig
        var clubs = ws.Sections.OfType<ClubsSectionViewModel>().Single();
        clubs.AddCommand.Execute(null);
        clubs.AddCommand.Execute(null);
        clubs.SelectedRow = clubs.Rows[^2];
        clubs.RemoveCommand.Execute(null);
        clubs.AddCommand.Execute(null);
        var keys = clubs.Rows.Select(r => r.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        ws.RunValidation();
        // Keine doppelten Schlüssel; die Prüfung meldet nur die nun zu volle Liga (Ligagrößen stehen im Länder-Bereich)
        Assert.DoesNotContain(ws.Validation.Errors, e => e.Contains("mehrfach"));
        Assert.All(ws.Validation.Errors, e => Assert.Contains("braucht", e));
        ws.SaveCommand.Execute(null);

        // Name und Version des Pakets laufen über den Verlauf
        var package = ws.Sections.OfType<PackageSectionViewModel>().Single();
        package.Name = "Geänderter Name";
        Assert.True(ws.Dirty);
        ws.UndoCommand.Execute(null);
        Assert.Equal("pruefung", package.Name);

        // Schließen mit ungespeicherten Änderungen warnt zuerst
        package.Version = "2.0.0";
        ws.CloseCommand.Execute(null);
        Assert.Same(ws, vm.Page);
        Assert.Contains("Ungespeicherte", ws.Status);
        ws.CloseCommand.Execute(null);
        Assert.IsType<StartViewModel>(vm.Page);
        Assert.Equal("1.0.0", PackageWorkspace.Open(_root, "pruefung").Manifest.Version);
    }

    [AvaloniaFact]
    public void CountriesPyramidWizardFormatVersionTreeEditingAndTrialRun()
    {
        var vm = new MainViewModel(_root);
        var window = new Window { Width = 1280, Height = 900, Content = new MainView { DataContext = vm } };
        window.Show();
        var start = Assert.IsType<StartViewModel>(vm.Page);
        start.NewId = "laender";
        start.CreateCommand.Execute(null);
        var ws = Assert.IsType<WorkspaceViewModel>(vm.Page);

        // Pyramide Deutschlands, dann neues Land per Assistent
        var countries = ws.Countries;
        ws.Selected = countries;
        Pump();
        Assert.Equal(4, countries.Levels.Count);
        Assert.Equal(90, countries.Levels[3].Teams);
        countries.WizardCode = "NL";
        countries.WizardName = "Neuland";
        countries.WizardTeams = "16, 16, 16, 16";
        countries.CreateCountryCommand.Execute(null);
        Assert.Equal("countries/nl.json", countries.File);
        Assert.Equal(new[] { 16, 16, 16, 16 }, countries.Levels.Select(l => l.Teams));
        countries.WizardTeams = "16, viele";
        countries.CreateCountryCommand.Execute(null);
        Assert.Contains("Komma", countries.Message);
        ws.RunValidation();
        Assert.Equal("Keine Fehler.", ws.ErrorSummary);
        Shot(window, "76_editor_countries");

        // Neue Formatversion für Deutschland
        countries.File = "countries/de.json";
        countries.NewVersionSeason = "2030";
        countries.NewVersionCommand.Execute(null);
        Assert.Equal("countries/de-2030.json", countries.File);
        ws.RunValidation();
        Assert.Empty(countries.Problems);

        // Baum: Feld hinzufügen, Eintrag anhängen und wieder entfernen
        countries.EditInTreeCommand.Execute(null);
        var files = Assert.IsType<FilesSectionViewModel>(ws.Selected);
        Assert.Equal("countries/de-2030.json", files.File);
        files.Nodes.First(n => n.Name == "pools").AddItemCommand.Execute(null);
        Assert.Equal(2, files.Nodes.First(n => n.Name == "pools").Items.Count);
        files.Nodes.First(n => n.Name == "pools").Items[1].RemoveCommand.Execute(null);
        var cup = files.Nodes.First(n => n.Name == "cups").Items[0];
        cup.NewField = "testFeld";
        cup.NewValue = "3";
        cup.AddFieldCommand.Execute(null);
        Assert.Contains(files.Nodes.First(n => n.Name == "cups").Items[0].Items, i => i.Name == "testFeld" && i.Value == "3");
        files.Nodes.First(n => n.Name == "cups").Items[0].Items.First(i => i.Name == "testFeld").RemoveCommand.Execute(null);
        ws.RunValidation();
        Assert.Equal("Keine Fehler.", ws.ErrorSummary);
        Shot(window, "77_editor_tree");

        // Probelauf mit dem neuen Land
        var trial = ws.Sections.OfType<TrialSectionViewModel>().Single();
        ws.Selected = trial;
        Pump();
        Assert.Contains("NL", trial.Countries);
        trial.Country = "NL";
        trial.RunCommand.Execute(null);
        var until = DateTime.UtcNow.AddMinutes(5);
        while (trial.Running && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(50);
        }
        Pump();
        Assert.Empty(trial.Errors);
        Assert.Equal(4, trial.Leagues.Count);
        Assert.All(trial.Leagues, l => Assert.Equal(16, l.Clubs));
        Assert.All(trial.Leagues, l => Assert.InRange(l.GoalsPerMatch, 1.5, 4.5));
        Assert.Equal(new[] { 0, 2, 2, 2 }, trial.Leagues.Select(l => l.Up.Count));
        Assert.Equal(new[] { 2, 2, 2, 0 }, trial.Leagues.Select(l => l.Down.Count));
        Shot(window, "78_editor_trial");
    }
}
