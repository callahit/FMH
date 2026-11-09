using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using static Fmh.Desktop.Controls.IsoArt;

namespace Fmh.Desktop.Controls;

/// <summary>Daten für die Stadion-Draufsicht.</summary>
public sealed record StadiumPlanModel(
    IReadOnlyDictionary<string, int> Capacities, IReadOnlySet<string> Roofed, IReadOnlySet<string> Boxes,
    int Floodlight, Color Seat, IReadOnlyDictionary<string, string> Labels);

/// <summary>
/// Stadion von oben: Spielfeld, vier Tribünen mit Sitzreihen in der Vereinsfarbe (Tiefe nach Plätzen), Glasdach über
/// überdachten Tribünen, Logenband, Flutlichtmasten nach Ausbaustufe, ringsum Vorplatz und Bäume.
/// </summary>
public sealed class StadiumPlan : Control
{
    public static readonly StyledProperty<StadiumPlanModel?> ModelProperty = AvaloniaProperty.Register<StadiumPlan, StadiumPlanModel?>(nameof(Model));

    static StadiumPlan() => AffectsRender<StadiumPlan>(ModelProperty);

    public StadiumPlanModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    private static readonly string[] Sides = { "North", "South", "West", "East" };

    public override void Render(DrawingContext ctx)
    {
        if (Model is not { } m) return;
        var all = new Rect(Bounds.Size);
        if (all.Width < 50 || all.Height < 50) return;
        ctx.DrawRectangle(B("#4E9A52"), null, all, 12, 12);
        for (var i = 0; i < 40; i += 2) ctx.FillRectangle(B("#47904B"), new Rect(all.Width * i / 40, 0, all.Width / 40, all.Height));
        var rnd = new Random(5);
        for (var i = 0; i < 26; i++)
        {
            var x = i % 2 == 0 ? rnd.NextDouble() * all.Width * 0.05 + 6 : all.Width - 6 - rnd.NextDouble() * all.Width * 0.05;
            var y = 10 + rnd.NextDouble() * (all.Height - 20);
            var r = 5 + rnd.NextDouble() * 4;
            ctx.DrawEllipse(B(Color.FromArgb(50, 0, 0, 0)), null, new Point(x + 2, y + 3), r, r);
            ctx.DrawEllipse(B("#2F7A3A"), null, new Point(x, y), r, r);
            ctx.DrawEllipse(B("#43994C"), null, new Point(x - r * 0.3, y - r * 0.3), r * 0.55, r * 0.55);
        }

        // Vorplatz und Stadionkörper
        // Fester Seitenverhältnis-Rahmen (Spielfeld etwa 105 x 68 m plus Tribünen), mittig
        var fw = Math.Min(all.Width - 40, (all.Height - 44) * 1.75);
        var plaza = new Rect(all.Center.X - fw / 2, 22, fw, all.Height - 44);
        ctx.DrawRectangle(B("#D9CFB8"), null, plaza, plaza.Height * 0.16, plaza.Height * 0.16);
        var st = plaza.Deflate(new Thickness(18, 14));
        ctx.DrawRectangle(B(Color.FromArgb(60, 0, 0, 0)), null, st.Translate(new Vector(6, 7)), 14, 14);
        ctx.DrawRectangle(B("#8D97A8"), null, st, 14, 14);

        var max = Math.Max(1, Sides.Max(s => m.Capacities.GetValueOrDefault(s)));
        var unit = Math.Min(st.Width, st.Height);
        double D(string s) => unit * (0.07 + 0.12 * m.Capacities.GetValueOrDefault(s) / max);
        double dn = D("North"), ds = D("South"), dw = D("West"), de = D("East");
        var pitch = new Rect(st.X + dw + 6, st.Y + dn + 6, st.Width - dw - de - 12, st.Height - dn - ds - 12);

        Stand(ctx, new Rect(pitch.Left + 4, st.Y, pitch.Width - 8, dn), true, false, m.Seat);
        Stand(ctx, new Rect(pitch.Left + 4, st.Bottom - ds, pitch.Width - 8, ds), true, true, m.Seat);
        Stand(ctx, new Rect(st.X, pitch.Top + 4, dw, pitch.Height - 8), false, false, m.Seat);
        Stand(ctx, new Rect(st.Right - de, pitch.Top + 4, de, pitch.Height - 8), false, true, m.Seat);
        if (m.Boxes.Contains("West"))
            ctx.FillRectangle(B("#9FD3F5"), new Rect(st.X + dw * 0.45, pitch.Top + pitch.Height * 0.22, 4, pitch.Height * 0.56));

        // Spielfeld
        ctx.DrawRectangle(B("#3E8E5A"), null, pitch, 4, 4);
        for (var i = 0; i < 14; i += 2) ctx.FillRectangle(B(Color.FromArgb(20, 0, 0, 0)), new Rect(pitch.X + i * pitch.Width / 14, pitch.Y, pitch.Width / 14, pitch.Height));
        IsoBuildings.FieldLines(ctx, pitch, true, 1.6, Color.Parse("#E8F6EC"));

        // Glasdach über überdachten Tribünen
        foreach (var side in m.Roofed)
        {
            var r = side switch
            {
                "North" => new Rect(st.X, st.Y, st.Width, dn + 4),
                "South" => new Rect(st.X, st.Bottom - ds - 4, st.Width, ds + 4),
                "West" => new Rect(st.X, st.Y, dw + 4, st.Height),
                _ => new Rect(st.Right - de - 4, st.Y, de + 4, st.Height),
            };
            ctx.DrawRectangle(B(Color.FromArgb(46, 230, 238, 248)), new Pen(B(Color.FromArgb(120, 255, 255, 255)), 1), r, 6, 6);
        }

        // Flutlicht
        if (m.Floodlight > 0)
            foreach (var p in new[] { st.TopLeft, st.TopRight, st.BottomLeft, st.BottomRight })
            {
                var c = new Point(p.X + (p.X < st.Center.X ? 12 : -12), p.Y + (p.Y < st.Center.Y ? 12 : -12));
                ctx.DrawEllipse(B(Color.FromArgb(60, 255, 243, 176)), null, c, 10 + 4 * m.Floodlight, 10 + 4 * m.Floodlight);
                ctx.DrawEllipse(B("#3A4256"), new Pen(B("#FFF3B0"), 1.5), c, 5, 5);
            }

        // Beschriftung der Tribünen
        if (m.Labels.TryGetValue("North", out var n)) Label(ctx, new Point(st.Center.X, st.Y - 6), n);
        if (m.Labels.TryGetValue("South", out var so)) Label(ctx, new Point(st.Center.X, st.Bottom + 6), so);
        if (m.Labels.TryGetValue("West", out var w)) Label(ctx, new Point(st.X + dw / 2 + 18, st.Center.Y), w);
        if (m.Labels.TryGetValue("East", out var e)) Label(ctx, new Point(st.Right - de / 2 - 18, st.Center.Y), e);
    }

