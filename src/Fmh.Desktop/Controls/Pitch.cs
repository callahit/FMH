using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fmh.Desktop.Controls;

/// <summary>Gezeichnetes Spielfeld (Hochformat, Angriff nach oben) mit Rasenstreifen und Linien.</summary>
public class Pitch : Control
{
    public static readonly StyledProperty<IBrush?> GrassProperty = AvaloniaProperty.Register<Pitch, IBrush?>(nameof(Grass));
    public static readonly StyledProperty<IBrush?> LinesProperty = AvaloniaProperty.Register<Pitch, IBrush?>(nameof(Lines));

    static Pitch() => AffectsRender<Pitch>(GrassProperty, LinesProperty);

    public IBrush? Grass { get => GetValue(GrassProperty); set => SetValue(GrassProperty, value); }
    public IBrush? Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var grass = (Grass as ISolidColorBrush)?.Color ?? Color.Parse("#2E7D4A");
        ctx.DrawRectangle(new SolidColorBrush(grass), null, r, 10, 10);
        const int stripes = 12;
        var sh = r.Height / stripes;
        var dark = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0));
        for (var i = 0; i < stripes; i += 2) ctx.FillRectangle(dark, new Rect(0, i * sh, r.Width, sh));

        var pen = new Pen(Lines ?? Brushes.White, 1.6) { };
        var m = Math.Min(r.Width, r.Height) * 0.04;
        var f = r.Deflate(m);
        ctx.DrawRectangle(null, pen, f);
        ctx.DrawLine(pen, new Point(f.Left, f.Center.Y), new Point(f.Right, f.Center.Y));
        var cr = f.Width * 0.14;
        ctx.DrawEllipse(null, pen, f.Center, cr, cr);
        ctx.DrawEllipse(Lines, null, f.Center, 2.5, 2.5);
        // Strafräume und Torräume
        var bw = f.Width * 0.58;
        var bh = f.Height * 0.15;
        var gw = f.Width * 0.27;
        var gh = f.Height * 0.055;
        ctx.DrawRectangle(null, pen, new Rect(f.Center.X - bw / 2, f.Top, bw, bh));
        ctx.DrawRectangle(null, pen, new Rect(f.Center.X - gw / 2, f.Top, gw, gh));
        ctx.DrawRectangle(null, pen, new Rect(f.Center.X - bw / 2, f.Bottom - bh, bw, bh));
        ctx.DrawRectangle(null, pen, new Rect(f.Center.X - gw / 2, f.Bottom - gh, gw, gh));
        var goal = f.Width * 0.12;
        ctx.DrawRectangle(Lines, null, new Rect(f.Center.X - goal / 2, f.Top - 4, goal, 4));
        ctx.DrawRectangle(Lines, null, new Rect(f.Center.X - goal / 2, f.Bottom, goal, 4));
    }
}

/// <summary>Spielfeld mit Heatmap: 5 Spalten x 7 Reihen, Reihe 0 = eigenes Tor (unten).</summary>
public sealed class Heatmap : Pitch
{
    public static readonly StyledProperty<IReadOnlyList<int>?> ValuesProperty = AvaloniaProperty.Register<Heatmap, IReadOnlyList<int>?>(nameof(Values));
    public static readonly StyledProperty<IBrush?> HeatProperty = AvaloniaProperty.Register<Heatmap, IBrush?>(nameof(Heat));

    static Heatmap() => AffectsRender<Heatmap>(ValuesProperty, HeatProperty);

    public IReadOnlyList<int>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush? Heat { get => GetValue(HeatProperty); set => SetValue(HeatProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (Values is not { Count: 35 } v) return;
        var max = Math.Max(1, v.Max());
        var r = new Rect(Bounds.Size).Deflate(Math.Min(Bounds.Width, Bounds.Height) * 0.04);
        var cw = r.Width / 5;
        var ch = r.Height / 7;
        for (var row = 0; row < 7; row++)
        {
            for (var col = 0; col < 5; col++)
            {
                var value = v[row * 5 + col] / (double)max;
                if (value <= 0.02) continue;
                var color = value > 0.66 ? Color.Parse("#FF4D3D") : value > 0.33 ? Color.Parse("#FFB020") : Color.Parse("#FFE66B");
                var cell = new Rect(r.Left + col * cw, r.Top + (6 - row) * ch, cw, ch).Deflate(2);
                ctx.DrawRectangle(new SolidColorBrush(color, 0.15 + 0.6 * value), null, cell, 6, 6);
            }
        }
    }
}

/// <summary>Ordnet Kinder relativ zur Größe an: X und Y von 0 bis 1 (Mittelpunkt des Kindes).</summary>
public sealed class PitchPanel : Panel
{
    public static readonly AttachedProperty<double> XProperty = AvaloniaProperty.RegisterAttached<PitchPanel, Control, double>("X", 0.5);
    public static readonly AttachedProperty<double> YProperty = AvaloniaProperty.RegisterAttached<PitchPanel, Control, double>("Y", 0.5);

    static PitchPanel()
    {
        AffectsParentArrange<PitchPanel>(XProperty, YProperty);
    }

    public static double GetX(Control c) => c.GetValue(XProperty);
    public static void SetX(Control c, double v) => c.SetValue(XProperty, v);
    public static double GetY(Control c) => c.GetValue(YProperty);
    public static void SetY(Control c, double v) => c.SetValue(YProperty, v);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);
        return new Size(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width,
                        double.IsInfinity(availableSize.Height) ? 560 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var x = GetX(child) * finalSize.Width;
            var y = GetY(child) * finalSize.Height;
            var s = child.DesiredSize;
            child.Arrange(new Rect(x - s.Width / 2, y - s.Height / 2, s.Width, s.Height));
        }
        return finalSize;
    }
}
