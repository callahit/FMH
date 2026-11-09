using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Fmh.Core.Model;

namespace Fmh.Desktop.Controls;

/// <summary>
/// Isometrische Vereinsgelände-Karte (64×32-Kacheln): Rasen, Wege, Bäume, gezeichnete Gebäude und Stadion, Geistervorschau.
/// Die Zeichnungen liegen in <see cref="IsoArt"/>, <see cref="IsoBuildings"/> und <see cref="IsoStadium"/>.
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

    /// <summary>Vergrößerung der Karte (Strg+Mausrad oder +/−).</summary>
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.6, 2.4); _userZoom = true; InvalidateMeasure(); InvalidateVisual(); }
    }

    private double _zoom = 1.0;
    private bool _userZoom;
    private ScrollViewer? _viewer;
    private Point? _dragStart;
    private (int x, int y)? _hover;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (State is not { } s) return new Size(400, 300);
        var (minX, minY, maxX, maxY) = BoundsOf(s.Width, s.Height);
        // Ohne eigenen Zoom füllt die Karte die Breite des sichtbaren Bereichs
        // (höchstens etwas breiter als sichtbar, dann seitlich scrollen) und möglichst die Höhe.
        if (!_userZoom && _viewer is { Bounds.Width: > 100, Bounds.Height: > 100 } v)
        {
            var fitW = (v.Bounds.Width - 24) / (maxX - minX + TileW);
            var fitH = (v.Bounds.Height - 24) / (maxY - minY + TileH * 3);
            // Pixel-Art bleibt scharf bei ganzzahliger Vergrößerung: ab 0,8 auf 1× bzw. 2× einrasten
            var fit = Math.Min(fitH, fitW * 1.35);
            _zoom = fit >= 2 ? 2 : fit >= 0.8 ? 1 : Math.Max(0.6, fit);
        }
        return new Size((maxX - minX + TileW) * Zoom, (maxY - minY + TileH * 3) * Zoom);
    }

    public override void Render(DrawingContext ctx)
    {
        if (State is not { } s) return;
        var o = OriginVector();
        using var zoom = ctx.PushTransform(Matrix.CreateScale(Zoom, Zoom));
        // Pixel-Art scharf zeichnen (ohne Weichzeichnen beim Vergrößern)
        using var pixels = ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });
        var atlas = IsoAtlas.Shared;
        IsoPixelArt.Trees(ctx, o, s, back: true, atlas);
        if (atlas is not null) IsoPixelArt.Ground(ctx, o, s, atlas);
        else
        {
            DrawGround(ctx, o, s);
            if (s.ShowPaths) DrawPaths(ctx, o, s);
        }

        // Markiertes freies Feld bzw. Feld unter der Maus
        if (_hover is { } hv && hv.x >= 0 && hv.y >= 0 && hv.x < s.Width && hv.y < s.Height && s.Ghost is null && HitBuilding(s, hv) is null)
            Outline(ctx, o, hv.x, hv.y, 1, 1, Color.FromArgb(200, 245, 192, 78), 1.6);
        if (s.SelectedCell is { } sc)
            Outline(ctx, o, sc.X, sc.Y, 1, 1, Color.Parse("#F5C04E"), 2.4);

        // Gebäude und Stadion von hinten nach vorn
        var items = s.Buildings.Select(b => (key: b.X + b.Width + b.Y + b.Height, b: (IsoBuildingVisual?)b))
            .Append((key: s.StadiumX + s.StadiumWidth + s.StadiumY + s.StadiumHeight, b: null))
            .OrderBy(i => i.key).ToList();
        var anchors = new Dictionary<int, Point>();
        var stadiumTop = default(Point);
        var decos = atlas is null ? new() : IsoPixelArt.Decorations(s);
        var di = 0;
        foreach (var (key, b) in items)
        {
            for (; di < decos.Count && decos[di].Key < key; di++) IsoPixelArt.Decoration(ctx, o, decos[di], s, atlas!);
            if (b is null) { stadiumTop = (atlas is null ? null : IsoPixelArt.Stadium(ctx, o, s, atlas)) ?? IsoStadium.Draw(ctx, o, s); continue; }
            if (b.Id == s.SelectedId) Outline(ctx, o, b.X, b.Y, b.Width, b.Height, Color.Parse("#F5C04E"), 2.6);
            anchors[b.Id] = (atlas is null ? null : IsoPixelArt.Building(ctx, o, b, atlas)) ?? IsoBuildings.Draw(ctx, o, b);
        }
        for (; di < decos.Count; di++) IsoPixelArt.Decoration(ctx, o, decos[di], s, atlas!);
        IsoPixelArt.Trees(ctx, o, s, back: false, atlas);

        // Geistervorschau Gebäude
        if (s.Ghost is { } ghost)
        {
            var color = ghost.Valid ? Color.FromArgb(150, 90, 210, 110) : Color.FromArgb(160, 230, 70, 70);
            using (ctx.PushTransform(IsoArt.Ground(o)))
                ctx.FillRectangle(IsoArt.B(IsoArt.Alpha(color, 90)), new Rect(ghost.X, ghost.Y, ghost.Width, ghost.Height));
            var (light, _, _) = IsoArt.Category(ghost.Category);
            IsoArt.Box(ctx, o, ghost.X + 0.1, ghost.Y + 0.1, ghost.Width - 0.2, ghost.Height - 0.2, 0, 16,
                IsoArt.Alpha(IsoArt.Shade(light, 0.7), 130), IsoArt.Alpha(light, 130), IsoArt.Alpha(IsoArt.Shade(light, 1.15), 130));
            Outline(ctx, o, ghost.X, ghost.Y, ghost.Width, ghost.Height, color, 2);
            if (!string.IsNullOrEmpty(ghost.CostLabel))
                IsoArt.Label(ctx, IsoArt.P(o, ghost.X + ghost.Width / 2.0, ghost.Y + ghost.Height / 2.0, 34), ghost.CostLabel);
        }

        // Wegvorschau
        if (s.PathGhost is { Count: > 0 } pathGhost)
        {
            var color = s.PathGhostValid ? Color.FromArgb(170, 225, 200, 120) : Color.FromArgb(170, 230, 80, 80);
            using (ctx.PushTransform(IsoArt.Ground(o)))
                foreach (var p in pathGhost) ctx.FillRectangle(IsoArt.B(color), new Rect(p.X + 0.08, p.Y + 0.08, 0.84, 0.84));
        }

        // Warnung: nicht an das Wegenetz angeschlossen
        if (s.ShowPaths)
            foreach (var b in s.Buildings.Where(b => !b.Connected && !b.UnderConstruction && anchors.ContainsKey(b.Id)))
            {
                var c = anchors[b.Id] + new Vector(-12, -8);
                ctx.DrawEllipse(IsoArt.B("#E53935"), new Pen(Brushes.White, 1.2), c, 6.5, 6.5);
                var t = IsoArt.Text("!", 10, Brushes.White, FontWeight.Bold);
                ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
            }

        // Beschriftungen: Stadion, gewähltes Gebäude, Gebäude unter der Maus
        if (!string.IsNullOrEmpty(s.StadiumLabel)) IsoArt.Label(ctx, stadiumTop, s.StadiumLabel);
        var hovered = _hover is { } h ? HitBuilding(s, h) : null;
        foreach (var b in s.Buildings.Where(b => (b.Id == s.SelectedId || b == hovered) && anchors.ContainsKey(b.Id)).DistinctBy(b => b.Id))
        {
            IsoArt.Label(ctx, anchors[b.Id] - new Vector(0, 20), b.Label);
            IsoArt.Level(ctx, anchors[b.Id] + new Vector(10, -6), b.Level, IsoArt.Category(b.Category).light);
        }
    }

    private static IsoBuildingVisual? HitBuilding(IsoMapState s, (int x, int y) c) =>
        s.Buildings.FirstOrDefault(b => c.x >= b.X && c.x < b.X + b.Width && c.y >= b.Y && c.y < b.Y + b.Height);

    private static void Outline(DrawingContext ctx, Vector o, double x, double y, double w, double h, Color color, double thickness) =>
        ctx.DrawGeometry(null, new Pen(IsoArt.B(color), thickness, lineJoin: PenLineJoin.Round),
            IsoArt.Poly(IsoArt.P(o, x, y), IsoArt.P(o, x + w, y), IsoArt.P(o, x + w, y + h), IsoArt.P(o, x, y + h)));

    /// <summary>Rasen mit Mähstreifen; zugekauftes Bauland leicht heller, dahinter die nächste kaufbare Spalte gestrichelt.</summary>
    private static void DrawGround(DrawingContext ctx, Vector o, IsoMapState s)
    {
        // Erdkante unter der Fläche
        var depth = 6.0;
        ctx.DrawGeometry(IsoArt.B("#6B4E35"), null, IsoArt.Poly(IsoArt.P(o, 0, s.Height), IsoArt.P(o, s.Width, s.Height),
            IsoArt.P(o, s.Width, s.Height) + new Vector(0, depth), IsoArt.P(o, 0, s.Height) + new Vector(0, depth)));
        ctx.DrawGeometry(IsoArt.B("#5A412C"), null, IsoArt.Poly(IsoArt.P(o, s.Width, s.Height), IsoArt.P(o, s.Width, 0),
            IsoArt.P(o, s.Width, 0) + new Vector(0, depth), IsoArt.P(o, s.Width, s.Height) + new Vector(0, depth)));
        using (ctx.PushTransform(IsoArt.Ground(o)))
        {
            ctx.FillRectangle(IsoArt.B("#4E9A52"), new Rect(0, 0, s.Width, s.Height));
            for (var x = 0; x < s.Width; x += 2) ctx.FillRectangle(IsoArt.B("#47904B"), new Rect(x, 0, 1, s.Height));
            if (s.BaseWidth < s.Width)
            {
                ctx.FillRectangle(IsoArt.B(Color.FromArgb(22, 255, 255, 255)), new Rect(s.BaseWidth, 0, s.Width - s.BaseWidth, s.Height));
                ctx.DrawLine(new Pen(IsoArt.B(Color.FromArgb(110, 255, 255, 255)), 0.03, new DashStyle(new double[] { 4, 4 }, 0)),
                    new Point(s.BaseWidth, 0), new Point(s.BaseWidth, s.Height));
            }
            var grid = new Pen(IsoArt.B(Color.FromArgb(18, 255, 255, 255)), 0.015);
            for (var x = 1; x < s.Width; x++) ctx.DrawLine(grid, new Point(x, 0), new Point(x, s.Height));
            for (var y = 1; y < s.Height; y++) ctx.DrawLine(grid, new Point(0, y), new Point(s.Width, y));
            // Kaufbare Spalte rechts davon
            ctx.FillRectangle(IsoArt.B(Color.FromArgb(40, 120, 150, 110)), new Rect(s.Width, 0, 1, s.Height));
            ctx.DrawRectangle(null, new Pen(IsoArt.B(Color.FromArgb(90, 255, 255, 255)), 0.025, new DashStyle(new double[] { 3, 3 }, 0)), new Rect(s.Width, 0, 1, s.Height));
        }
    }

    /// <summary>Wege als helle Pflasterflächen mit Bordstein an den Kanten, Tore als Torbögen.</summary>
    private static void DrawPaths(DrawingContext ctx, Vector o, IsoMapState s)
    {
        var set = s.Paths.Select(p => (p.X, p.Y)).ToHashSet();
        var curb = new Pen(IsoArt.B("#A8977A"), 0.05);
        using (ctx.PushTransform(IsoArt.Ground(o)))
        {
            foreach (var p in s.Paths)
            {
                ctx.FillRectangle(IsoArt.B("#D9CFB8"), new Rect(p.X, p.Y, 1, 1));
                ctx.FillRectangle(IsoArt.B(Color.FromArgb(25, 0, 0, 0)), new Rect(p.X + 0.48, p.Y + 0.48, 0.04, 0.04));
                if (!set.Contains((p.X, p.Y - 1))) ctx.DrawLine(curb, new Point(p.X, p.Y), new Point(p.X + 1, p.Y));
                if (!set.Contains((p.X, p.Y + 1))) ctx.DrawLine(curb, new Point(p.X, p.Y + 1), new Point(p.X + 1, p.Y + 1));
                if (!set.Contains((p.X - 1, p.Y))) ctx.DrawLine(curb, new Point(p.X, p.Y), new Point(p.X, p.Y + 1));
                if (!set.Contains((p.X + 1, p.Y))) ctx.DrawLine(curb, new Point(p.X + 1, p.Y), new Point(p.X + 1, p.Y + 1));
            }
            foreach (var g in s.Gates) ctx.FillRectangle(IsoArt.B("#E8D9A8"), new Rect(g.X, g.Y, 1, 1));
        }
        foreach (var g in s.Gates)
        {
            var a = IsoArt.P(o, g.X + 0.15, g.Y + 0.5);
            var b = IsoArt.P(o, g.X + 0.85, g.Y + 0.5);
            var pen = new Pen(IsoArt.B("#3A4256"), 3);
            ctx.DrawLine(pen, a, a - new Vector(0, 22));
            ctx.DrawLine(pen, b, b - new Vector(0, 22));
            ctx.DrawLine(new Pen(IsoArt.B("#F5C04E"), 4), a - new Vector(0, 22), b - new Vector(0, 22));
        }
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
        // Rand für Baumreihe, kaufbare Spalte und Masten
        static Point IsoD(double x, double y) => new((x - y) * (TileW / 2.0), (x + y) * (TileH / 2.0));
        var corners = new[] { IsoD(-0.9, -0.9), IsoD(w + 1.9, -0.9), IsoD(-0.9, h + 0.9), IsoD(w + 1.9, h + 0.9) };
        return (corners.Min(p => p.X) - 8, corners.Min(p => p.Y) - TileH * 2,
            corners.Max(p => p.X) + 8, corners.Max(p => p.Y) + 8);
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
        var cell = ScreenToIso(Unzoom(e.GetPosition(this)), OriginVector());
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
        var pos = Unzoom(e.GetPosition(this));
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
            var to = ScreenToIso(Unzoom(e.GetPosition(this)), OriginVector());
            s.OnPathRelease?.Invoke(from.x, from.y, to.x, to.y);
        }
        _dragStart = null;
    }

    private Point Unzoom(Point p) => new(p.X / Zoom, p.Y / Zoom);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        Zoom *= e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.OemPlus or Key.Add) Zoom *= 1.12;
        if (e.Key is Key.OemMinus or Key.Subtract) Zoom /= 1.12;
        if (e.Key == Key.R) State?.OnRotate?.Invoke();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
        _viewer = this.FindAncestorOfType<ScrollViewer>();
        if (_viewer is not null) _viewer.PropertyChanged += OnViewerChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewer is not null) _viewer.PropertyChanged -= OnViewerChanged;
        _viewer = null;
    }

    private void OnViewerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && !_userZoom) { InvalidateMeasure(); InvalidateVisual(); }
    }
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
    /// <summary>Gebäudetyp (Id aus rules/buildings.json) für die Zeichenart.</summary>
    public string Type { get; init; } = "";
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
    /// <summary>Plätze je Tribüne (North/South/West/East) für Tiefe und Höhe der Ränge.</summary>
    public Dictionary<string, int> StandCapacities { get; init; } = new();
    public IReadOnlySet<string> RoofSides { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BoxSides { get; init; } = new HashSet<string>();
    public int Floodlight { get; init; }
    public Color SeatColor { get; init; } = Color.Parse("#C8414A");
    public string StadiumLabel { get; init; } = "";
    public int? SelectedId { get; init; }
    public GridPos? SelectedCell { get; init; }
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
