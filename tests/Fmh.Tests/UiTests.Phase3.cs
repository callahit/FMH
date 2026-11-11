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

/// <summary>Oberflächentests für Phase 3: Seiten mit der Maus, Entscheidungen im Posteingang und Konferenz.</summary>
public partial class UiTests
{
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
        // Freies Feld anklicken und das erste mögliche Gebäude bauen (Listen-Fallback-Tab).
        w.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 2;
        var free = stadium.Tiles.First(t => t.Building is null && !t.IsStadium && string.IsNullOrEmpty(t.Label));
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

        // Stadionneubau erst nach dem Angebot der Stadt.
        Assert.False(stadium.HasSite);
        var site = session.TriggerRandomEvent("citySite")!;
        session.ResolveEvent(site, "accept");
        stadium.Refresh();
        Assert.True(stadium.HasSite);
        Assert.False(string.IsNullOrEmpty(stadium.DesignImpact));
        Assert.Contains(stadium.Stands[0].Actions, a => a.Impact.Length > 0);
        w.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 0;
        Pump();
        AssertNoMissingTexts(w);
        Shot(w, "59_new_stadium");

        shell.OpenPage<ClubPageViewModel>();
        var club = Assert.IsType<ClubPageViewModel>(shell.Page);
        ShowTabs(w, "54_club");
        Assert.Equal(3, club.YouthGroups.Count);
        Assert.Equal(4, club.AcademyCriteria.Count);
        club.AcademyBudget = 2_000_000;
        club.SaveAcademyBudgetCommand.Execute(null);
        Assert.Equal(2_000_000, session.HumanClub.Academy.Budget);
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
}
