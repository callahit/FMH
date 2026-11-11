using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Core.Model;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

namespace Fmh.Tests;

/// <summary>AP 12a: Spielmodus im Startdialog, Herausforderung mit festem Verein, Stand auf der Vereinsseite.</summary>
[Collection("Sprache")]
public class ChallengeUiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<string> VisibleTexts(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    private static (Window, MainWindowViewModel, NewGameViewModel) NewGame()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        Pump();
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4716";
        ng.ManagerName = "Test";
        return (w, vm, ng);
    }

    [AvaloniaFact]
    public void ChallengeFixesTheClubAndShowsItsGoal()
    {
        var (w, vm, ng) = NewGame();
        Assert.Equal(3, ng.Modes.Count);
        Assert.Contains("Spielmodus", VisibleTexts(w));
        ng.SelectedMode = ng.Modes[2];
        ng.SelectedChallenge = ng.ChallengeChoices.Single(c => c.Rule.Id == "survival");
        Pump();
        var club = ng.SelectedChallenge.Club!;
        Assert.Same(club, ng.Selected);
        Assert.Contains("Frist: 1 Saison(s)", ng.ModeInfo);
        // Andere Vereine lassen sich bei einer Herausforderung nicht wählen.
        ng.SelectClubCommand.Execute(ng.Clubs.First(c => c != club));
        Assert.Same(club, ng.Selected);
        Assert.Contains(VisibleTexts(w), t => t == "Klassenerhalt mit dem Letzten");

        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var s = AppState.Current.Session!;
        Assert.Equal((GameMode.Challenge, "survival"), (s.Mode!.Mode, s.Mode.ChallengeId));
        Assert.Equal(club.Def.Key, s.HumanClub.Key);
        shell.OpenPage<ClubPageViewModel>();
        Pump();
        var page = Assert.IsType<ClubPageViewModel>(shell.Page);
        Assert.Contains(page.CareerFacts, l => l.Label == "Herausforderung" && l.Value.Contains("Klassenerhalt mit dem Letzten") && l.Value.EndsWith("offen"));
    }

    [AvaloniaFact]
    public void QuickSeasonStartsFromTheDialog()
    {
        var (_, vm, ng) = NewGame();
        ng.SelectedMode = ng.Modes[1];
        Assert.StartsWith("Eine Saison ohne Halt", ng.ModeInfo);
        ng.StartCommand.Execute(null);
        Assert.IsType<ShellViewModel>(vm.Screen);
        Assert.Equal(GameMode.QuickSeason, AppState.Current.Session!.Mode!.Mode);
    }
}
