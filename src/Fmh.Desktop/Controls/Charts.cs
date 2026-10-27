using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fmh.Desktop.Controls;

/// <summary>Vergleichsbalken für Spielstatistiken: links Heim, rechts Gast.</summary>
public sealed class CompareBar : Control
{
    public static readonly StyledProperty<double> LeftProperty = AvaloniaProperty.Register<CompareBar, double>(nameof(Left));
    public static readonly StyledProperty<double> RightProperty = AvaloniaProperty.Register<CompareBar, double>(nameof(Right));
    public static readonly StyledProperty<IBrush?> LeftBrushProperty = AvaloniaProperty.Register<CompareBar, IBrush?>(nameof(LeftBrush));
    public static readonly StyledProperty<IBrush?> RightBrushProperty = AvaloniaProperty.Register<CompareBar, IBrush?>(nameof(RightBrush));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<CompareBar, IBrush?>(nameof(Track));

    static CompareBar() => AffectsRender<CompareBar>(LeftProperty, RightProperty, LeftBrushProperty, RightBrushProperty, TrackProperty);

    public double Left { get => GetValue(LeftProperty); set => SetValue(LeftProperty, value); }
    public double Right { get => GetValue(RightProperty); set => SetValue(RightProperty, value); }
    public IBrush? LeftBrush { get => GetValue(LeftBrushProperty); set => SetValue(LeftBrushProperty, value); }
    public IBrush? RightBrush { get => GetValue(RightBrushProperty); set => SetValue(RightBrushProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(availableSize.Width, 400), 8);

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        var h = Math.Min(8, r.Height);
        var y = (r.Height - h) / 2;
        ctx.DrawRectangle(Track, null, new Rect(0, y, r.Width, h), h / 2, h / 2);
        var total = Left + Right;
        if (total <= 0) return;
        var lw = r.Width * Left / total;
        ctx.DrawRectangle(LeftBrush, null, new Rect(0, y, Math.Max(0, lw - 1), h), h / 2, h / 2);
        ctx.DrawRectangle(RightBrush, null, new Rect(lw + 1, y, Math.Max(0, r.Width - lw - 1), h), h / 2, h / 2);
    }
}

/// <summary>Einfacher Balken (Kondition, Attribute), Wert von 0 bis Maximum.</summary>
public sealed class Meter : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Meter, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Meter, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Meter, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<Meter, IBrush?>(nameof(Track));

    static Meter() => AffectsRender<Meter>(ValueProperty, MaximumProperty, FillProperty, TrackProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(availableSize.Width, 120), 6);

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        var h = Math.Min(6, r.Height);
        var y = (r.Height - h) / 2;
        ctx.DrawRectangle(Track, null, new Rect(0, y, r.Width, h), h / 2, h / 2);
        var w = r.Width * Math.Clamp(Value / Math.Max(1e-9, Maximum), 0, 1);
        if (w > 0) ctx.DrawRectangle(Fill, null, new Rect(0, y, w, h), h / 2, h / 2);
    }
}

