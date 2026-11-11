using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Tests;

/// <summary>AP 12b: Screenreader-Namen, Skalierung, Kontrastthema, Tutorial und Tastenbelegung in der Oberfläche.</summary>
[Collection("Sprache")]
public class AccessibilityUiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window, MainWindowViewModel, ShellViewModel) Game()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4721";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        Pump();
        return (w, vm, Assert.IsType<ShellViewModel>(vm.Screen));
    }

    /// <summary>Einstellungen der Testanwendung gelten für alle Oberflächentests: nach jedem Test zurücksetzen.</summary>
    private static void ResetSettings()
    {
        var s = AppState.Current.Settings;
        s.KeyBindings.Clear();
        s.FontScale = 1.0;
        s.Tutorial = true;
        s.TutorialSeen.Clear();
        AppState.Current.SaveSettings();
    }

    private static List<string> UnnamedButtons(Window w) => w.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible)
        .Where(b => string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(b).GetName()))
        .Select(b => $"{b.GetType().Name} in {b.FindAncestorOfType<UserControl>()?.GetType().Name}").ToList();

    [AvaloniaFact]
    public void EveryVisibleButtonHasAScreenReaderName()
    {
        try
        {
            var (w, vm, shell) = Game();
            var missing = new List<string>();
            for (var i = 0; i < shell.Nav.Count; i++)
            {
                shell.Open(shell.Nav[i]);
                Pump();
                missing.AddRange(UnnamedButtons(w));
                // Alle Reiter der Seite.
                foreach (var tabs in w.GetVisualDescendants().OfType<TabControl>().ToList())
                    for (var t = 0; t < tabs.ItemCount; t++)
                    {
                        tabs.SelectedIndex = t;
                        Pump();
                        missing.AddRange(UnnamedButtons(w));
                    }
            }
            shell.OptionsCommand.Execute(null);
            Pump();
            missing.AddRange(UnnamedButtons(w));
            Assert.True(missing.Count == 0, "Knöpfe ohne Namen: " + string.Join(", ", missing.Distinct()));
            // Symbolknöpfe tragen den Tooltip als Namen.
            vm.CloseDialog();
            Pump();
            var save = w.GetVisualDescendants().OfType<Button>().First(b => b.Command == shell.SaveCommand);
            Assert.Equal(AppState.Current.T["tip.save"], ControlAutomationPeer.CreatePeerForElement(save).GetName());
        }
        finally { ResetSettings(); }
    }

    [AvaloniaFact]
    public void ScaleContrastAndTutorialFollowTheOptions()
    {
        try
        {
            var (w, vm, shell) = Game();
            // Tutorial: Hinweis zur Seite, nach "Verstanden" nicht wieder.
            Assert.Equal("inbox", shell.Tutorial?.Id);
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == shell.TutorialTitle);
            shell.TutorialDoneCommand.Execute(null);
            Assert.False(shell.HasTutorial);
            shell.Open(shell.Nav[1]);
            Assert.Equal("squad", shell.Tutorial?.Id);
            shell.Open(shell.Nav[0]);
            Assert.Null(shell.Tutorial);
            Assert.Contains("inbox", AppState.Current.Settings.TutorialSeen);
            shell.TutorialOffCommand.Execute(null);
            shell.Open(shell.Nav[2]);
            Assert.Null(shell.Tutorial);

            // Einstellungen: Skalierung, Kontrastthema, Tutorial zurücksetzen.
            shell.OptionsCommand.Execute(null);
            var options = Assert.IsType<OptionsViewModel>(vm.Dialog);
            Pump();
            options.Access[0].SelectedIndex = AppState.Current.Data.Ui.FontScales.Count - 1;
            Assert.Equal(AppState.Current.Data.Ui.FontScales[^1], vm.Scale);
            Assert.Equal(vm.Scale, AppState.Current.Settings.FontScale);
            Pump();
            var root = w.GetVisualDescendants().OfType<LayoutTransformControl>().First();
            Assert.Equal(vm.Scale, root.LayoutTransform!.Value.M11, 3);
            var theme = options.Settings.Single(s => s.Label == AppState.Current.T["options.theme"]);
            theme.SelectedIndex = Palette.All.ToList().IndexOf(Palette.HighContrast);
            Assert.Equal(Palette.HighContrast, Palette.Current);
            Assert.Equal("Kontrast", AppState.Current.Session!.HumanManager.Profile.Theme);
            options.ResetTutorialCommand.Execute(null);
            Assert.True(AppState.Current.Settings.Tutorial);
            Assert.Equal("tactics", shell.Tutorial?.Id);
            Pump();
            Assert.Empty(UnnamedButtons(w));
            vm.CloseDialog();
            vm.SetScale(1.0);
            Palette.Apply(Palette.Classic.Name);
        }
        finally { ResetSettings(); }
    }

    [AvaloniaFact]
    public void KeyBindingsCanBeChangedInTheDialog()
    {
        try
        {
            var (w, vm, shell) = Game();
            Assert.True(vm.HandleKey(Key.F2, KeyModifiers.None));
            Assert.IsType<SquadPageViewModel>(shell.Page);

            shell.OptionsCommand.Execute(null);
            ((OptionsViewModel)vm.Dialog!).KeyBindingsCommand.Execute(null);
            var dialog = Assert.IsType<KeyBindingsViewModel>(vm.Dialog);
            Pump();
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == AppState.Current.T["keys.title"]);
            // Kader auf F1 legen: der Posteingang übernimmt F2.
            dialog.CaptureCommand.Execute(dialog.Rows.Single(r => r.Action == "page.squad"));
            Assert.True(vm.HandleKey(Key.LeftCtrl, KeyModifiers.Control));
            Assert.NotNull(dialog.Capturing);
            Assert.True(vm.HandleKey(Key.F1, KeyModifiers.None));
            Assert.Null(dialog.Capturing);
            Assert.Equal("F1", dialog.Rows.Single(r => r.Action == "page.squad").Gesture);
            Assert.Equal("F2", dialog.Rows.Single(r => r.Action == "page.inbox").Gesture);
            Assert.Contains("F2", dialog.Message);
            // Reservierte Taste wird abgelehnt, die Aufnahme läuft weiter.
            dialog.CaptureCommand.Execute(dialog.Rows.Single(r => r.Action == "continue"));
            Assert.True(vm.HandleKey(Key.Enter, KeyModifiers.None));
            Assert.NotNull(dialog.Capturing);
            Assert.DoesNotContain("continue", AppState.Current.Settings.KeyBindings.Keys);
            // Esc bricht nur die Aufnahme ab, nicht den Dialog.
            Assert.True(vm.HandleKey(Key.Escape, KeyModifiers.None));
            Assert.Same(dialog, vm.Dialog);
            dialog.CloseCommand.Execute(null);
            Assert.IsType<OptionsViewModel>(vm.Dialog);
            vm.CloseDialog();

            var newShell = Assert.IsType<ShellViewModel>(vm.Screen);
            Assert.Equal("F1", newShell.Nav[1].Shortcut);
            Assert.True(vm.HandleKey(Key.F1, KeyModifiers.None));
            Assert.IsType<SquadPageViewModel>(newShell.Page);
            Assert.True(vm.HandleKey(Key.F2, KeyModifiers.None));
            Assert.IsType<InboxPageViewModel>(newShell.Page);
            Assert.Contains(AppState.Current.T["keys.name.Space"], newShell.HelpText);

            // Standard wiederherstellen.
            newShell.OptionsCommand.Execute(null);
            ((OptionsViewModel)vm.Dialog!).KeyBindingsCommand.Execute(null);
            ((KeyBindingsViewModel)vm.Dialog!).ResetCommand.Execute(null);
            Assert.Empty(AppState.Current.Settings.KeyBindings);
        }
        finally { ResetSettings(); }
    }
}
