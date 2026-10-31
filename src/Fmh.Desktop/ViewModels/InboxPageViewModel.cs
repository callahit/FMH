using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed partial class MessageItem : ObservableObject
{
    public required Message Message { get; init; }
    private static Fmh.Core.Data.Texts T => Fmh.Desktop.Services.AppState.Current.T;
    public string Subject => Message.SubjectText(T);
    public string Sender => Message.SenderText(T);
    public string Date => Fmt.ShortDate(Message.Date);
    public string FullDate => Fmt.Date(Message.Date);
    public string Body => Message.BodyText(T);
    public IBrush TypeBrush { get; init; } = Brushes.Gray;
    public FontWeight Weight => IsRead ? FontWeight.Normal : FontWeight.Bold;
    public bool Open => Message.Required && !Message.Resolved;
    public void Update() => OnPropertyChanged(nameof(Open));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Weight))]
    private bool _isRead;
}

/// <summary>Knopf einer Entscheidung im Posteingang (Ereignis, Saisonziel, Sponsor, Jobangebot).</summary>
public sealed record DecisionOption(string Label, string Detail, IRelayCommand Command, bool Primary = false);

/// <summary>Frage einer Pressekonferenz mit den vier Antworttönen.</summary>
public sealed partial class PressQuestionItem : ObservableObject
{
    public required string Text { get; init; }
    public required IReadOnlyList<string> Answers { get; init; }
    [ObservableProperty] private int _answer = (int)PressAnswer.Modest;
}

public sealed partial class InboxPageViewModel : PageViewModel, IRefreshable
{
    public InboxPageViewModel(ShellViewModel shell) : base(shell) => Refresh();

    public override string Title => T["menu.inbox"];

    [ObservableProperty] private IReadOnlyList<MessageItem> _messages = Array.Empty<MessageItem>();
    [ObservableProperty] private MessageItem? _selected;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _isOffer;
    [ObservableProperty] private bool _canOpenPlayer;
    [ObservableProperty] private decimal _counterFee;
    [ObservableProperty] private string? _actionResult;
    [ObservableProperty] private IReadOnlyList<DecisionOption> _decisions = Array.Empty<DecisionOption>();
    [ObservableProperty] private string? _decisionTitle;
    [ObservableProperty] private IReadOnlyList<PressQuestionItem> _pressQuestions = Array.Empty<PressQuestionItem>();

    public bool HasDecisions => Decisions.Count > 0;
    public bool IsPress => PressQuestions.Count > 0;
    partial void OnDecisionsChanged(IReadOnlyList<DecisionOption> value) => OnPropertyChanged(nameof(HasDecisions));
    partial void OnPressQuestionsChanged(IReadOnlyList<PressQuestionItem> value) => OnPropertyChanged(nameof(IsPress));

    private Negotiation? Offer => Selected is { Message: { Action: MessageAction.IncomingOffer } m }
        ? Session.World.Negotiations.FirstOrDefault(n => n.Id == m.ActionRefId) : null;

    public void Refresh()
    {
        Messages = Session.Inbox.Select(m => new MessageItem { Message = m, IsRead = m.Read, TypeBrush = TypeBrush(m.Type) }).ToList();
        // Offene Pflicht-Entscheidungen zuerst anzeigen.
        Selected = Messages.FirstOrDefault(m => m.Open) ?? Messages.FirstOrDefault();
        UpdateCount();
    }

    private static IBrush TypeBrush(MessageType t) => new SolidColorBrush(t switch
    {
        MessageType.Medical => P.Bad,
        MessageType.Discipline => P.Warn,
        MessageType.Board => P.Gold,
        MessageType.Season => P.Accent,
        MessageType.Transfer or MessageType.Contract => P.Good,
        MessageType.Cup => P.Gold,
        MessageType.Scouting or MessageType.Youth => P.Accent,
        _ => P.Muted,
    });

