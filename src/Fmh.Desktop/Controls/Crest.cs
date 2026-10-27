using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.Controls;

/// <summary>Vereinswappen: Schild in den Vereinsfarben mit Mittelstreifen und Kürzel (keine echten Logos, Kapitel 1).</summary>
public sealed class Crest : Control
{
    public static readonly StyledProperty<string> PrimaryProperty = AvaloniaProperty.Register<Crest, string>(nameof(Primary), "Gray");
    public static readonly StyledProperty<string> SecondaryProperty = AvaloniaProperty.Register<Crest, string>(nameof(Secondary), "White");
    public static readonly StyledProperty<string> InitialsProperty = AvaloniaProperty.Register<Crest, string>(nameof(Initials), "");

    static Crest() => AffectsRender<Crest>(PrimaryProperty, SecondaryProperty, InitialsProperty);

    public string Primary { get => GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public string Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public string Initials { get => GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var s = Math.Min(w, h / 1.15);
        var ox = (w - s) / 2;
        var oy = (h - s * 1.15) / 2;
        Point P(double x, double y) => new(ox + x * s, oy + y * s * 1.15);

        var shield = new StreamGeometry();
        using (var g = shield.Open())
        {
            g.BeginFigure(P(0.5, 0.0), true);
            g.LineTo(P(0.97, 0.1));
            g.LineTo(P(0.97, 0.5));
            g.CubicBezierTo(P(0.97, 0.78), P(0.76, 0.93), P(0.5, 1.0));
            g.CubicBezierTo(P(0.24, 0.93), P(0.03, 0.78), P(0.03, 0.5));
            g.LineTo(P(0.03, 0.1));
            g.EndFigure(true);
        }

        var primary = Palette.ClubColor(Primary);
        var secondary = Palette.ClubColor(Secondary);
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Lighten(primary, 0.18), 0), new GradientStop(primary, 0.6), new GradientStop(Darken(primary, 0.25), 1) },
        };
        ctx.DrawGeometry(fill, null, shield);

        using (ctx.PushGeometryClip(shield))
        {
            // Mittelstreifen in der Zweitfarbe
            var band = new Rect(P(0.36, 0), P(0.64, 1.0));
            ctx.FillRectangle(new SolidColorBrush(secondary, 0.92), band);
            // Kopfleiste
            ctx.FillRectangle(new SolidColorBrush(Darken(primary, 0.35), 0.9), new Rect(P(0, 0), P(1, 0.2)));
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), Math.Max(1, s * 0.035)), shield);

        if (!string.IsNullOrEmpty(Initials))
        {
            var text = new FormattedText(Initials, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Black), s * (Initials.Length > 2 ? 0.26 : 0.32),
                new SolidColorBrush(Palette.Contrast(secondary)));
            var badge = new Rect(P(0.16, 0.42), P(0.84, 0.72));
            ctx.DrawRectangle(new SolidColorBrush(Darken(primary, 0.1), 0.95), new Pen(new SolidColorBrush(secondary), Math.Max(1, s * 0.025)), badge, s * 0.06, s * 0.06);
            text.SetForegroundBrush(new SolidColorBrush(Palette.Contrast(Darken(primary, 0.1))));
            ctx.DrawText(text, new Point(badge.Center.X - text.Width / 2, badge.Center.Y - text.Height / 2));
        }
    }

    private static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    private static Color Darken(Color c, double f) =>
        Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

    /// <summary>Kürzel aus dem Vereinsnamen, z. B. "SV Nordheide" → "SVN", "FC Rotenburg" → "FCR".</summary>
    public static string InitialsOf(string name)
    {
        var parts = name.Replace("-", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.EndsWith('.') || p.Length <= 2).ToList();
        var letters = string.Concat(parts.Where(p => char.IsLetter(p[0])).Select(p => char.ToUpperInvariant(p[0])));
        if (letters.Length > 3) letters = letters[..3];
        return letters.Length == 0 ? "?" : letters;
    }
}
