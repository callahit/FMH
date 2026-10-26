using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Core.Game;
using Fmh.Core.Model;
using Fmh.Desktop;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;
using Fmh.Desktop.Views;

[assembly: AvaloniaTestApplication(typeof(Fmh.Tests.TestAppBuilder))]

namespace Fmh.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        AppState.Initialise(TestData.Data, Path.Combine(Path.GetTempPath(), "fmh-ui-" + Guid.NewGuid()));
        MatchViewModel.UseTimer = false;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}

/// <summary>
/// Oberflächentests ohne Bildschirm (Avalonia Headless). Mit FMH_SHOTS=&lt;ordner&gt; werden Bildschirmfotos gespeichert.
/// </summary>
public class UiTests
{
    private static (Window window, MainWindowViewModel vm) Open()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump();
        return (window, vm);
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
        var dir = Environment.GetEnvironmentVariable("FMH_SHOTS");
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    private static IEnumerable<string> VisibleTexts(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    private static void AssertNoMissingTexts(Window w)
    {
        var missing = VisibleTexts(w).Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\[[a-z]+\.[A-Za-z.0-9]+\]$")).ToList();
        Assert.True(missing.Count == 0, "Fehlende Texte: " + string.Join(", ", missing));
        Assert.DoesNotContain(VisibleTexts(w), t => t.StartsWith("View fehlt"));
    }

    private static ShellViewModel StartGame(MainWindowViewModel vm)
    {
        var start = Assert.IsType<StartViewModel>(vm.Screen);
        start.NewGameCommand.Execute(null);
        var ng = Assert.IsType<NewGameViewModel>(vm.Screen);
        ng.Seed = "4711";
        ng.ManagerName = "Test";
        ng.StartCommand.Execute(null);
        return Assert.IsType<ShellViewModel>(vm.Screen);
    }

    [AvaloniaFact]
    public void StartAndNewGameScreensRender()
    {
        var (w, vm) = Open();
        Assert.Contains(VisibleTexts(w), t => t == "Neues Spiel");
        AssertNoMissingTexts(w);
        Shot(w, "01_start");
        ((StartViewModel)vm.Screen).NewGameCommand.Execute(null);
        Pump();
        Assert.Contains(VisibleTexts(w), t => t == "SV Nordheide");
        AssertNoMissingTexts(w);
        Shot(w, "02_newgame");
    }

    [AvaloniaFact]
    public void AllPagesRender()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var names = new[] { "inbox", "squad", "tactics", "training", "fixtures", "transfers", "finances", "stadium", "club", "league" };
        for (var i = 0; i < shell.Nav.Count; i++)
        {
            shell.Open(shell.Nav[i]);
            Pump();
            AssertNoMissingTexts(w);
            Shot(w, $"1{i}_{names[i]}");
        }
        shell.Open(shell.Nav[1]);
        var squad = Assert.IsType<SquadPageViewModel>(shell.Page);
        squad.OpenPlayer(squad.Rows[0]);
        Pump();
        Assert.IsType<PlayerPageViewModel>(shell.Page);
        AssertNoMissingTexts(w);
        Shot(w, "20_player");
        shell.Back();
        Assert.IsType<SquadPageViewModel>(shell.Page);
    }

