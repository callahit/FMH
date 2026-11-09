using Avalonia;
using Avalonia.Media;
using static Fmh.Desktop.Controls.IsoArt;

namespace Fmh.Desktop.Controls;

/// <summary>Gebäude der Iso-Karte nach Zeichenart (Haus, Halle, Platz, Parkplatz, Solar …).</summary>
public static class IsoBuildings
{
    private static readonly Color Wall = Color.Parse("#EEE8DC");
    private static readonly Color WallSide = Color.Parse("#D3CAB8");
    private static readonly Color Glass = Color.Parse("#9FC6E8");
    private const double Inset = 0.1;

    /// <summary>Zeichnet ein Gebäude und gibt den Punkt für Symbol und Beschriftung zurück.</summary>
    public static Point Draw(DrawingContext ctx, Vector o, IsoBuildingVisual b)
    {
        var (light, dark, icon) = Category(b.Category);
        double x = b.X + Inset, y = b.Y + Inset, w = b.Width - 2 * Inset, h = b.Height - 2 * Inset;
        var kind = KindOf(b.Type, b.Width, b.Height);
        if (b.UnderConstruction) return Construction(ctx, o, x, y, w, h, light);
        var top = kind switch
        {
            IsoArtKind.House => House(ctx, o, x, y, w, h, b.Level, light, dark),
            IsoArtKind.Hall => Hall(ctx, o, x, y, w, h, b.Level, light, dark),
            IsoArtKind.Pitch => Pitch(ctx, o, x, y, w, h, Color.Parse("#3E8E5A"), true),
            IsoArtKind.Turf => Pitch(ctx, o, x, y, w, h, Color.Parse("#2E8C78"), false),
            IsoArtKind.Court => Court(ctx, o, x, y, w, h),
            IsoArtKind.Track => Track(ctx, o, x, y, w, h),
            IsoArtKind.Parking => Parking(ctx, o, x, y, w, h, b.Id),
            IsoArtKind.Garage => Garage(ctx, o, x, y, w, h, b.Level),
            IsoArtKind.Road => Road(ctx, o, x, y, w, h),
            IsoArtKind.Solar => Solar(ctx, o, x, y, w, h),
            IsoArtKind.Park => Park(ctx, o, x, y, w, h),
            IsoArtKind.Pool => Pool(ctx, o, x, y, w, h, b.Type == "icePool"),
            IsoArtKind.Garden => Garden(ctx, o, x, y, w, h, b.Id),
            IsoArtKind.Tower => Tower(ctx, o, x, y, w, h, b.Type == "ledTower"),
            IsoArtKind.Tank => Tank(ctx, o, x, y, w, h, b.Type == "chp"),
            IsoArtKind.Monument => Monument(ctx, o, x, y, w, h, b.Type == "chapel"),
            _ => House(ctx, o, x, y, w, h, b.Level, light, dark),
        };
        Badge(ctx, top, icon, dark, 7.5);
        return top;
    }

    private static int Floors(int level) => level <= 2 ? 1 : level <= 4 ? 2 : 3;

    private static Point House(DrawingContext ctx, Vector o, double x, double y, double w, double h, int level, Color light, Color dark)
    {
        const double floorH = 11;
        var z = Floors(level) * floorH + 2;
        Shadow(ctx, o, x, y, w, h, z);
        Box(ctx, o, x, y, w, h, 0, z, WallSide, Wall, null);
        WindowRows(ctx, o, x, y, w, h, 0, Floors(level), floorH, Glass);
        var rise = 7 + 4 * Math.Min(w, h);
        GableRoof(ctx, o, x, y, w, h, z, rise, light, dark, Wall);
        return P(o, x + w / 2, y + h / 2, z + rise + 12);
    }

