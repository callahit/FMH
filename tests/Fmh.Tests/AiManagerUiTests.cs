using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Core.Logic;
using Fmh.Desktop;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Tests;

/// <summary>KI-Manager in der Oberfläche: Liga-Ansicht „Trainer“ mit Wechseln und vereinslosen Trainern.</summary>
[Collection("Sprache")]
public sealed class AiManagerUiTests
{
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

    private static IEnumerable<string> VisibleTexts(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void LeagueManagersViewShowsManagersChangesAndFreeManagers()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        var start = Assert.IsType<StartViewModel>(vm.Screen);
        start.NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4711";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var session = AppState.Current.Session!;

        // Ein Trainerwechsel in der eigenen Liga
        var leagueModel = session.World.League(session.HumanClub.LeagueId);
        var club = session.World.Clubs[leagueModel.ClubIds.First(id => id != session.HumanClub.Id)];
        var previous = session.ManagerOf(club)!;
        ManagerGenerator.Release(previous, club, session.World.Date, "sacked");
        session.FillManagerVacancies();

        shell.OpenPage<LeaguePageViewModel>();
        var league = Assert.IsType<LeaguePageViewModel>(shell.Page);
        league.ShowManagers = true;
        Assert.False(league.ShowTable);
        Assert.True(league.ShowManagerList);
        Assert.Equal(leagueModel.ClubIds.Count, league.Managers.Count);
        Assert.Contains(league.Managers, m => m.Manager == "Test (du)");
        Assert.All(league.Managers.Where(m => !m.Manager.EndsWith("(du)")), m => Assert.NotEmpty(m.Style));
        var change = Assert.Single(league.CoachChanges);
        Assert.Contains(club.Name, change);
        Assert.Contains(previous.Name, change);
        Assert.Contains("entlassen", change);
        Assert.NotEmpty(league.FreeManagers);
        Pump();
        Assert.Contains(VisibleTexts(w), t => t == "Trainerwechsel dieser Saison");
        Assert.DoesNotContain(VisibleTexts(w), t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\[[a-z]+\.[A-Za-z.0-9]+\]$"));
        Shot(w, "83_league_managers");

        // Torjäger und Trainer schließen sich aus
        league.ShowScorers = true;
        Assert.False(league.ShowManagers);
        Assert.True(league.ShowScorerList);
    }
}