    [AvaloniaFact]
    public void KeyboardShortcutsSwitchPages()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        w.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        Pump();
        Assert.IsType<TacticsPageViewModel>(shell.Page);
        w.KeyPress(Key.F10, RawInputModifiers.None, PhysicalKey.F10, null);
        Pump();
        Assert.IsType<LeaguePageViewModel>(shell.Page);
    }

    [AvaloniaFact]
    public void TacticsClickToSwapAndSettings()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        shell.Open(shell.Nav[2]);
        var page = Assert.IsType<TacticsPageViewModel>(shell.Page);
        var club = AppState.Current.Session!.HumanClub;
        var before = club.Tactic.Lineup.ToList();

        // Zwei Plätze nacheinander anklicken tauscht die Spieler.
        page.SelectSlot(page.Slots[1]);
        page.SelectSlot(page.Slots[2]);
        Assert.Equal(before[1], club.Tactic.Lineup[2]);
        Assert.Equal(before[2], club.Tactic.Lineup[1]);

        // Platz wählen, dann Reservespieler anklicken: Spieler kommt in die Startelf.
        var reserve = page.Reserves[0].Player;
        page.SelectSlot(page.Slots[5]);
        page.ClickPlayer(page.Reserves.First(r => r.Player == reserve));
        Assert.Equal(reserve.Id, club.Tactic.Lineup[5]);

        // Drag & Drop Ziel: Spieler auf Platz ziehen.
        var benchPlayer = page.Bench[0].Player;
        page.DropOnSlot(0, benchPlayer.Id);
        Assert.Equal(benchPlayer.Id, club.Tactic.Lineup[0]);

        var formation = page.Settings[0];
        formation.SelectedIndex = (formation.SelectedIndex + 1) % formation.Options.Count;
        Assert.Equal(11, club.Tactic.Lineup.Count(id => id.HasValue));
        Pump();
        Shot(w, "21_tactics_changed");
    }

    /// <summary>"Weiter" bis zum Spieltag; Pflicht-Entscheidungen (z. B. Angebote) übernimmt der Assistent.</summary>
    private static void ContinueToMatchday(ShellViewModel shell, GameSession session)
    {
        for (var i = 0; i < 20 && session.PendingHumanFixture is null; i++)
        {
            if (session.PendingDecisions.Count > 0) session.DelegateDecisions();
            shell.Continue();
        }
    }

    [AvaloniaFact]
    public void PlayMatchdayLive()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        ContinueToMatchday(shell, session);
        var fixture = session.PendingHumanFixture;
        Assert.NotNull(fixture);
        shell.Continue();
        var match = Assert.IsType<MatchViewModel>(vm.Screen);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "30_prematch");

        match.EditTacticsCommand.Execute(null);
        Pump();
        Shot(w, "31_prematch_tactics");
        match.EditTacticsCommand.Execute(null);

        match.Kickoff();
        Pump();
        Shot(w, "32_teamtalk");
        match.Talk(match.Tones[5]);
        Assert.Equal(MatchStage.Live, match.Stage);
        for (var i = 0; i < 40; i++) match.Tick(TimeSpan.FromSeconds(1));
        if (match.Paused) match.TogglePause();
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "33_live");

        for (var i = 0; i < 400 && match.Stage == MatchStage.Live; i++)
        {
            match.Tick(TimeSpan.FromSeconds(1));
            if (match.Paused) match.TogglePause();
        }
        Assert.Equal(MatchStage.HalfTime, match.Stage);
        Pump();
        Shot(w, "34_halftime");
        match.Talk(match.Tones[0]);
        match.SecondHalf();
        match.Substitute();
        var outDialog = Assert.IsType<ChoiceDialogViewModel>(vm.Dialog);
        Pump();
        Shot(w, "35_substitution");
        outDialog.ChooseCommand.Execute(outDialog.Options[3]);
        var inDialog = Assert.IsType<ChoiceDialogViewModel>(vm.Dialog);
        inDialog.ChooseCommand.Execute(inDialog.Options[0]);
        Assert.Null(vm.Dialog);

        for (var i = 0; i < 1000 && match.Stage != MatchStage.PostMatch; i++)
        {
            match.Tick(TimeSpan.FromSeconds(1));
            if (match.Paused) match.TogglePause();
        }
        Assert.Equal(MatchStage.PostMatch, match.Stage);
        Assert.NotNull(fixture!.Result);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "36_postmatch");
        match.Close();
        Assert.IsType<ShellViewModel>(vm.Screen);
        Pump();
        Shot(w, "37_after_match");
    }

    [AvaloniaFact]
    public void InstantMatchSaveAndLoad()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        session.HumanManager.Profile.MatchDisplay = MatchDisplayMode.Instant;
        ContinueToMatchday(shell, AppState.Current.Session!);
        shell.Continue();
        var match = Assert.IsType<MatchViewModel>(vm.Screen);
        match.Kickoff();
        Assert.Equal(MatchStage.PostMatch, match.Stage);
        match.Close();

        shell.Save();
        Assert.Contains("Gespeichert", shell.StatusMessage);
        var date = session.World.Date;
        vm.Navigate(new StartViewModel(vm));
        var start = (StartViewModel)vm.Screen;
        Pump();
        Shot(w, "03_start_with_saves");
        var save = start.Saves.First(s => s.Info.Name.StartsWith("nordheide"));
        start.LoadCommand.Execute(save);
        Assert.IsType<ShellViewModel>(vm.Screen);
        Assert.Equal(date, AppState.Current.Session!.World.Date);
    }

    [AvaloniaFact]
    public void ThemesAndOptions()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        shell.OptionsCommand.Execute(null);
        var options = Assert.IsType<OptionsViewModel>(vm.Dialog);
        Pump();
        Shot(w, "40_options");
        var theme = options.Settings.Last();
        theme.SelectedIndex = 1;
        Assert.Equal("Hell", AppState.Current.Session!.HumanManager.Profile.Theme);
        options.Settings[0].SelectedIndex = 2;
        Assert.Equal(WageDisplay.Year, AppState.Current.Session!.HumanManager.Profile.WageDisplay);
        options.CloseCommand.Execute(null);
        shell.Open(shell.Nav[1]);
        Pump();
        Shot(w, "41_light_squad");
        Assert.Contains(VisibleTexts(w), t => t.EndsWith("/Jahr"));
        Palette.Apply("Terminal");
        shell.Open(shell.Nav[9]);
        Pump();
        Shot(w, "42_terminal_league");
        Palette.Apply("Klassik");
    }
}