    private static Point Hall(DrawingContext ctx, Vector o, double x, double y, double w, double h, int level, Color light, Color dark)
    {
        var z = 18 + Floors(level) * 5;
        Shadow(ctx, o, x, y, w, h, z);
        Box(ctx, o, x, y, w, h, 0, z, Color.Parse("#B9C1CE"), Color.Parse("#D5DBE4"), null);
        WindowRows(ctx, o, x, y, w, h, 0, 1, z * 0.9, Glass);
        // Flachdach mit Attika und Oberlichtern in der Kategoriefarbe
        Box(ctx, o, x, y, w, h, z, z + 3, Shade(dark, 0.9), dark, light);
        var g = Ground(o, z + 3);
        using (ctx.PushTransform(g))
        {
            for (var i = 1; i <= 3; i++)
                ctx.FillRectangle(B(Alpha(Colors.White, 70)), new Rect(x + w * i / 4 - 0.06, y + 0.25, 0.12, h - 0.5));
        }
        return P(o, x + w / 2, y + h / 2, z + 16);
    }

    private static Point Pitch(DrawingContext ctx, Vector o, double x, double y, double w, double h, Color grass, bool stripes)
    {
        using (ctx.PushTransform(Ground(o)))
        {
            ctx.FillRectangle(B(grass), new Rect(x, y, w, h));
            var along = w >= h;
            const int n = 8;
            if (stripes)
                for (var i = 0; i < n; i += 2)
                    ctx.FillRectangle(B(Color.FromArgb(20, 0, 0, 0)), along ? new Rect(x + i * w / n, y, w / n, h) : new Rect(x, y + i * h / n, w, h / n));
            FieldLines(ctx, new Rect(x, y, w, h), along, 0.035, Color.Parse("#E8F6EC"));
        }
        Goals(ctx, o, new Rect(x, y, w, h), w >= h);
        return P(o, x + w / 2, y + h / 2, 14);
    }

    /// <summary>Spielfeldlinien in Rasterkoordinaten (Mittellinie quer zur langen Seite).</summary>
    public static void FieldLines(DrawingContext ctx, Rect r, bool along, double thickness, Color line)
    {
        var pen = new Pen(B(line), thickness);
        var m = Math.Min(r.Width, r.Height) * 0.1;
        var f = r.Deflate(m);
        ctx.DrawRectangle(null, pen, f);
        var c = f.Center;
        if (along)
        {
            ctx.DrawLine(pen, new Point(c.X, f.Top), new Point(c.X, f.Bottom));
            var bw = f.Width * 0.15; var bh = f.Height * 0.55;
            ctx.DrawRectangle(null, pen, new Rect(f.Left, c.Y - bh / 2, bw, bh));
            ctx.DrawRectangle(null, pen, new Rect(f.Right - bw, c.Y - bh / 2, bw, bh));
        }
        else
        {
            ctx.DrawLine(pen, new Point(f.Left, c.Y), new Point(f.Right, c.Y));
            var bw = f.Width * 0.55; var bh = f.Height * 0.15;
            ctx.DrawRectangle(null, pen, new Rect(c.X - bw / 2, f.Top, bw, bh));
            ctx.DrawRectangle(null, pen, new Rect(c.X - bw / 2, f.Bottom - bh, bw, bh));
        }
        var rr = Math.Min(f.Width, f.Height) * 0.2;
        ctx.DrawEllipse(null, pen, c, rr, rr);
    }

    private static void Goals(DrawingContext ctx, Vector o, Rect r, bool along)
    {
        var pen = new Pen(Brushes.White, 1.4);
        var m = Math.Min(r.Width, r.Height) * 0.1;
        var f = r.Deflate(m);
        var gw = Math.Min(f.Width, f.Height) * 0.12;
        IEnumerable<(Point a, Point b)> mouths = along
            ? new[] { (new Point(f.Left, f.Center.Y - gw), new Point(f.Left, f.Center.Y + gw)), (new Point(f.Right, f.Center.Y - gw), new Point(f.Right, f.Center.Y + gw)) }
            : new[] { (new Point(f.Center.X - gw, f.Top), new Point(f.Center.X + gw, f.Top)), (new Point(f.Center.X - gw, f.Bottom), new Point(f.Center.X + gw, f.Bottom)) };
        foreach (var (a, b) in mouths)
        {
            var pa = P(o, a.X, a.Y); var pb = P(o, b.X, b.Y);
            ctx.DrawLine(pen, pa, pa - new Vector(0, 5));
            ctx.DrawLine(pen, pb, pb - new Vector(0, 5));
            ctx.DrawLine(pen, pa - new Vector(0, 5), pb - new Vector(0, 5));
        }
    }

