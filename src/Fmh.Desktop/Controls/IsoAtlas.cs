using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Fmh.Desktop.Controls;

/// <summary>
/// Pixel-Art-Atlas der Vereinsgelände-Karte (Assets/grounds.png + grounds.json, erzeugt von tools/sprites/gen_sprites.py).
/// Jeder Eintrag: Ausschnitt im Atlas und Anker (obere Ecke der Grundfläche). Maskenpixel (Rot = Blau, Grün = 0)
/// werden in die Vereinsfarbe umgefärbt; je Farbe entsteht einmal eine gefärbte Kopie des Atlas.
/// </summary>
public sealed class IsoAtlas
{
    private static readonly Lazy<IsoAtlas?> SharedAtlas = new(Load);

    /// <summary>Gemeinsamer Atlas; null, wenn die Grafik nicht geladen werden kann (dann zeichnet die Karte Vektorgrafik).</summary>
    public static IsoAtlas? Shared => SharedAtlas.Value;

    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _pixels;
    private readonly int _stride;
    private readonly bool _bgra;
    private readonly Dictionary<string, (PixelRect src, int ax, int ay)> _sprites;
    private readonly Dictionary<Color, WriteableBitmap> _tinted = new();

    private IsoAtlas(WriteableBitmap bitmap, Dictionary<string, (PixelRect, int, int)> sprites)
    {
        _bitmap = bitmap;
        _sprites = sprites;
        using var fb = bitmap.Lock();
        _stride = fb.RowBytes;
        _bgra = fb.Format == PixelFormat.Bgra8888;
        _pixels = new byte[_stride * fb.Size.Height];
        Marshal.Copy(fb.Address, _pixels, 0, _pixels.Length);
    }

    private static IsoAtlas? Load()
    {
        try
        {
            using var png = AssetLoader.Open(new Uri("avares://fmh/Assets/grounds.png"));
            using var json = AssetLoader.Open(new Uri("avares://fmh/Assets/grounds.json"));
            var raw = JsonSerializer.Deserialize<Dictionary<string, int[]>>(json) ?? new();
            var sprites = raw.ToDictionary(kv => kv.Key, kv => (new PixelRect(kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]), kv.Value[4], kv.Value[5]));
            return new IsoAtlas(WriteableBitmap.Decode(png), sprites);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Has(string name) => _sprites.ContainsKey(name);

    public int Count => _sprites.Count;

    /// <summary>Oberkante des Sprites über dem Anker (für Beschriftungen).</summary>
    public int Above(string name) => _sprites.TryGetValue(name, out var s) ? s.ay : 0;

    /// <summary>Zeichnet ein Sprite mit seinem Anker auf <paramref name="anchor"/>; optional mit Vereinsfarbe und Deckkraft.</summary>
    public void Draw(DrawingContext ctx, string name, Point anchor, Color? tint = null, double opacity = 1)
    {
        if (!_sprites.TryGetValue(name, out var s)) return;
        var dest = new Rect(Math.Round(anchor.X) - s.ax, Math.Round(anchor.Y) - s.ay, s.src.Width, s.src.Height);
        var image = tint is { } c ? Tinted(c) : _bitmap;
        using (ctx.PushOpacity(opacity))
            ctx.DrawImage(image, new Rect(s.src.X, s.src.Y, s.src.Width, s.src.Height), dest);
    }

    private WriteableBitmap Tinted(Color color)
    {
        if (_tinted.TryGetValue(color, out var done)) return done;
        var px = (byte[])_pixels.Clone();
        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            // Maske: erster und dritter Kanal gleich (Rot = Blau in beiden Byte-Reihenfolgen), Grün = 0
            if (px[i + 3] == 0 || px[i + 1] != 0 || px[i] != px[i + 2] || px[i] == 0) continue;
            var f = px[i] / 200.0 * px[i + 3] / 255.0;
            byte r = Scale(color.R, f), g = Scale(color.G, f), b = Scale(color.B, f);
            (px[i], px[i + 1], px[i + 2]) = _bgra ? (b, g, r) : (r, g, b);
        }
        var bmp = new WriteableBitmap(_bitmap.PixelSize, _bitmap.Dpi, _bgra ? PixelFormat.Bgra8888 : PixelFormat.Rgba8888, _bitmap.AlphaFormat);
        using (var fb = bmp.Lock())
            for (var y = 0; y < fb.Size.Height; y++)
                Marshal.Copy(px, y * _stride, fb.Address + y * fb.RowBytes, Math.Min(_stride, fb.RowBytes));
        if (_tinted.Count > 4) _tinted.Clear();
        return _tinted[color] = bmp;
    }

    private static byte Scale(byte c, double f) => (byte)Math.Clamp(c * f, 0, 255);
}
