using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Desktop.ViewModels;

public sealed partial class MessageItem : ObservableObject
{
    public required Message Message { get; init; }
    public string Subject => Message.Subject;
    public string Sender => Message.Sender;
    public string Date => Fmt.ShortDate(Message.Date);
    public string FullDate => Fmt.Date(Message.Date);
    public string Body => Message.Body;
    public IBrush TypeBrush { get; init; } = Brushes.Gray;
    public FontWeight Weight => IsRead ? FontWeight.Normal : FontWeight.Bold;
    public bool Open => Message.Required && !Message.Resolved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Weight))]
    private bool _isRead;
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
        ActionResult = Session.RespondIncoming(n, kind, (long)CounterFee);
        IsOffer = false;
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void OpenPlayer()
    {
        if (PlayerOfMessage() is not { } p) return;
        Shell.ShowSubPage(new PlayerPageViewModel(Shell, new[] { p }, 0));
    }

    private void UpdateCount() => CountText = T.Format("inbox.count", Messages.Count(m => !m.IsRead), Messages.Count);
}