    private static Point Court(DrawingContext ctx, Vector o, double x, double y, double w, double h)
    {
        Pitch(ctx, o, x, y, w, h, Color.Parse("#2D7A52"), false);
        // Bandenzaun
        var pen = new Pen(B(Alpha(Colors.White, 150)), 1);
        foreach (var (ax, ay, bx, by) in new[] { (x, y + h, x + w, y + h), (x + w, y, x + w, y + h) })
        {
            ctx.DrawLine(pen, P(o, ax, ay, 7), P(o, bx, by, 7));
            for (var i = 0; i <= 4; i++) ctx.DrawLine(pen, P(o, ax + (bx - ax) * i / 4, ay + (by - ay) * i / 4), P(o, ax + (bx - ax) * i / 4, ay + (by - ay) * i / 4, 7));
        }
        return P(o, x + w / 2, y + h / 2, 18);
    }

    private static Point Track(DrawingContext ctx, Vector o, double x, double y, double w, double h)
    {
        using (ctx.PushTransform(Ground(o)))
        {
            ctx.FillRectangle(B("#C8573F"), new Rect(x, y, w, h));
            var pen = new Pen(B(Alpha(Colors.White, 200)), 0.025);
            for (var i = 1; i < 5; i++) ctx.DrawLine(pen, new Point(x, y + h * i / 5), new Point(x + w, y + h * i / 5));
            ctx.DrawLine(new Pen(Brushes.White, 0.05), new Point(x + w * 0.85, y), new Point(x + w * 0.85, y + h));
        }
        return P(o, x + w / 2, y + h / 2, 12);
    }

    private static readonly Color[] CarColors = { Color.Parse("#E0565C"), Color.Parse("#E8EDF7"), Color.Parse("#3F7FD9"), Color.Parse("#F5C04E"), Color.Parse("#2A2F3A"), Color.Parse("#9AA3B2") };

    private static Point Parking(DrawingContext ctx, Vector o, double x, double y, double w, double h, int seed)
    {
        using (ctx.PushTransform(Ground(o)))
        {
            ctx.FillRectangle(B("#5B6170"), new Rect(x, y, w, h));
            var pen = new Pen(B(Alpha(Colors.White, 160)), 0.025);
            var along = w >= h;
            var len = along ? w : h;
            var bays = Math.Max(2, (int)(len * 4));
            for (var i = 0; i <= bays; i++)
            {
                var t = i / (double)bays * len;
                if (along)
                {
                    ctx.DrawLine(pen, new Point(x + t, y), new Point(x + t, y + h * 0.38));
                    ctx.DrawLine(pen, new Point(x + t, y + h * 0.62), new Point(x + t, y + h));
                }
                else
                {
                    ctx.DrawLine(pen, new Point(x, y + t), new Point(x + w * 0.38, y + t));
                    ctx.DrawLine(pen, new Point(x + w * 0.62, y + t), new Point(x + w, y + t));
                }
            }
        }
        // Autos als kleine Quader
        var rnd = new Random(seed);
        var along2 = w >= h;
        var len2 = along2 ? w : h;
        var bays2 = Math.Max(2, (int)(len2 * 4));
        for (var row = 0; row < 2; row++)
            for (var i = 0; i < bays2; i++)
            {
                if (rnd.NextDouble() > 0.6) continue;
                var c = CarColors[rnd.Next(CarColors.Length)];
                var t = (i + 0.18) / bays2 * len2;
                var s = len2 / bays2 * 0.64;
                double cx, cy, cw, ch;
                if (along2) { cx = x + t; cy = row == 0 ? y + 0.03 : y + h * 0.65; cw = s; ch = h * 0.3; }
                else { cy = y + t; cx = row == 0 ? x + 0.03 : x + w * 0.65; ch = s; cw = w * 0.3; }
                Box(ctx, o, cx, cy, cw, ch, 0, 4, Shade(c, 0.7), Shade(c, 0.85), c);
            }
        return P(o, x + w / 2, y + h / 2, 16);
    }

