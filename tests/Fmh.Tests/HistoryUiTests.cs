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

/// <summary>AP 11b: Historie-Seite (Vereins- und Wettbewerbsrekorde, Meisterchronik) und Karriere auf der Spielerseite.</summary>
[Collection("Sprache")]
public class HistoryUiTests
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
    public void HistoryPageShowsRecordsChronicleAndCareer()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Pump();
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4715";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        var shell = Assert.IsType<ShellViewModel>(vm.Screen);
        var s = AppState.Current.Session!;
        var world = s.World;
        var own = s.HumanClub;
        var rival = world.Clubs[world.LeagueOf(own).ClubIds.First(id => id != own.Id)];

        // Ein Rekordspiel und eine abgeschlossene Saison in der Welt
        world.Records.Add(new MatchRecord
        {
            Scope = MatchRecord.ClubScope(own.Id), Kind = RecordKinds.BiggestWin, SeasonYear = world.SeasonYear, Date = world.Date,
            CompetitionId = own.LeagueId, HomeId = own.Id, AwayId = rival.Id, HomeGoals = 6, AwayGoals = 0, Attendance = 12_345,
        });
        world.History.Add(new SeasonRecord { SeasonYear = world.SeasonYear - 1, LeagueId = own.LeagueId, LeagueName = "Liga", ChampionId = rival.Id, ChampionName = rival.Name });

        shell.OpenPage<HistoryPageViewModel>();
        Pump();
        var page = Assert.IsType<HistoryPageViewModel>(shell.Page);
        Assert.Equal(own.Id, page.Club!.Id);
        Assert.Equal(own.LeagueId, page.Competition!.Id);
        var texts = VisibleTexts(w).ToList();
        Assert.Contains("Vereinsrekorde (Pflichtspiele)", texts);
        Assert.Contains("Meisterchronik", texts);
        Assert.Contains(texts, t => t.Contains($"{own.Name} – {rival.Name} 6 : 0"));
        Assert.Contains(page.Chronicle, c => c.Champion == rival.Name);
        Assert.Contains(page.TitleTable, l => l.Label == rival.Name && l.Value == "1 Titel");
        Assert.Contains(page.Honours, l => l.Label == "Noch keine Titel");

        // Anderer Verein: eigene Rekorde, Titel aus der Chronik
        page.Club = page.Clubs.First(c => c.Id == rival.Id);
        Pump();
        Assert.Equal("–", page.ClubRecords[0].Value);
        Assert.Contains(page.Honours, l => l.Label.EndsWith("(1×)", StringComparison.Ordinal));

        // Spielerseite: Karriere mit Gesamtbilanz
        var player = world.SquadOf(own).First();
        shell.ShowSubPage(new PlayerPageViewModel(shell, new[] { player }, 0));
        Pump();
        var playerPage = Assert.IsType<PlayerPageViewModel>(shell.Page);
        Assert.Equal("Gesamt", playerPage.CareerLines[0].Label);
        Assert.Contains("Karriere", VisibleTexts(w));
    }
}