    /// <summary>Tribüne als Block mit Sitzreihen; die oberste Reihe liegt außen.</summary>
    private static void Stand(DrawingContext ctx, Rect r, bool horizontal, bool far, Color seat)
    {
        ctx.DrawRectangle(B("#5D6779"), null, r, 3, 3);
        var depth = horizontal ? r.Height : r.Width;
        var rows = Math.Max(3, (int)(depth / 5));
        for (var i = 0; i < rows; i++)
        {
            var col = i % 5 == 4 ? Color.Parse("#E8EDF7") : seat;
            var a = depth * i / rows + 0.6;
            var size = depth / rows - 1.4;
            var row = horizontal ? new Rect(r.X + 2, r.Y + a, r.Width - 4, size) : new Rect(r.X + a, r.Y + 2, size, r.Height - 4);
            ctx.FillRectangle(B(Shade(col, far ? 0.92 + 0.08 * i / rows : 1 - 0.08 * i / rows)), row);
        }
        var pen = new Pen(B("#5D6779"), 2);
        for (var j = 1; j < 6; j++)
        {
            if (horizontal) { var x = r.X + r.Width * j / 6; ctx.DrawLine(pen, new Point(x, r.Y), new Point(x, r.Bottom)); }
            else { var y = r.Y + r.Height * j / 6; ctx.DrawLine(pen, new Point(r.X, y), new Point(r.Right, y)); }
        }
    }
}