    private static Point Garage(DrawingContext ctx, Vector o, double x, double y, double w, double h, int level)
    {
        var decks = 1 + Math.Min(3, level);
        const double dh = 8;
        Shadow(ctx, o, x, y, w, h, decks * dh);
        for (var i = 0; i < decks; i++)
        {
            Box(ctx, o, x, y, w, h, i * dh, i * dh + 2.5, Color.Parse("#9AA3B2"), Color.Parse("#B9C1CE"), null);
            Box(ctx, o, x + 0.05, y + 0.05, w - 0.1, h - 0.1, i * dh + 2.5, (i + 1) * dh, Color.Parse("#3E4554"), Color.Parse("#4A5263"), null);
        }
        Box(ctx, o, x, y, w, h, decks * dh, decks * dh + 2.5, Color.Parse("#9AA3B2"), Color.Parse("#B9C1CE"), Color.Parse("#6B7487"));
        return P(o, x + w / 2, y + h / 2, decks * dh + 14);
    }

    private static Point Road(DrawingContext ctx, Vector o, double x, double y, double w, double h)
    {
        using (ctx.PushTransform(Ground(o)))
        {
            ctx.FillRectangle(B("#4F5563"), new Rect(x, y, w, h));
            var pen = new Pen(B(Alpha(Colors.White, 200)), 0.04, new DashStyle(new double[] { 3, 3 }, 0));
            if (w >= h) ctx.DrawLine(pen, new Point(x, y + h / 2), new Point(x + w, y + h / 2));
            else ctx.DrawLine(pen, new Point(x + w / 2, y), new Point(x + w / 2, y + h));
        }
        return P(o, x + w / 2, y + h / 2, 12);
    }

    private static Point Solar(DrawingContext ctx, Vector o, double x, double y, double w, double h)
    {
        using (ctx.PushTransform(Ground(o)))
            ctx.FillRectangle(B("#7FA36E"), new Rect(x, y, w, h));
        // Schräge Paneelreihen
        const int rows = 3;
        for (var r = 0; r < rows; r++)
        {
            var y0 = y + 0.08 + r * (h - 0.16) / rows;
            var y1 = y0 + (h - 0.16) / rows * 0.7;
            ctx.DrawGeometry(B("#1F3F78"), new Pen(B("#5B86C9"), 0.8),
                Poly(P(o, x + 0.08, y0, 7), P(o, x + w - 0.08, y0, 7), P(o, x + w - 0.08, y1, 2), P(o, x + 0.08, y1, 2)));
        }
        return P(o, x + w / 2, y + h / 2, 20);
    }

    private static Point Park(DrawingContext ctx, Vector o, double x, double y, double w, double h)
    {
        using (ctx.PushTransform(Ground(o)))
        {
            ctx.FillRectangle(B("#5CAA5F"), new Rect(x, y, w, h));
            var path = new StreamGeometry();
            using (var g = path.Open())
            {
                g.BeginFigure(new Point(x, y + h * 0.7), false);
                g.CubicBezierTo(new Point(x + w * 0.4, y + h * 0.4), new Point(x + w * 0.6, y + h * 0.95), new Point(x + w, y + h * 0.55));
            }
            ctx.DrawGeometry(null, new Pen(B("#D9CFB8"), 0.12), path);
        }
        foreach (var (dx, dy, s) in new[] { (0.25, 0.3, 7.0), (0.7, 0.25, 8.0), (0.55, 0.6, 6.0) })
            Tree(ctx, P(o, x + w * dx, y + h * dy), s);
        return P(o, x + w / 2, y + h / 2, 26);
    }