/// <summary>Liniendiagramm mit bis zu zwei Reihen (z. B. xG-Verlauf über 90 Minuten oder Formkurve).</summary>
public sealed class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<Point>?> SeriesAProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<Point>?>(nameof(SeriesA));
    public static readonly StyledProperty<IReadOnlyList<Point>?> SeriesBProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<Point>?>(nameof(SeriesB));
    public static readonly StyledProperty<IReadOnlyList<double>?> MarkersAProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<double>?>(nameof(MarkersA));
    public static readonly StyledProperty<IReadOnlyList<double>?> MarkersBProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<double>?>(nameof(MarkersB));
    public static readonly StyledProperty<double> MaxXProperty = AvaloniaProperty.Register<LineChart, double>(nameof(MaxX), 90);
    public static readonly StyledProperty<double> MaxYProperty = AvaloniaProperty.Register<LineChart, double>(nameof(MaxY), 1);
    public static readonly StyledProperty<double> MinYProperty = AvaloniaProperty.Register<LineChart, double>(nameof(MinY), 0);
    public static readonly StyledProperty<bool> InvertYProperty = AvaloniaProperty.Register<LineChart, bool>(nameof(InvertY));
    public static readonly StyledProperty<bool> StepProperty = AvaloniaProperty.Register<LineChart, bool>(nameof(Step));
    public static readonly StyledProperty<IBrush?> BrushAProperty = AvaloniaProperty.Register<LineChart, IBrush?>(nameof(BrushA));
    public static readonly StyledProperty<IBrush?> BrushBProperty = AvaloniaProperty.Register<LineChart, IBrush?>(nameof(BrushB));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<LineChart, IBrush?>(nameof(GridBrush));

    static LineChart() => AffectsRender<LineChart>(SeriesAProperty, SeriesBProperty, MarkersAProperty, MarkersBProperty, MaxXProperty, MaxYProperty,
        MinYProperty, InvertYProperty, StepProperty, BrushAProperty, BrushBProperty, GridBrushProperty);

    public IReadOnlyList<Point>? SeriesA { get => GetValue(SeriesAProperty); set => SetValue(SeriesAProperty, value); }
    public IReadOnlyList<Point>? SeriesB { get => GetValue(SeriesBProperty); set => SetValue(SeriesBProperty, value); }
    public IReadOnlyList<double>? MarkersA { get => GetValue(MarkersAProperty); set => SetValue(MarkersAProperty, value); }
    public IReadOnlyList<double>? MarkersB { get => GetValue(MarkersBProperty); set => SetValue(MarkersBProperty, value); }
    public double MaxX { get => GetValue(MaxXProperty); set => SetValue(MaxXProperty, value); }
    public double MaxY { get => GetValue(MaxYProperty); set => SetValue(MaxYProperty, value); }
    public double MinY { get => GetValue(MinYProperty); set => SetValue(MinYProperty, value); }
    /// <summary>Für Noten: kleiner ist besser, also oben.</summary>
    public bool InvertY { get => GetValue(InvertYProperty); set => SetValue(InvertYProperty, value); }
    public bool Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public IBrush? BrushA { get => GetValue(BrushAProperty); set => SetValue(BrushAProperty, value); }
    public IBrush? BrushB { get => GetValue(BrushBProperty); set => SetValue(BrushBProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(new Thickness(4, 6));
        if (r.Width <= 0 || r.Height <= 0) return;
        var grid = new Pen(GridBrush ?? Brushes.Gray, 1, DashStyle.Dash);
        for (var i = 0; i <= 3; i++)
        {
            var y = r.Top + r.Height * i / 3;
            ctx.DrawLine(grid, new Point(r.Left, y), new Point(r.Right, y));
        }
        var maxY = Math.Max(MaxY, MinY + 1e-6);
        foreach (var series in new[] { SeriesA, SeriesB }) if (series is not null) maxY = Math.Max(maxY, series.Select(p => p.Y).DefaultIfEmpty(0).Max());

        Point Map(Point p)
        {
            var fy = (p.Y - MinY) / (maxY - MinY);
            if (InvertY) fy = 1 - fy;
            return new Point(r.Left + r.Width * Math.Clamp(p.X / MaxX, 0, 1), r.Bottom - r.Height * Math.Clamp(fy, 0, 1));
        }

        void Draw(IReadOnlyList<Point>? s, IBrush? brush, IReadOnlyList<double>? markers)
        {
            if (s is null || s.Count == 0 || brush is null) return;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Map(s[0]), false);
                for (var i = 1; i < s.Count; i++)
                {
                    if (Step) g.LineTo(Map(new Point(s[i].X, s[i - 1].Y)));
                    g.LineTo(Map(s[i]));
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(brush, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
            if (!Step) foreach (var p in s) ctx.DrawEllipse(brush, null, Map(p), 3, 3);
            if (markers is null) return;
            foreach (var m in markers)
            {
                var y = s.LastOrDefault(p => p.X <= m).Y;
                ctx.DrawEllipse(brush, new Pen(Brushes.White, 1.5), Map(new Point(m, y)), 5, 5);
            }
        }
        Draw(SeriesA, BrushA, MarkersA);
        Draw(SeriesB, BrushB, MarkersB);
    }
}

/// <summary>Netzdiagramm (Radar) für Spielerprofile, Werte 0–20.</summary>
public sealed class RadarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<RadarChart, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty = AvaloniaProperty.Register<RadarChart, IReadOnlyList<string>?>(nameof(Labels));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<RadarChart, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<RadarChart, IBrush?>(nameof(LineBrush));
    public static readonly StyledProperty<IBrush?> TextBrushProperty = AvaloniaProperty.Register<RadarChart, IBrush?>(nameof(TextBrush));

    static RadarChart() => AffectsRender<RadarChart>(ValuesProperty, LabelsProperty, FillProperty, LineBrushProperty, TextBrushProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IReadOnlyList<string>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        if (Values is not { Count: >= 3 } values) return;
        var n = values.Count;
        var c = new Rect(Bounds.Size).Center;
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 34;
        if (radius <= 10) return;
        Point At(int i, double f)
        {
            var a = -Math.PI / 2 + 2 * Math.PI * i / n;
            return new Point(c.X + Math.Cos(a) * radius * f, c.Y + Math.Sin(a) * radius * f);
        }
        var pen = new Pen(LineBrush ?? Brushes.Gray, 1);
        for (var ring = 1; ring <= 4; ring++)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(At(0, ring / 4.0), false);
                for (var i = 1; i < n; i++) g.LineTo(At(i, ring / 4.0));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        for (var i = 0; i < n; i++) ctx.DrawLine(pen, c, At(i, 1));

        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(At(0, values[0] / 20.0), true);
            for (var i = 1; i < n; i++) g.LineTo(At(i, Math.Clamp(values[i] / 20.0, 0, 1)));
            g.EndFigure(true);
        }
        var fill = Fill ?? Brushes.Green;
        var color = (fill as ISolidColorBrush)?.Color ?? Colors.Green;
        ctx.DrawGeometry(new SolidColorBrush(color, 0.35), new Pen(fill, 2), shape);

        if (Labels is null) return;
        for (var i = 0; i < n && i < Labels.Count; i++)
        {
            var text = new FormattedText($"{Labels[i]} {Fmh.Core.Fmt.Num(values[i])}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 11, TextBrush ?? Brushes.White);
            var p = At(i, 1.18);
            ctx.DrawText(text, new Point(p.X - text.Width / 2, p.Y - text.Height / 2));
        }
    }
}
