using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fmh.Core.Model;

namespace Fmh.Desktop.Controls;

/// <summary>
/// Isometrische Vereinsgelände-Karte (64×32-Kacheln): Wege, Gebäude, Stadion, Geistervorschau.
/// </summary>
public sealed class IsoMapControl : Control
{
    public const int TileW = 64;
    public const int TileH = 32;

    public static readonly StyledProperty<IsoMapState?> StateProperty =
        AvaloniaProperty.Register<IsoMapControl, IsoMapState?>(nameof(State));

    static IsoMapControl()
    {
        AffectsRender<IsoMapControl>(StateProperty);
        AffectsMeasure<IsoMapControl>(StateProperty);
    }

    public IsoMapState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private Point? _dragStart;
    private (int x, int y)? _hover;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (State is not { } s) return new Size(400, 300);
        var (minX, minY, maxX, maxY) = BoundsOf(s.Width, s.Height);
        return new Size(maxX - minX + TileW, maxY - minY + TileH * 3);
    }

    public override void Render(DrawingContext ctx)
    {
        if (State is not { } s) return;
        var (ox, oy, _, _) = BoundsOf(s.Width, s.Height);
        var origin = new Vector(-ox + 8, -oy + 8);

        // Boden
        for (var y = 0; y < s.Height; y++)
        for (var x = 0; x < s.Width; x++)
        {
            var owned = x < s.BaseWidth;
            DrawTile(ctx, origin, x, y, owned
                ? Color.Parse(y % 2 == 0 ? "#3E6B3A" : "#365F33")
                : Color.Parse("#2A4A28"), 1);
        }

        // Wege
        if (s.ShowPaths)
        {
            foreach (var p in s.Paths)
                DrawTile(ctx, origin, p.X, p.Y, Color.Parse("#C2A26B"), 1);
            foreach (var g in s.Gates)
                DrawTile(ctx, origin, g.X, g.Y, Color.Parse("#E8D48A"), 1);
        }

        // Stadion (vier Tribünen als Komposit)
        DrawStadium(ctx, origin, s);

        // Gebäude
        foreach (var b in s.Buildings.OrderBy(b => b.X + b.Y))
            DrawBuilding(ctx, origin, b);

        // Geistervorschau Gebäude
        if (s.Ghost is { } ghost)
        {
            var color = ghost.Valid ? Color.FromArgb(140, 80, 200, 90) : Color.FromArgb(150, 220, 60, 60);
            for (var x = ghost.X; x < ghost.X + ghost.Width; x++)
            for (var y = ghost.Y; y < ghost.Y + ghost.Height; y++)
                DrawTile(ctx, origin, x, y, color, 1);
            DrawBox(ctx, origin, ghost.X, ghost.Y, ghost.Width, ghost.Height, color, ghost.Category);
            if (!string.IsNullOrEmpty(ghost.CostLabel))
            {
                var sp = Iso(ghost.X, ghost.Y) + origin;
                var text = new FormattedText(ghost.CostLabel, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White);
                ctx.DrawText(text, new Point(sp.X, sp.Y - 18));
            }
        }

        // Wegvorschau
        if (s.PathGhost is { Count: > 0 } pathGhost)
        {
            var ok = s.PathGhostValid;
            var color = ok ? Color.FromArgb(160, 210, 180, 90) : Color.FromArgb(160, 220, 80, 80);
            foreach (var p in pathGhost)
                DrawTile(ctx, origin, p.X, p.Y, color, 1);
        }

        // Warnung: nicht angeschlossene Gebäude
        if (s.ShowPaths)
        {
            foreach (var b in s.Buildings.Where(b => !b.Connected && !b.UnderConstruction))
            {
                var sp = Iso(b.X, b.Y) + origin;
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#E53935")), null, new Point(sp.X, sp.Y - b.Height * 10), 5, 5);
            }
        }
    }

    private static void DrawStadium(DrawingContext ctx, Vector origin, IsoMapState s)
    {
        var sx = s.StadiumX;
        var sy = s.StadiumY;
        var sw = s.StadiumWidth;
        var sh = s.StadiumHeight;
        // Rasen
        for (var x = sx + 1; x < sx + sw - 1; x++)
        for (var y = sy + 1; y < sy + sh - 1; y++)
            DrawTile(ctx, origin, x, y, Color.Parse("#2E7D32"), 1);
        // Tribünen N/S/O/W
        DrawStand(ctx, origin, sx, sy, sw, 1, Color.Parse("#455A64"), s.StandHeights.GetValueOrDefault("North", 1)); // Nord
        DrawStand(ctx, origin, sx, sy + sh - 1, sw, 1, Color.Parse("#546E7A"), s.StandHeights.GetValueOrDefault("South", 1)); // Süd
        DrawStand(ctx, origin, sx, sy, 1, sh, Color.Parse("#607D8B"), s.StandHeights.GetValueOrDefault("West", 1)); // West
        DrawStand(ctx, origin, sx + sw - 1, sy, 1, sh, Color.Parse("#78909C"), s.StandHeights.GetValueOrDefault("East", 1)); // Ost
        if (s.StadiumRoof)
            DrawBox(ctx, origin, sx, sy, sw, sh, Color.FromArgb(80, 255, 255, 255), "admin");
    }

    private static void DrawStand(DrawingContext ctx, Vector origin, int x, int y, int w, int h, Color color, int levels)
    {
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
            DrawBox(ctx, origin, x + i, y + j, 1, 1, color, "admin", Math.Clamp(levels, 1, 4));
    }

    private static void DrawBuilding(DrawingContext ctx, Vector origin, IsoBuildingVisual b)
    {
        var color = CategoryColor(b.Category);
        if (b.UnderConstruction)
            color = Color.FromArgb(160, color.R, color.G, color.B);
        DrawBox(ctx, origin, b.X, b.Y, b.Width, b.Height, color, b.Category, Math.Clamp(b.Level, 1, 4));
        var sp = Iso(b.X, b.Y) + origin;
        if (!string.IsNullOrEmpty(b.Label))
        {
            var text = new FormattedText(b.Label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.White);
            ctx.DrawText(text, new Point(sp.X - text.Width / 2, sp.Y - b.Height * 8 - 14));
        }
    }

    private static void DrawBox(DrawingContext ctx, Vector origin, int x, int y, int w, int h, Color color, string category, int levels = 2)
    {
        var basePt = Iso(x, y + h - 1) + origin; // untere Kachel als Anker
        // Vereinfachte Iso-Quader: Bodenraute + Seiten + Dach
        var tw = TileW / 2.0 * w;
        var th = TileH / 2.0 * h;
        var elev = 10.0 * levels;
        var top = new Point(basePt.X, basePt.Y - elev - th);
        var left = new Point(basePt.X - tw, basePt.Y - elev);
        var right = new Point(basePt.X + tw, basePt.Y - elev);
        var bottom = new Point(basePt.X, basePt.Y - elev + th);

        var fill = new SolidColorBrush(color);
        var dark = new SolidColorBrush(Darken(color, 0.75));
        var light = new SolidColorBrush(Lighten(color, 1.15));
        var roof = new SolidColorBrush(RoofColor(category));

        // Seiten
        var leftFace = Quad(left, top, new Point(top.X, top.Y + elev), new Point(left.X, left.Y + elev));
        ctx.DrawGeometry(dark, null, leftFace);

        var rightFace = Quad(right, top, new Point(top.X, top.Y + elev), new Point(right.X, right.Y + elev));
        ctx.DrawGeometry(fill, null, rightFace);

        // Dach
        var roofGeo = Quad(top, left, bottom, right);
        ctx.DrawGeometry(roof, new Pen(light, 0.8), roofGeo);

        // Fenster
        if (levels >= 2 && w * h >= 1)
        {
            var win = new SolidColorBrush(Color.FromArgb(180, 200, 230, 255));
            ctx.FillRectangle(win, new Rect(right.X - 10, top.Y + elev * 0.35, 4, 4));
            ctx.FillRectangle(win, new Rect(right.X - 18, top.Y + elev * 0.35, 4, 4));
        }
    }

    private static void DrawTile(DrawingContext ctx, Vector origin, int x, int y, Color color, double alpha)
    {
        var c = Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B);
        var p = Iso(x, y) + origin;
        var geo = Quad(
            new Point(p.X, p.Y),
            new Point(p.X + TileW / 2.0, p.Y + TileH / 2.0),
            new Point(p.X, p.Y + TileH),
            new Point(p.X - TileW / 2.0, p.Y + TileH / 2.0));
        ctx.DrawGeometry(new SolidColorBrush(c), new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 0.5), geo);
    }

    private static StreamGeometry Quad(Point a, Point b, Point c, Point d)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(a, true);
        g.LineTo(b);
        g.LineTo(c);
        g.LineTo(d);
        g.EndFigure(true);
        return geo;
    }

    public static Point Iso(int x, int y) => new((x - y) * (TileW / 2.0), (x + y) * (TileH / 2.0));

    public static (int x, int y) ScreenToIso(Point screen, Vector origin)
    {
        var lx = screen.X - origin.X;
        var ly = screen.Y - origin.Y;
        var x = (int)Math.Floor((lx / (TileW / 2.0) + ly / (TileH / 2.0)) / 2.0);
        var y = (int)Math.Floor((ly / (TileH / 2.0) - lx / (TileW / 2.0)) / 2.0);
        return (x, y);
    }

    private (double minX, double minY, double maxX, double maxY) BoundsOf(int w, int h)
    {
        var corners = new[] { Iso(0, 0), Iso(w, 0), Iso(0, h), Iso(w, h) };
        return (corners.Min(p => p.X) - TileW, corners.Min(p => p.Y) - TileH,
            corners.Max(p => p.X) + TileW, corners.Max(p => p.Y) + TileH * 2);
    }

    private Vector OriginVector()
    {
        if (State is not { } s) return default;
        var (ox, oy, _, _) = BoundsOf(s.Width, s.Height);
        return new Vector(-ox + 8, -oy + 8);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (State is not { } s) return;
        var cell = ScreenToIso(e.GetPosition(this), OriginVector());
        if (_hover != cell)
        {
            _hover = cell;
            s.OnHover?.Invoke(cell.x, cell.y);
        }
        if (_dragStart is { } start && s.Tool == IsoMapTool.Path)
        {
            var from = ScreenToIso(start, OriginVector());
            s.OnPathDrag?.Invoke(from.x, from.y, cell.x, cell.y);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (State is not { } s) return;
        var pos = e.GetPosition(this);
        var cell = ScreenToIso(pos, OriginVector());
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            s.OnRightClick?.Invoke(cell.x, cell.y);
            return;
        }
        if (props.IsLeftButtonPressed)
        {
            _dragStart = pos;
            s.OnClick?.Invoke(cell.x, cell.y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (State is not { } s) return;
        if (_dragStart is { } start && s.Tool == IsoMapTool.Path)
        {
            var from = ScreenToIso(start, OriginVector());
            var to = ScreenToIso(e.GetPosition(this), OriginVector());
            s.OnPathRelease?.Invoke(from.x, from.y, to.x, to.y);
        }
        _dragStart = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.R) State?.OnRotate?.Invoke();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
    }

    private static Color CategoryColor(string category) => category switch
    {
        "training" => Color.Parse("#1E88E5"),
        "medical" => Color.Parse("#E53935"),
        "youth" => Color.Parse("#8E24AA"),
        "fans" => Color.Parse("#FB8C00"),
        "commerce" => Color.Parse("#F9A825"),
        "admin" => Color.Parse("#546E7A"),
        "infrastructure" => Color.Parse("#6D4C41"),
        "energy" => Color.Parse("#00ACC1"),
        "teams" => Color.Parse("#43A047"),
        _ => Color.Parse("#757575"),
    };

    private static Color RoofColor(string category) => category switch
    {
        "training" => Color.Parse("#1565C0"),
        "medical" => Color.Parse("#B71C1C"),
        "youth" => Color.Parse("#6A1B9A"),
        "fans" => Color.Parse("#EF6C00"),
        "commerce" => Color.Parse("#F57F17"),
        _ => Color.Parse("#37474F"),
    };

    private static Color Darken(Color c, double f) =>
        Color.FromArgb(c.A, (byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static Color Lighten(Color c, double f) =>
        Color.FromArgb(c.A, (byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f));
}

public enum IsoMapTool { Select, Build, Path }

public sealed class IsoBuildingVisual
{
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Level { get; init; }
    public string Category { get; init; } = "";
    public string Label { get; init; } = "";
    public bool UnderConstruction { get; init; }
    public bool Connected { get; init; } = true;
}

public sealed class IsoGhost
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Valid { get; init; }
    public string Category { get; init; } = "";
    public string CostLabel { get; init; } = "";
}

public sealed class IsoMapState
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int BaseWidth { get; init; }
    public int StadiumX { get; init; }
    public int StadiumY { get; init; }
    public int StadiumWidth { get; init; }
    public int StadiumHeight { get; init; }
    public bool StadiumRoof { get; init; }
    public Dictionary<string, int> StandHeights { get; init; } = new();
    public IReadOnlyList<GridPos> Paths { get; init; } = Array.Empty<GridPos>();
    public IReadOnlyList<GridPos> Gates { get; init; } = Array.Empty<GridPos>();
    public IReadOnlyList<IsoBuildingVisual> Buildings { get; init; } = Array.Empty<IsoBuildingVisual>();
    public IsoGhost? Ghost { get; set; }
    public IReadOnlyList<GridPos>? PathGhost { get; set; }
    public bool PathGhostValid { get; set; } = true;
    public bool ShowPaths { get; set; } = true;
    public IsoMapTool Tool { get; set; } = IsoMapTool.Select;

    public Action<int, int>? OnHover { get; set; }
    public Action<int, int>? OnClick { get; set; }
    public Action<int, int>? OnRightClick { get; set; }
    public Action? OnRotate { get; set; }
    public Action<int, int, int, int>? OnPathDrag { get; set; }
    public Action<int, int, int, int>? OnPathRelease { get; set; }
}
