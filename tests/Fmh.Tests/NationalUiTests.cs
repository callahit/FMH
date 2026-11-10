using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Core.Logic;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Tests;

/// <summary>AP 8c: Seite Nationalteam als Nationaltrainer (Vormerkliste, Spielsystem, Turnier, Angebot im Posteingang).</summary>
[Collection("Sprache")]
public class NationalUiTests
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
    public void NationalPageLetsTheCoachPlanTheSquad()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4712";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var s = AppState.Current.Session!;

        // Ohne Amt: Hinweis auf den nötigen Ruf
        shell.OpenPage<NationalPageViewModel>();
        Pump();
        var page = Assert.IsType<NationalPageViewModel>(shell.Page);
        Assert.False(page.IsCoach);
        Assert.Contains(VisibleTexts(w), t => t.Contains("kein Nationaltrainer"));

        // Angebot annehmen und Seite neu öffnen
        NationalSquads.Ensure(s.World, s.Data);
        var de = NationalSquads.TeamOf(s.World, "DE")!;
        de.CoachId = null;
        de.OfferExpires = s.World.Date.AddDays(10);
        Assert.Null(s.AcceptNationalJob(de));
        shell.OpenPage<InboxPageViewModel>();
        shell.OpenPage<NationalPageViewModel>();
        Pump();
        page = Assert.IsType<NationalPageViewModel>(shell.Page);
        Assert.True(page.IsCoach);
        Assert.Contains(VisibleTexts(w), t => t.StartsWith("Nationaltrainer von"));
        Assert.Equal(s.Data.Internationals.SquadSize, page.Candidates.Count(c => c.Selected));

        // Haken entfernen ändert die Vormerkliste, Spielsystem wird übernommen
        page.Candidates.First(c => c.Selected).Selected = false;
        Assert.Equal(s.Data.Internationals.SquadSize - 1, de.PlannedSquad.Count);
        Assert.Contains("22/23", page.SquadTitle);
        page.Formation = "433";
        Assert.Equal("433", de.Formation);
        page.SuggestCommand.Execute(null);
        Assert.Equal(s.Data.Internationals.SquadSize, de.PlannedSquad.Count);
        Assert.DoesNotContain(VisibleTexts(w), t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\[[a-z]+\.[A-Za-z.0-9]+\]$"));
        page.ResignCommand.Execute(null);
        Assert.False(page.IsCoach);
    }
}
