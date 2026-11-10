using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Tests;

/// <summary>AP 9: Testspiele auf der Seite Spielplan vereinbaren und absagen.</summary>
[Collection("Sprache")]
public class FriendlyUiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void FixturesPageArrangesAndCancelsFriendlies()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4713";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var s = AppState.Current.Session!;

        shell.OpenPage<FixturesPageViewModel>();
        var page = Assert.IsType<FixturesPageViewModel>(shell.Page);
        page.FriendlyView = true;
        Pump();
        Assert.True(page.NoFriendlies);
        Assert.NotEmpty(page.Opponents);
        // Ein Gegner mit ähnlichem Ruf, Heimspiel mit halber Teilung
        page.Opponent = page.Opponents.OrderBy(o => Math.Abs(o.Club.Reputation - s.HumanClub.Reputation)).First();
        Assert.NotEmpty(page.FriendlyDates);
        page.Share = 50;
        page.ArrangeFriendlyCommand.Execute(null);
        Pump();
        Assert.Single(page.Friendlies);
        Assert.Contains("vereinbart", page.FriendlyMessage);
        Assert.Contains(page.Own, f => f.Fixture.Kind == Fmh.Core.Model.CompetitionKind.Friendly && f.Matchday == "Testspiel");
        var texts = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\[[a-z]+\.[A-Za-z.0-9]+\]$"));
        page.Friendlies[0].Cancel.Execute(null);
        Assert.True(page.NoFriendlies);
        Assert.Empty(s.OwnFriendlies);

        // AP 9b: Anmeldung zu einem Sommerturnier
        Assert.Equal(2, page.SummerTournaments.Count(t => t.CanEnter));
        page.SummerTournaments.First(t => t.CanEnter).Enter.Execute(null);
        Assert.StartsWith("Angemeldet", page.FriendlyMessage);
        Assert.Contains(page.SummerTournaments, t => !t.CanEnter && t.Detail == "läuft");
        Assert.Single(page.Friendlies); // das eigene Halbfinale
    }
}