    private static Point Pool(DrawingContext ctx, Vector o, double x, double y, double w, double h, bool ice)
    {
        Box(ctx, o, x, y, w, h, 0, 3, Color.Parse("#C9CFD9"), Color.Parse("#DDE2EA"), Color.Parse("#EEF1F5"));
        using (ctx.PushTransform(Ground(o, 3)))
        {
            var water = new Rect(x + 0.12, y + 0.12, w - 0.24, h - 0.24);
            ctx.FillRectangle(B(ice ? "#A9DDF2" : "#3BA7E0"), water);
            var pen = new Pen(B(Alpha(Colors.White, 160)), 0.025, new DashStyle(new double[] { 2, 2 }, 0));
            for (var i = 1; i < 3; i++) ctx.DrawLine(pen, new Point(water.Left, water.Top + water.Height * i / 3), new Point(water.Right, water.Top + water.Height * i / 3));
        }
        return P(o, x + w / 2, y + h / 2, 16);
    }

    private static Point Garden(DrawingContext ctx, Vector o, double x, double y, double w, double h, int seed)
    {
        using (ctx.PushTransform(Ground(o)))
            ctx.FillRectangle(B("#C8B690"), new Rect(x, y, w, h));
        var rnd = new Random(seed);
        var n = Math.Max(3, (int)(w * h * 3));
        for (var i = 0; i < n; i++)
        {
            var px = x + 0.15 + rnd.NextDouble() * (w - 0.3);
            var py = y + 0.15 + rnd.NextDouble() * (h - 0.3);
            var foot = P(o, px, py);
            ctx.DrawLine(new Pen(B("#5A4632"), 1.2), foot, foot - new Vector(0, 9));
            var col = i % 2 == 0 ? Color.Parse("#F08A3C") : Color.Parse("#F3EFE6");
            ctx.DrawEllipse(B(Color.FromArgb(40, 0, 0, 0)), null, foot + new Vector(3, 0), 7, 3);
            ctx.DrawEllipse(B(col), new Pen(B(Shade(col, 0.8)), 0.8), foot - new Vector(0, 10), 8, 4);
        }
        return P(o, x + w / 2, y + h / 2, 26);
    }

    private static Point Tower(DrawingContext ctx, Vector o, double x, double y, double w, double h, bool led)
    {
        var foot = P(o, x + w / 2, y + h / 2);
        ctx.DrawEllipse(B(Color.FromArgb(50, 0, 0, 0)), null, foot + new Vector(6, 0), 10, 4);
        ctx.DrawLine(new Pen(B("#5B6170"), 3), foot, foot - new Vector(0, 40));
        if (led)
        {
            var top = foot - new Vector(0, 40);
            ctx.DrawRectangle(B("#141B2B"), new Pen(B("#9AA3B2"), 1), new Rect(top.X - 13, top.Y - 16, 26, 16), 2, 2);
            ctx.FillRectangle(B("#3DDC97"), new Rect(top.X - 10, top.Y - 13, 20, 4));
            ctx.FillRectangle(B("#F5C04E"), new Rect(top.X - 10, top.Y - 7, 13, 3));
            return top - new Vector(0, 28);
        }
        var head = foot - new Vector(0, 44);
        ctx.DrawEllipse(B(Color.FromArgb(70, 255, 243, 176)), null, head, 14, 9);
        ctx.DrawRectangle(B("#3A4256"), new Pen(B("#FFF3B0"), 1), new Rect(head.X - 7, head.Y - 4, 14, 7), 2, 2);
        return head - new Vector(0, 22);
    }

    private static Point Tank(DrawingContext ctx, Vector o, double x, double y, double w, double h, bool chp)
    {
        if (chp)
        {
            var top = House(ctx, o, x, y, w * 0.8, h * 0.8, 1, Color.Parse("#2CB5C8"), Color.Parse("#1E91A3"));
            var c = P(o, x + w * 0.85, y + h * 0.2);
            ctx.DrawRectangle(B("#8D97A8"), null, new Rect(c.X - 3, c.Y - 36, 6, 36));
            ctx.DrawEllipse(B(Color.FromArgb(90, 230, 235, 240)), null, new Point(c.X + 4, c.Y - 44), 7, 5);
            return top;
        }
        var foot = P(o, x + w / 2, y + h / 2);
        var r = Hw * Math.Min(w, h) * 0.55;
        ctx.DrawEllipse(B(Color.FromArgb(50, 0, 0, 0)), null, foot + new Vector(5, 2), r, r / 2);
        ctx.DrawRectangle(B("#9AA3B2"), null, new Rect(foot.X - r, foot.Y - 22, 2 * r, 22));
        ctx.DrawEllipse(B("#9AA3B2"), null, foot, r, r / 2);
        ctx.DrawEllipse(B("#C9CFD9"), new Pen(B("#7D8698"), 1), foot - new Vector(0, 22), r, r / 2);
        return foot - new Vector(0, 36);
    }

