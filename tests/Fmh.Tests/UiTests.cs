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
        // Eigene Datenkopie und Einstellungsdatei: Sprachwechsel in Oberflächentests betreffen andere Tests nicht.
        var dir = Path.Combine(Path.GetTempPath(), "fmh-ui-" + Guid.NewGuid());
        var data = Fmh.Core.Data.PackageLoader.Load(Fmh.Core.Data.PackageLoader.FindDataRoot());
        AppState.Initialise(data, Path.Combine(dir, "saves"), "de", Path.Combine(dir, "settings.json"));
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
[Collection("Sprache")]
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

    /// <summary>Alle Reiter einer Seite nacheinander zeigen und prüfen.</summary>
    private static void ShowTabs(Window w, string prefix)
    {
        Pump();
        var tabs = w.GetVisualDescendants().OfType<TabControl>().First();
        for (var t = 0; t < tabs.ItemCount; t++)
        {
            tabs.SelectedIndex = t;
            Pump();
            AssertNoMissingTexts(w);
            Shot(w, $"{prefix}_{t}");
        }
    }

    [AvaloniaFact]
    public void Phase3PagesWorkWithTheMouse()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        session.HumanClub.Budget.Balance += 20_000_000;

        shell.OpenPage<FinancesPageViewModel>();
        var fin = Assert.IsType<FinancesPageViewModel>(shell.Page);
        ShowTabs(w, "50_finances");
        fin.LoanAmount = 1_000_000;
        fin.TakeLoanCommand.Execute(null);
        Assert.Single(session.HumanClub.Finance.Loans);
        Assert.Single(fin.Loans);
        fin.Standing = 12;
        fin.SavePricesCommand.Execute(null);
        Assert.Equal(12, session.HumanClub.Finance.Prices.Standing);
        Assert.Contains("Konto", shell.EconomyText);

        shell.OpenPage<StadiumPageViewModel>();
        var stadium = Assert.IsType<StadiumPageViewModel>(shell.Page);
        ShowTabs(w, "51_stadium");
        w.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 0;
        Pump();
        stadium.Stands[0].Actions[0].Command.Execute(null);
        Assert.Single(session.HumanClub.Grounds.Projects);
        // Freies Feld anklicken und das erste mögliche Gebäude bauen.
        w.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
        var free = stadium.Tiles.First(t => t.Building is null && !t.IsStadium);
        free.Select.Execute(null);
        Assert.True(stadium.IsFreeCell);
        var option = stadium.BuildOptions.First(o => o.Possible);
        Pump();
        Shot(w, "52_build_menu");
        option.Build.Execute(null);
        Assert.Equal(2, session.HumanClub.Grounds.Projects.Count);
        var building = stadium.Tiles.First(t => t.Building is not null);
        building.Select.Execute(null);
        Assert.True(stadium.HasBuilding);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "53_building");

        shell.OpenPage<ClubPageViewModel>();
        var club = Assert.IsType<ClubPageViewModel>(shell.Page);
        ShowTabs(w, "54_club");
        var staff = club.OwnStaff.Count;
        club.OwnStaff[0].Action.Execute(null);
        Assert.Equal(staff - 1, club.OwnStaff.Count);
        club.Requests[2].Command.Execute(null);
        Assert.False(string.IsNullOrEmpty(club.Result));
    }

    [AvaloniaFact]
    public void Phase3DecisionsInTheInbox()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        shell.OpenPage<InboxPageViewModel>();
        var inbox = Assert.IsType<InboxPageViewModel>(shell.Page);

        // Saisonziel
        inbox.Selected = inbox.Messages.First(m => m.Message.Action == MessageAction.SeasonGoal);
        Assert.True(inbox.HasDecisions);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "55_season_goal");
        inbox.Decisions[0].Command.Execute(null);
        Assert.DoesNotContain(session.PendingDecisions, m => m.Action == MessageAction.SeasonGoal);

        // Zufallsereignis mit Auswahl
        var e = session.TriggerRandomEvent("trainingFight")!;
        inbox.Refresh();
        inbox.Selected = inbox.Messages.First(m => m.Message.Action == MessageAction.Event && m.Message.ActionRefId == e.Id);
        Assert.Equal(3, inbox.Decisions.Count);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "56_event");
        inbox.Decisions[1].Command.Execute(null);
        Assert.NotNull(e.Chosen);

        // Pressekonferenz
        session.SchedulePress(session.NextFixture(session.HumanClub)!, after: false);
        inbox.Refresh();
        inbox.Selected = inbox.Messages.First(m => m.Message.Action == MessageAction.PressConference && m.Open);
        Assert.True(inbox.IsPress);
        inbox.PressQuestions[0].Answer = (int)PressAnswer.Confident;
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "57_press");
        inbox.AnswerPressCommand.Execute(null);
        Assert.Null(session.World.PendingPress);

        // Sponsorenangebot
        if (session.HumanClub.SponsorOffers.FirstOrDefault() is { } offer)
        {
            inbox.Refresh();
            inbox.Selected = inbox.Messages.First(m => m.Message.Action == MessageAction.SponsorOffer && m.Message.ActionRefId == offer.Id);
            inbox.Decisions[0].Command.Execute(null);
            Assert.Contains(offer, session.HumanClub.Sponsors);
        }
    }

    [AvaloniaFact]
    public void ConferenceShowsOtherMatchesAndLiveTable()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        session.HumanManager.Profile.MatchDisplay = MatchDisplayMode.Conference;
        // Bis zum ersten Ligaspiel (vorher kommt der Pokal).
        for (var i = 0; i < 40; i++)
        {
            ContinueToMatchday(shell, session);
            if (session.PendingHumanFixture is { Kind: CompetitionKind.League }) break;
            session.PlayInstant(session.PendingHumanFixture!);
        }
        Assert.Equal(CompetitionKind.League, session.PendingHumanFixture!.Kind);
        shell.Continue();
        var match = Assert.IsType<MatchViewModel>(vm.Screen);
        match.Kickoff();
        match.Talk(match.Tones[0]);
        for (var i = 0; i < 200 && match.Stage == MatchStage.Live; i++)
        {
            match.Tick(TimeSpan.FromSeconds(1));
            if (match.Paused) match.TogglePause();
        }
        Assert.True(match.IsConference);
        Assert.NotEmpty(match.ConferenceGames);
        Assert.Equal(session.World.LeagueOf(session.HumanClub).ClubIds.Count, match.LiveTable.Count);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "58_conference");
    }

    [AvaloniaFact]
    public void TransferFlowThroughTheUi()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        shell.OpenPage<TransfersPageViewModel>();
        var page = Assert.IsType<TransfersPageViewModel>(shell.Page);
        Assert.NotEmpty(page.Results);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "30_transfers");

        // Spielerprofil eines fremden Spielers: Attribute als Spanne, Kaufen öffnet den Verhandlungsdialog.
        var row = page.Results.First(r => r.Player.ClubId is { } c && session.World.FindLeague(session.World.Clubs[c].LeagueId) is not null
                                           && Fmh.Core.Logic.Negotiations.RefusalReason(r.Player, session.HumanClub, session.World, session.Data) is null
                                           && Fmh.Core.Logic.Negotiations.AskingPrice(r.Player, session.World.Clubs[r.Player.ClubId!.Value], session.World, session.Data) < session.HumanClub.Budget.TransferBudget / 2);
        page.OpenPlayer(row);
        var profile = Assert.IsType<PlayerPageViewModel>(shell.Page);
        Assert.False(profile.IsOwn);
        Assert.Contains(profile.Groups.SelectMany(g => g.Items), a => a.Display.Contains('–'));
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "31_player_foreign");
        var buy = profile.Actions.First(a => a.Label == AppState.Current.T["player.action.buy"]);
        buy.Command.Execute(null);
        var neg = Assert.IsType<NegotiationViewModel>(vm.Dialog);
        Assert.True(neg.ClubStage);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "32_negotiation_club");
        neg.Fee = 1_000;
        neg.SubmitCommand.Execute(null);
        Assert.False(string.IsNullOrEmpty(neg.Response));
        // Die Gegenforderung wird ins Feld übernommen; damit ist der Verein einverstanden.
        neg.SubmitCommand.Execute(null);
        Assert.True(neg.PlayerStage, neg.Response);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "33_negotiation_player");
        session.HumanClub.Budget.WageBudget += 1_000_000;
        neg.WeeklyWage *= 2;
        neg.SubmitCommand.Execute(null);
        Assert.True(neg.Finished);
        Assert.Equal(session.HumanClub.Id, row.Player.ClubId);
        neg.CloseCommand.Execute(null);
        Assert.Null(vm.Dialog);

        shell.OpenPage<TransfersPageViewModel>();
        page = Assert.IsType<TransfersPageViewModel>(shell.Page);
        Assert.NotEmpty(page.Scouts);
        page.ScoutSearchCommand.Execute(page.Scouts[0]);
        Assert.True(page.Scouts[0].Busy);
    }

    [AvaloniaFact]
    public void TrainingTacticsCupAndYouthPagesWork()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;

        shell.OpenPage<TrainingPageViewModel>();
        var training = Assert.IsType<TrainingPageViewModel>(shell.Page);
        training.Days[1].Morning.SelectedIndex = (int)TrainingBlock.Finishing;
        Assert.Equal(TrainingBlock.Finishing, session.HumanClub.Training.Sessions[2]);
        training.IntensityIndex = 2;
        Assert.Equal(TrainingIntensity.Hard, session.HumanClub.Training.Intensity);
        training.CampInDays = 60;
        training.BookCampCommand.Execute(null);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "34_training");

        shell.OpenPage<TacticsPageViewModel>();
        var tactics = Assert.IsType<TacticsPageViewModel>(shell.Page);
        var striker = tactics.Slots.Last();
        tactics.SelectSlot(striker);
        Assert.True(tactics.HasSlot);
        Assert.NotNull(tactics.RoleSetting);
        tactics.RoleSetting!.SelectedIndex = tactics.RoleSetting.Options.Count - 1;
        tactics.MoreShots = true;
        Assert.True(session.HumanClub.Tactic.Orders[striker.Index].MoreShots);
        tactics.SaveTacticCommand.Execute(tactics.SavedTactics[0]);
        Assert.Single(session.HumanClub.SavedTactics);
        tactics.SelectSlot(tactics.Slots[striker.Index]);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "35_tactics_roles");

        shell.OpenPage<LeaguePageViewModel>();
        var league = Assert.IsType<LeaguePageViewModel>(shell.Page);
        league.SelectLeagueCommand.Execute(league.Leagues.First(l => l.IsCup));
        Assert.True(league.IsCup);
        Assert.NotEmpty(league.CupRounds[0].Ties);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "36_cup");

        shell.OpenPage<SquadPageViewModel>();
        var squad = Assert.IsType<SquadPageViewModel>(shell.Page);
        squad.ShowYouth = true;
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "37_youth");
    }

    [AvaloniaFact]
    public void IncomingOfferCanBeAnsweredInTheInbox()
    {
        var (w, vm) = Open();
        var shell = StartGame(vm);
        var session = AppState.Current.Session!;
        bool HasOffer() => session.PendingDecisions.Any(m => m.Action == MessageAction.IncomingOffer);
        for (var i = 0; i < 150 && !HasOffer(); i++)
        {
            if (session.PendingDecisions.Count > 0) session.DelegateDecisions();
            if (session.PendingHumanFixture is { } f) session.PlayInstant(f);
            shell.Continue();
        }
        Assert.NotEmpty(session.PendingDecisions);
        var inbox = Assert.IsType<InboxPageViewModel>(shell.Page);
        Assert.True(inbox.IsOffer);
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "38_inbox_offer");
        inbox.RejectOfferCommand.Execute(null);
        Assert.False(HasOffer());
        Assert.False(string.IsNullOrEmpty(inbox.ActionResult));
    }

    [AvaloniaFact]
    public void LanguageCanBeSwitchedAtStartAndInGame()
    {
        var (w, vm) = Open();
        try
        {
            var start = Assert.IsType<StartViewModel>(vm.Screen);
            Assert.Contains("English", start.Languages);
            start.LanguageIndex = start.Languages.ToList().IndexOf("English");
            Pump();
            Assert.Equal("en", AppState.Current.Language);
            Assert.Contains(VisibleTexts(w), t => t == "New game");
            AssertNoMissingTexts(w);
            Shot(w, "40_start_en");
            // Die Wahl wird gespeichert und gilt beim nächsten Start.
            Assert.Equal("en", Fmh.Desktop.Services.AppSettings.Load(AppState.Current.SettingsFile).Language);

            var shell = StartGame(vm);
            Pump();
            Assert.Contains(VisibleTexts(w), t => t == "Inbox");
            var welcome = AppState.Current.Session!.Inbox.Last();
            Assert.StartsWith("Welcome to", welcome.SubjectText(AppState.Current.T));
            shell.OpenPage<TransfersPageViewModel>();
            Pump();
            AssertNoMissingTexts(w);
            Shot(w, "41_transfers_en");

            // Wechsel im laufenden Spiel über die Einstellungen: dieselbe Seite, jetzt auf Deutsch, auch ältere Nachrichten.
            shell.OptionsCommand.Execute(null);
            var options = Assert.IsType<OptionsViewModel>(vm.Dialog);
            var lang = options.Settings.First(x => x.Label == "Language");
            lang.SelectedIndex = lang.Options.ToList().IndexOf("Deutsch");
            Pump();
            Assert.Equal("de", AppState.Current.Language);
            var shell2 = Assert.IsType<ShellViewModel>(vm.Screen);
            Assert.IsType<TransfersPageViewModel>(shell2.Page);
            Assert.StartsWith("Willkommen bei", welcome.SubjectText(AppState.Current.T));
            Assert.Contains(VisibleTexts(w), t => t == "Posteingang");
            AssertNoMissingTexts(w);
        }
        finally
        {
            AppState.Current.SetLanguage("de");
        }
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

        for (var i = 0; i < 2000 && match.Stage != MatchStage.PostMatch; i++)
        {
            // Pokalspiel: bei Gleichstand Pause vor der Verlängerung.
            if (match.Stage == MatchStage.HalfTime) match.SecondHalf();
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