    partial void OnSelectedChanged(MessageItem? value)
    {
        if (value is null) return;
        value.Message.Read = true;
        value.IsRead = true;
        ActionResult = null;
        IsOffer = Offer is { Stage: NegotiationStage.Pending };
        if (Offer is { } n) CounterFee = MarketValue.Round(n.Offer.Fee * 1.25);
        CanOpenPlayer = value.Message.Action is MessageAction.ScoutReport or MessageAction.IncomingOffer && PlayerOfMessage() is not null;
        BuildDecisions(value.Message);
        UpdateCount();
        Shell.RefreshHeader();
    }

    private Player? PlayerOfMessage() => Selected?.Message switch
    {
        { Action: MessageAction.ScoutReport, ActionRefId: { } id } => Session.World.Players.GetValueOrDefault(id),
        { Action: MessageAction.IncomingOffer } when Offer is { } n => Session.World.Players[n.PlayerId],
        _ => null,
    };

    [RelayCommand]
    private void AcceptOffer() => Answer(ResponseKind.Accept);

    [RelayCommand]
    private void RejectOffer() => Answer(ResponseKind.Reject);

    [RelayCommand]
    private void CounterOffer() => Answer(ResponseKind.Counter);

    private void Answer(ResponseKind kind)
    {
        if (Offer is not { } n) return;
        ActionResult = T.Render(Session.RespondIncoming(n, kind, (long)CounterFee));
        IsOffer = false;
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void OpenPlayer()
    {
        if (PlayerOfMessage() is not { } p) return;
        Shell.ShowSubPage(new PlayerPageViewModel(Shell, new[] { p }, 0));
    }

    /// <summary>Entscheidungsknöpfe für die Phase-3-Nachrichten.</summary>
    private void BuildDecisions(Message m)
    {
        var options = new List<DecisionOption>();
        var questions = new List<PressQuestionItem>();
        string? title = null;
        var s = Session;
        var club = s.HumanClub;
        var currency = UiText.Currency(club);
        void Done(string? result)
        {
            ActionResult = result;
            Decisions = Array.Empty<DecisionOption>();
            PressQuestions = Array.Empty<PressQuestionItem>();
            DecisionTitle = null;
            Selected?.Update();
            Shell.RefreshHeader();
        }
        switch (m.Action)
        {
            case MessageAction.Event when !m.Resolved && s.EventOf(m) is { Chosen: null } e
                                          && Data.Events.FirstOrDefault(d => d.Id == e.EventId) is { } def:
                title = T["inbox.eventDecision"];
                var args = e.Args.Cast<object?>().ToArray();
                foreach (var o in def.Options)
                {
                    var id = o.Id;
                    var risk = o.FailEffects is null ? "" : T.Format("inbox.eventRisk", (int)Math.Round(o.Chance * 100));
                    options.Add(new DecisionOption(T.Render(Fmh.Core.Data.TextRef.Of($"event.{def.Id}.opt.{id}", args)), risk,
                        new RelayCommand(() =>
                        {
                            s.ResolveEvent(e, id);
                            var result = s.Inbox.FirstOrDefault();
                            Done(result is null ? null : result.BodyText(T));
                        })));
                }
                break;
            case MessageAction.SeasonGoal when !m.Resolved:
                title = T["inbox.goalDecision"];
                var expected = s.ExpectedGoal(club);
                foreach (var goal in s.GoalChoices(club))
                {
                    var g = goal;
                    options.Add(new DecisionOption(T["goal." + g], T.Format("inbox.goalPosition", s.GoalPosition(club, g)),
                        new RelayCommand(() =>
                        {
                            s.ChooseSeasonGoal(g);
                            Done(T.Format("inbox.goalChosen", T["goal." + g], Fmt.Money(club.Budget.TransferBudget, currency)));
                        }), g == expected));
                }
                break;
            case MessageAction.SponsorOffer when club.SponsorOffers.FirstOrDefault(o => o.Id == m.ActionRefId) is { } offer:
                title = T["inbox.sponsorDecision"];
                var current = s.DealFor(club, offer.Slot, offer.Index);
                var detail = current is null ? T["inbox.slotFree"] : T.Format("inbox.slotTaken", current.Company, Fmt.Money(current.PerSeason, currency));
                if (offer.RelegationPercent > 0 || offer.PromotionPercent > 0)
                    detail += " · " + T.Format("inbox.sponsorClause", offer.RelegationPercent, offer.PromotionPercent);
                options.Add(new DecisionOption(T["inbox.accept"], detail, new RelayCommand(() =>
                {
                    var error = s.AcceptSponsor(offer);
                    Done(error is null ? T.Format("inbox.sponsorAccepted", offer.Company) : T[error]);
                }), true));
                options.Add(new DecisionOption(T["inbox.reject"], "", new RelayCommand(() =>
                {
                    s.RejectSponsor(offer);
                    Done(T["inbox.sponsorRejected"]);
                })));
                break;
            case MessageAction.JobOffer when s.HumanManager.Career.Offers.FirstOrDefault(o => o.ClubId == m.ActionRefId) is { } job:
                title = T["inbox.jobDecision"];
                var target = s.World.Clubs[job.ClubId];
                options.Add(new DecisionOption(T["inbox.acceptJob"], T.Format("inbox.jobDetail", UiText.Competition(target.LeagueId), target.Reputation, job.Years),
                    new RelayCommand(() =>
                    {
                        var error = s.AcceptJob(job);
                        Done(error is null ? T.Format("inbox.jobAccepted", target.Name) : T[error]);
                        if (error is null) Shell.Rebuild();
                    }), true));
                options.Add(new DecisionOption(T["inbox.declineJob"], "", new RelayCommand(() =>
                {
                    s.DeclineJob(job);
                    Done(T["inbox.jobDeclined"]);
                })));
                break;
            case MessageAction.PressConference when !m.Resolved && s.World.PendingPress is { } conf:
                title = T["inbox.pressDecision"];
                var answers = Enum.GetValues<PressAnswer>().Select(a => T["press.answer." + a]).ToList();
                questions.AddRange(conf.Questions.Select(q => new PressQuestionItem
                {
                    Text = T.Render(Fmh.Core.Data.TextRef.Of("press.q." + q.Key, q.Args.Cast<object?>().ToArray())),
                    Answers = answers,
                }));
                break;
            case MessageAction.Preemption when !m.Resolved
                && s.World.PreemptionOffers.FirstOrDefault(o => o.NegotiationId == m.ActionRefId) is { } preempt:
                title = T["inbox.preemptionDecision"];
                var preemptFee = Fmt.Money(preempt.Fee, currency);
                options.Add(new DecisionOption(T["inbox.preemptionMatch"], T.Format("inbox.preemptionMatchDetail", preemptFee),
                    new RelayCommand(() =>
                    {
                        var error = s.ResolvePreemption(preempt, match: true);
                        Done(error is null ? T.Format("inbox.preemptionMatched", preemptFee) : T[error]);
                    }), true));
                options.Add(new DecisionOption(T["inbox.preemptionPass"], "", new RelayCommand(() =>
                {
                    s.ResolvePreemption(preempt, match: false);
                    Done(T["inbox.preemptionPassed"]);
                })));
                break;
        }
        DecisionTitle = title;
        Decisions = options;
        PressQuestions = questions;
    }

    [RelayCommand]
    private void AnswerPress()
    {
        if (PressQuestions.Count == 0) return;
        Session.AnswerPress(PressQuestions.Select(q => (PressAnswer)q.Answer).ToList());
        ActionResult = T["inbox.pressAnswered"];
        PressQuestions = Array.Empty<PressQuestionItem>();
        DecisionTitle = null;
        Selected?.Update();
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void DelegatePress()
    {
        if (Session.World.PendingPress is not { } conf) return;
        var officer = Session.StaffAbility(Session.HumanClub, StaffRole.PressOfficer);
        Session.AnswerPress(conf.Questions.Select(_ => officer >= 10 ? PressAnswer.Modest : PressAnswer.Evasive).ToList(), delegated: true);
        ActionResult = T["inbox.pressDelegated"];
        PressQuestions = Array.Empty<PressQuestionItem>();
        DecisionTitle = null;
        Selected?.Update();
        Shell.RefreshHeader();
    }

    private void UpdateCount() => CountText = T.Format("inbox.count", Messages.Count(m => !m.IsRead), Messages.Count);
}