    private static Point Monument(DrawingContext ctx, Vector o, double x, double y, double w, double h, bool chapel)
    {
        if (chapel)
        {
            var top = House(ctx, o, x + w * 0.15, y + h * 0.15, w * 0.7, h * 0.7, 1, Color.Parse("#B9C1CE"), Color.Parse("#8D97A8"));
            ctx.DrawLine(new Pen(Brushes.White, 1.6), top + new Vector(0, 2), top + new Vector(0, -8));
            ctx.DrawLine(new Pen(Brushes.White, 1.6), top + new Vector(-3, -4), top + new Vector(3, -4));
            return top - new Vector(0, 8);
        }
        using (ctx.PushTransform(Ground(o)))
            ctx.FillRectangle(B("#D9CFB8"), new Rect(x, y, w, h));
        var cx = x + w / 2; var cy = y + h / 2;
        Box(ctx, o, cx - 0.15, cy - 0.15, 0.3, 0.3, 0, 8, Color.Parse("#9AA3B2"), Color.Parse("#B9C1CE"), Color.Parse("#C9CFD9"));
        var foot = P(o, cx, cy, 8);
        ctx.DrawRectangle(B("#B08D3C"), null, new Rect(foot.X - 2.5, foot.Y - 14, 5, 14), 2, 2);
        ctx.DrawEllipse(B("#B08D3C"), null, foot - new Vector(0, 17), 3.2, 3.2);
        return foot - new Vector(0, 30);
    }

    /// <summary>Baustelle: Schraffur, Gerüst, Kran.</summary>
    private static Point Construction(DrawingContext ctx, Vector o, double x, double y, double w, double h, Color light)
    {
        using (ctx.PushTransform(Ground(o)))
        {
            ctx.FillRectangle(B("#B59E78"), new Rect(x, y, w, h));
            var pen = new Pen(B(Color.FromArgb(150, 245, 192, 78)), 0.07);
            using (ctx.PushClip(new Rect(x, y, w, h)))
                for (var t = -h; t < w; t += 0.3) ctx.DrawLine(pen, new Point(x + t, y + h), new Point(x + t + h, y));
        }
        Box(ctx, o, x + 0.1, y + 0.1, w - 0.2, h - 0.2, 0, 8, Alpha(Shade(light, 0.7), 200), Alpha(light, 200), null);
        var scaffold = new Pen(B("#F5C04E"), 1);
        foreach (var (px, py) in new[] { (x, y + h), (x + w, y + h), (x + w, y) })
            ctx.DrawLine(scaffold, P(o, px, py), P(o, px, py, 16));
        ctx.DrawLine(scaffold, P(o, x, y + h, 16), P(o, x + w, y + h, 16));
        ctx.DrawLine(scaffold, P(o, x + w, y + h, 16), P(o, x + w, y, 16));
        // Kran
        var mast = P(o, x + 0.2, y + 0.2);
        ctx.DrawLine(new Pen(B("#F5C04E"), 2), mast, mast - new Vector(0, 46));
        ctx.DrawLine(new Pen(B("#F5C04E"), 2), mast - new Vector(14, 44), mast - new Vector(-30, 44));
        ctx.DrawLine(new Pen(B("#E8EDF7"), 0.8), mast - new Vector(-24, 44), mast - new Vector(-24, 22));
        return P(o, x + w / 2, y + h / 2, 30);
    }
}
