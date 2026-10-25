using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

/// <summary>Auswahlliste als Dialog. Enter wählt, Esc bricht ab.</summary>
public sealed class ListDialog : View
{
    private readonly string _title;
    private readonly IReadOnlyList<string> _items;
    private readonly Action<int> _onSelect;
    private readonly string? _hint;
    private readonly ListState _list = new();

    public ListDialog(App app, string title, IReadOnlyList<string> items, Action<int> onSelect, int selected = 0, string? hint = null)
        : base(app)
    {
        _title = title;
        _items = items;
        _onSelect = onSelect;
        _hint = hint;
        _list.Index = selected;
    }

    public override bool IsOverlay => true;

    public override void Render(Canvas c)
    {
        var w = Math.Min(c.Width - 4, Math.Max(_title.Length + 6, _items.Select(i => i.Length).DefaultIfEmpty(10).Max() + 4));
        w = Math.Max(w, (_hint?.Length ?? 0) + 4);
        var rows = Math.Min(_items.Count, c.Height - 8);
        var h = rows + (_hint is null ? 2 : 4);
        var x = (c.Width - w) / 2;
        var y = (c.Height - h) / 2;
        c.Box(x, y, w, h, Th.Border, Th.PanelBg, _title);
        var start = _list.VisibleStart(rows, _items.Count);
        for (var i = 0; i < rows && start + i < _items.Count; i++)
        {
            var idx = start + i;
            var sel = idx == _list.Index;
            c.WriteField(x + 1, y + 1 + i, w - 2, " " + _items[idx], sel ? Th.SelFg : Th.Fg, sel ? Th.SelBg : Th.PanelBg);
        }
        if (_hint is not null) c.Write(x + 2, y + h - 2, _hint, Th.Dim, Th.PanelBg, w - 4);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key, _items.Count)) return;
        if (key.Key == ConsoleKey.Escape) { App.Pop(); return; }
        if (key.Key == ConsoleKey.Enter && _items.Count > 0)
        {
            var idx = _list.Index;
            App.Pop();
            _onSelect(idx);
        }
    }
}

/// <summary>Hinweis- oder Infodialog mit mehreren Zeilen.</summary>
public sealed class MessageDialog : View
{
    private readonly string _title;
    private readonly IReadOnlyList<string> _lines;
    private readonly Action? _onClose;

    public MessageDialog(App app, string title, IReadOnlyList<string> lines, Action? onClose = null) : base(app)
    {
        _title = title;
        _lines = lines;
        _onClose = onClose;
    }

    public override bool IsOverlay => true;

    public override void Render(Canvas c)
    {
        var footer = T["ui.pressEnter"];
        var w = Math.Min(c.Width - 4, Math.Max(Math.Max(_title.Length, footer.Length) + 6, _lines.Select(l => l.Length).DefaultIfEmpty(10).Max() + 4));
        var rows = Math.Min(_lines.Count, c.Height - 8);
        var h = rows + 4;
        var x = (c.Width - w) / 2;
        var y = (c.Height - h) / 2;
        c.Box(x, y, w, h, Th.Border, Th.PanelBg, _title);
        for (var i = 0; i < rows; i++) c.Write(x + 2, y + 1 + i, _lines[i], Th.Fg, Th.PanelBg, w - 4);
        c.Write(x + 2, y + h - 2, footer, Th.Dim, Th.PanelBg, w - 4);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape or ConsoleKey.Spacebar)
        {
            App.Pop();
            _onClose?.Invoke();
        }
    }
}

/// <summary>Einzeiliges Texteingabefeld.</summary>
public sealed class TextField
{
    public TextField(string value = "", int maxLength = 30)
    {
        Value = value;
        MaxLength = maxLength;
    }

    public string Value { get; private set; }
    public int MaxLength { get; }

    public bool Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (Value.Length > 0) Value = Value[..^1];
            return true;
        }
        if (!char.IsControl(key.KeyChar) && Value.Length < MaxLength && (key.Modifiers & ConsoleModifiers.Control) == 0)
        {
            Value += key.KeyChar;
            return true;
        }
        return false;
    }
}
