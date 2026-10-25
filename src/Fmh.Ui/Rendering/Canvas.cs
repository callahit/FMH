using System.Text;

namespace Fmh.Ui.Rendering;

/// <summary>Zeichenpuffer für eine Bildschirmseite. Wird als ganzes Bild ausgegeben, damit nichts flackert.</summary>
public sealed class Canvas
{
    private readonly char[] _chars;
    private readonly ConsoleColor[] _fg;
    private readonly ConsoleColor[] _bg;

    public Canvas(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _chars = new char[Width * Height];
        _fg = new ConsoleColor[Width * Height];
        _bg = new ConsoleColor[Width * Height];
        Clear(ConsoleColor.Gray, ConsoleColor.Black);
    }

    public int Width { get; }
    public int Height { get; }

    public void Clear(ConsoleColor fg, ConsoleColor bg)
    {
        Array.Fill(_chars, ' ');
        Array.Fill(_fg, fg);
        Array.Fill(_bg, bg);
    }

    public char CharAt(int x, int y) => _chars[y * Width + x];
    public ConsoleColor FgAt(int x, int y) => _fg[y * Width + x];
    public ConsoleColor BgAt(int x, int y) => _bg[y * Width + x];

    public void Set(int x, int y, char ch, ConsoleColor fg, ConsoleColor? bg = null)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = y * Width + x;
        _chars[i] = ch;
        _fg[i] = fg;
        if (bg is { } b) _bg[i] = b;
    }

    /// <summary>Schreibt Text ab (x, y); abgeschnitten nach maxWidth Zeichen. Gibt die Breite zurück.</summary>
    public int Write(int x, int y, string text, ConsoleColor fg, ConsoleColor? bg = null, int maxWidth = int.MaxValue)
    {
        var n = 0;
        foreach (var ch in text)
        {
            if (n >= maxWidth) break;
            Set(x + n, y, ch == '\n' ? ' ' : ch, fg, bg);
            n++;
        }
        return n;
    }

    /// <summary>Text in einem Feld fester Breite, links-, rechtsbündig oder zentriert.</summary>
    public void WriteField(int x, int y, int width, string text, ConsoleColor fg, ConsoleColor? bg = null, Align align = Align.Left)
    {
        if (width <= 0) return;
        if (text.Length > width) text = width > 1 ? text[..(width - 1)] + "…" : text[..width];
        var pad = width - text.Length;
        var left = align switch { Align.Right => pad, Align.Center => pad / 2, _ => 0 };
        Fill(x, y, width, 1, ' ', fg, bg);
        Write(x + left, y, text, fg, bg);
    }

    public void Fill(int x, int y, int w, int h, char ch, ConsoleColor fg, ConsoleColor? bg)
    {
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                Set(xx, yy, ch, fg, bg);
    }

    public void FillBg(int x, int y, int w, int h, ConsoleColor bg)
    {
        for (var yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (var xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                _bg[yy * Width + xx] = bg;
    }

    public void Box(int x, int y, int w, int h, ConsoleColor fg, ConsoleColor? bg = null, string? title = null)
    {
        if (w < 2 || h < 2) return;
        if (bg is { } b) Fill(x, y, w, h, ' ', fg, b);
        Set(x, y, '┌', fg, bg); Set(x + w - 1, y, '┐', fg, bg);
        Set(x, y + h - 1, '└', fg, bg); Set(x + w - 1, y + h - 1, '┘', fg, bg);
        for (var i = 1; i < w - 1; i++) { Set(x + i, y, '─', fg, bg); Set(x + i, y + h - 1, '─', fg, bg); }
        for (var i = 1; i < h - 1; i++) { Set(x, y + i, '│', fg, bg); Set(x + w - 1, y + i, '│', fg, bg); }
        if (!string.IsNullOrEmpty(title)) Write(x + 2, y, $" {title} ", fg, bg, w - 4);
    }

    public void HLine(int x, int y, int w, ConsoleColor fg, ConsoleColor? bg = null, char ch = '─')
    {
        for (var i = 0; i < w; i++) Set(x + i, y, ch, fg, bg);
    }

    /// <summary>Komplettes Bild als ANSI-Text (16 Farben), Cursor oben links.</summary>
    public string ToAnsi()
    {
        var sb = new StringBuilder(Width * Height * 2);
        sb.Append("\u001b[H");
        ConsoleColor? fg = null, bg = null;
        for (var y = 0; y < Height; y++)
        {
            if (y > 0) sb.Append("\u001b[").Append(y + 1).Append(";1H");
            for (var x = 0; x < Width; x++)
            {
                var i = y * Width + x;
                if (_fg[i] != fg || _bg[i] != bg)
                {
                    fg = _fg[i];
                    bg = _bg[i];
                    sb.Append("\u001b[").Append(AnsiFg(fg.Value)).Append(';').Append(AnsiFg(bg.Value) + 10).Append('m');
                }
                sb.Append(_chars[i]);
            }
        }
        sb.Append("\u001b[0m");
        return sb.ToString();
    }

    /// <summary>Reiner Text ohne Farben, z. B. für Tests und Screenreader-Ausgabe.</summary>
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        for (var y = 0; y < Height; y++)
        {
            sb.Append(_chars, y * Width, Width);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static int AnsiFg(ConsoleColor c) => c switch
    {
        ConsoleColor.Black => 30,
        ConsoleColor.DarkRed => 31,
        ConsoleColor.DarkGreen => 32,
        ConsoleColor.DarkYellow => 33,
        ConsoleColor.DarkBlue => 34,
        ConsoleColor.DarkMagenta => 35,
        ConsoleColor.DarkCyan => 36,
        ConsoleColor.Gray => 37,
        ConsoleColor.DarkGray => 90,
        ConsoleColor.Red => 91,
        ConsoleColor.Green => 92,
        ConsoleColor.Yellow => 93,
        ConsoleColor.Blue => 94,
        ConsoleColor.Magenta => 95,
        ConsoleColor.Cyan => 96,
        _ => 97,
    };
}

public enum Align { Left, Right, Center }
