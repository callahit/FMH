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

/// <summary>AP 13a: Anstoß-Modus im Startdialog und Einzelschalter in den Einstellungen.</summary>
[Collection("Sprache")]
public class AnstossUiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window, MainWindowViewModel, NewGameViewModel) NewGame()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        Pump();
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4731";
        ng.ManagerName = "Test";
        return (w, vm, ng);
    }

    [AvaloniaFact]
    public void NewGameCanStartWithoutAnstossMode()
    {
        var (w, vm, ng) = NewGame();
        Assert.True(ng.HasAnstoss);
        Assert.True(ng.Anstoss);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == AppState.Current.T["newGame.anstoss"]);
        ng.Anstoss = false;
        ng.StartCommand.Execute(null);
        var session = AppState.Current.Session!;
        Assert.False(session.AnstossOn);
        Assert.False(Anstoss.IsOn(session.World, session.HumanManager.Id, "anstoss.satire"));
    }

    [AvaloniaFact]
    public void OptionsSwitchSingleElementsInTheRunningGame()
    {
        var (w, vm, ng) = NewGame();
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var session = AppState.Current.Session!;
        Assert.True(session.AnstossOn);
        shell.OptionsCommand.Execute(null);
        var options = Assert.IsType<OptionsViewModel>(vm.Dialog);
        Pump();
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == AppState.Current.T["options.anstossTitle"]);
        Assert.Equal(1 + AppState.Current.Data.Anstoss.Elements.Count, options.Anstoss.Count);
        var curiosities = options.Anstoss.Single(s => s.Label.Trim() == AppState.Current.T["anstoss.curiosities.name"]);
        curiosities.SelectedIndex = 0;
        Assert.False(session.AnstossElementOn("curiosities"));
        Assert.True(session.AnstossElementOn("satire"));
        options.Anstoss[0].SelectedIndex = 0;
        Assert.False(session.AnstossOn);
        vm.CloseDialog();
    }

    [AvaloniaFact]
    public void PrivateLifeDialogChangesHobbies()
    {
        var (w, vm, ng) = NewGame();
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        shell.Open(shell.Nav.Single(n => n.Id == "club"));
        var club = Assert.IsType<ClubPageViewModel>(shell.Page);
        Assert.True(club.HasPrivateLife);
        club.OpenPrivateLifeCommand.Execute(null);
        var dialog = Assert.IsType<PrivateLifeViewModel>(vm.Dialog);
        Pump();
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == AppState.Current.T["private.title"]);
        Assert.Equal(AppState.Current.Data.PrivateLife!.Homes.Count, dialog.Homes.Count);
        Assert.Single(dialog.Homes, h => h.Owned);
        var before = dialog.Savings;
        dialog.Hobbies.Single(h => h.Name == AppState.Current.T["private.golf"]).Command.Execute(null);
        Assert.Contains("golf", AppState.Current.Session!.PrivateLife!.Hobbies);
        Assert.NotEqual(before, dialog.Savings);
        Assert.True(dialog.Hobbies.Single(h => h.Name == AppState.Current.T["private.golf"]).Owned);
        dialog.Homes.Single(h => h.Name == AppState.Current.T["private.estate"]).Command.Execute(null);
        Assert.Equal(AppState.Current.T["private.error.money"], dialog.Message);
        Pump();
        vm.CloseDialog();
    }
}
