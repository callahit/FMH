using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core;
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

    public void Refresh()
    {
        Messages = Session.Inbox.Select(m => new MessageItem { Message = m, IsRead = m.Read, TypeBrush = TypeBrush(m.Type) }).ToList();
        Selected = Messages.FirstOrDefault();
        UpdateCount();
    }

    private static IBrush TypeBrush(MessageType t) => new SolidColorBrush(t switch
    {
        MessageType.Medical => P.Bad,
        MessageType.Discipline => P.Warn,
        MessageType.Board => P.Gold,
        MessageType.Season => P.Accent,
        _ => P.Muted,
    });

    partial void OnSelectedChanged(MessageItem? value)
    {
        if (value is null) return;
        value.Message.Read = true;
        value.IsRead = true;
        UpdateCount();
        Shell.RefreshHeader();
    }

    private void UpdateCount() => CountText = T.Format("inbox.count", Messages.Count(m => !m.IsRead), Messages.Count);
}
