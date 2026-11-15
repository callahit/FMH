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

/// <summary>Editor: Szenen des Textmodus mit Bedingungen und Texten je Sprache bearbeiten.</summary>
[Collection("Sprache")]
public sealed class EditorScenesUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-editor-scenes-" + Guid.NewGuid().ToString("N"));

    public EditorScenesUiTests()
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

    [AvaloniaFact]
    public void ScenesAreListedEditedPerLanguageAndSaved()
    {
        var vm = new MainViewModel(_root);
        var window = new Window { Width = 1400, Height = 900, Content = new MainView { DataContext = vm } };
        window.Show();
        var start = Assert.IsType<StartViewModel>(vm.Page);
        start.NewId = "szenen";
        start.NewName = "Szenen";
        start.CreateCommand.Execute(null);
        Pump();
        var ws = Assert.IsType<WorkspaceViewModel>(vm.Page);
        var scenes = ws.Sections.OfType<ScenesSectionViewModel>().Single();
        ws.Selected = scenes;
        Pump();

        var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base" });
        Assert.Equal(data.Scenes.Scenes.Count, scenes.SceneCount);
        Assert.Equal(data.Scenes.Rare.Count, scenes.RareCount);
        Assert.Equal(0, scenes.ProblemCount);
        Assert.Contains("de", scenes.Languages);
        Assert.Contains("en", scenes.Languages);
        Assert.DoesNotContain("de.scenes", ws.Sections.OfType<LanguagesSectionViewModel>().Single().Languages);

        // Filter nach Ereignis, Bedingung setzen und wieder entfernen
        scenes.Filter = "goal.header";
        var header = scenes.Items.First(i => i.On.Contains("goal.header"));
        scenes.Selected = scenes.Items.First(i => i.Id == header.Id);
        Pump();
        header.MinMinute = "60";
        Assert.Equal("60", header.MinMinute);
        Assert.Equal(60, (int)((JsonObject)header.Node["when"]!)["minMinute"]!);
        header.Attrs = "Heading>=14, Composure<=9";
        Assert.Equal("Heading>=14, Composure<=9", header.Attrs);
        header.MinMinute = "";
        Assert.Null(((JsonObject)header.Node["when"]!)["minMinute"]);

        // Texte je Sprache: englische Variante ändern, fehlender Text wird markiert
        var step = header.Steps[^1];
        var en = step.Texts.Single(t => t.Language == "en");
        en.Text = en.Text + "\n{p} again!";
        Assert.EndsWith("{p} again!", step["en"]);
        en.Text = "";
        Assert.True(step.Problem);
        Assert.True(header.Problem);
        ws.UndoCommand.Execute(null);
        Assert.False(step.Problem);

        // Neue Zeile und Kopie der Szene
        var lines = header.Steps.Count;
        header.AddStepCommand.Execute(null);
        Assert.Equal(lines + 1, header.Steps.Count);
        Assert.True(header.Steps[^1].Problem); // neuer Schlüssel ohne Texte
        header.Steps[^1].Texts.Single(t => t.Language == "de").Text = "{p} jubelt.";
        header.Steps[^1].Texts.Single(t => t.Language == "en").Text = "{p} celebrates.";
        Assert.False(header.Steps[^1].Problem);
        scenes.Filter = "";
        Assert.Same(header, scenes.Selected); // Auswahl bleibt beim Leeren des Filters
        var count = scenes.SceneCount;
        scenes.AddCommand.Execute(null);
        Assert.Equal(count + 1, scenes.SceneCount);
        Assert.Equal(header.Id + ".copy", scenes.Selected!.Id);
        Assert.Equal(header.Steps.Count, scenes.Selected.Steps.Count);
        scenes.Selected = scenes.Items.First(i => i.Id == header.Id);
        Pump();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("FMH_SHOTS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, "79_editor_scenes.png"));
        }

        // Speichern: Paket enthält Szenen und Texte, das Spiel lädt sie
        ws.SaveCommand.Execute(null);
        var loaded = PackageLoader.Load(new[] { _root }, enabledPackages: new[] { "base", "szenen" });
        Assert.Equal(count + 1, loaded.Scenes.Scenes.Count);
        loaded.UseLanguage("en");
        Assert.Contains("{p} celebrates.", loaded.Commentary[header.Steps[^1].Key]);
    }
}
