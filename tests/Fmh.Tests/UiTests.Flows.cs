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

namespace Fmh.Tests;

/// <summary>Oberflächentests für Abläufe: Transfers, Training und Taktik, Angebote im Posteingang, Sprache und Tastatur.</summary>
public partial class UiTests
{
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

        // Spielerprofil eines fremden Spielers aus der eigenen Liga: Attribute als Spanne, Kaufen öffnet den Verhandlungsdialog.
        page.League = page.Leagues.First(o => Equals(o.Value, session.HumanClub.LeagueId));
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
        // Anfrage (E2-S2) für den UI-Test überspringen und Dialog neu öffnen.
        var open = session.OpenNegotiationFor(row.Player)!;
        if (open.Stage == NegotiationStage.Enquiry)
        {
            open.Stage = NegotiationStage.Talks;
            open.EnquiryDue = null;
            open.Response = Fmh.Core.Data.TextRef.Of("neg.enquiry.accepted");
            neg.CloseCommand.Execute(null);
            buy.Command.Execute(null);
            neg = Assert.IsType<NegotiationViewModel>(vm.Dialog);
        }
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
        for (var i = 0; i < 300 && !HasOffer(); i++)
        {
            if (session.PendingDecisions.Count > 0) session.DelegateDecisions();
            if (session.PendingHumanFixture is { } f) session.PlayInstant(f);
            shell.Continue();
        }
        Assert.True(HasOffer());
        var inbox = Assert.IsType<InboxPageViewModel>(shell.Page);
        // Liegen weitere Entscheidungen an, wählt der Spieler das Angebot selbst aus.
        inbox.Selected = inbox.Messages.First(m => m.Message.Action == MessageAction.IncomingOffer && m.Open);
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
}
