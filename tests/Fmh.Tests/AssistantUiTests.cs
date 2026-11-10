using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Tests;

/// <summary>AP 11a: Bericht des Co-Trainers auf der Trainingsseite und Urlaub über die Kopfzeile.</summary>
[Collection("Sprache")]
public class AssistantUiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<string> VisibleTexts(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void TrainingReportAndHolidayWorkThroughTheUi()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4714";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var s = AppState.Current.Session!;

        // Trainingsseite: Bericht mit mindestens einem Befund, Übernahme meldet sich
        shell.OpenPage<TrainingPageViewModel>();
        Pump();
        var training = Assert.IsType<TrainingPageViewModel>(shell.Page);
        Assert.NotEmpty(training.AssistantFindings);
        Assert.Contains(VisibleTexts(w), t => t == "Bericht des Co-Trainers");
        training.ApplyAssistantCommand.Execute(null);
        Assert.False(string.IsNullOrEmpty(training.AssistantMessage));

        // Urlaub: Dialog, eine Woche, Bericht danach
        shell.HolidayCommand.Execute(null);
        Pump();
        var holiday = Assert.IsType<HolidayDialogViewModel>(vm.Dialog);
        Assert.Equal(5, holiday.Durations.Count);
        holiday.DurationIndex = 0;
        var start = s.World.Date;
        holiday.StartCommand.Execute(null);
        Pump();
        Assert.Equal(start.AddDays(7), s.World.Date);
        var report = Assert.IsType<MessageDialogViewModel>(vm.Dialog);
        Assert.StartsWith("Zurück aus dem Urlaub", report.Title);
        Assert.DoesNotContain(VisibleTexts(w), t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\[[a-z]+\.[A-Za-z.0-9]+\]$"));
    }
}
