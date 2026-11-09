using Avalonia;
using Avalonia.Media;
using static Fmh.Desktop.Controls.IsoArt;

namespace Fmh.Desktop.Controls;

/// <summary>
/// Stadion auf der Iso-Karte: Spielfeld, vier Tribünen (Tiefe und Höhe nach Plätzen, Sitze in der Vereinsfarbe),
/// Glasdach über überdachten Tribünen, Logenband an der Haupttribüne, Flutlichtmasten nach Ausbaustufe.
/// Hintere Tribünen (Nord, West) zeigen die Ränge, vordere (Süd, Ost) ihre Außenfassade.
/// </summary>
public static class IsoStadium
{
    private static readonly Color Concrete = Color.Parse("#8D97A8");
    private static readonly Color Facade = Color.Parse("#C9D0DB");
    private static readonly Color FacadeSide = Color.Parse("#AAB3C1");

    public static Point Draw(DrawingContext ctx, Vector o, IsoMapState s)
    {
        double sx = s.StadiumX + 0.05, sy = s.StadiumY + 0.05, sw = s.StadiumWidth - 0.1, sh = s.StadiumHeight - 0.1;
        int Cap(string side) => s.StandCapacities.GetValueOrDefault(side, 0);
        var max = Math.Max(1, new[] { "North", "South", "West", "East" }.Max(Cap));
        double Depth(string side) => Math.Min(sh, sw) * (0.14 + 0.16 * Cap(side) / max);
        double Height(string side) => 8 + 26 * Cap(side) / max;
        double dN = Depth("North"), dS = Depth("South"), dW = Depth("West"), dE = Depth("East");
        double ix = sx + dW, iy = sy + dN, iw = sw - dW - dE, ih = sh - dN - dS;
        var seat = s.SeatColor;
        var hi = new[] { Height("North"), Height("South"), Height("West"), Height("East") }.Max();

        Shadow(ctx, o, sx, sy, sw, sh, hi);
        using (ctx.PushTransform(Ground(o)))
            ctx.FillRectangle(B(Concrete), new Rect(sx, sy, sw, sh));

        // Hintere Tribünen: Ränge sichtbar
        Slope(ctx, o, ix, sy, ix + iw, iy, Height("North"), 2, horizontal: true, seat);
        EndFace(ctx, o, ix + iw, sy, iy, Height("North"), alongY: true);
        Slope(ctx, o, sx, iy, ix, iy + ih, Height("West"), 2, horizontal: false, seat);
        EndFace(ctx, o, iy + ih, sx, ix, Height("West"), alongY: false);
        // Logen: Glasband auf den Rängen der hinteren Tribünen (vordere siehe Fassade unten)
        void BackBoxes(string side)
        {
            if (!s.BoxSides.Contains(side)) return;
            var z = Height(side) * 0.6;
            var pen = new Pen(B("#E8F6FF"), 0.8);
            if (side == "West")
            {
                var bx = sx + dW * 0.45;
                ctx.DrawGeometry(B("#9FD3F5"), pen, Poly(P(o, bx, iy + ih * 0.2, z), P(o, bx, iy + ih * 0.8, z), P(o, bx, iy + ih * 0.8, z + 4), P(o, bx, iy + ih * 0.2, z + 4)));
            }
            else
            {
                var by = sy + dN * 0.45;
                ctx.DrawGeometry(B("#9FD3F5"), pen, Poly(P(o, ix + iw * 0.2, by, z), P(o, ix + iw * 0.8, by, z), P(o, ix + iw * 0.8, by, z + 4), P(o, ix + iw * 0.2, by, z + 4)));
            }
        }
        BackBoxes("North");
        BackBoxes("West");

        // Spielfeld
        using (ctx.PushTransform(Ground(o, 1)))
        {
            var pitch = new Rect(ix, iy, iw, ih);
            ctx.FillRectangle(B("#3E8E5A"), pitch);
            const int n = 12;
            for (var i = 0; i < n; i += 2) ctx.FillRectangle(B(Color.FromArgb(20, 0, 0, 0)), new Rect(ix + i * iw / n, iy, iw / n, ih));
            IsoBuildings.FieldLines(ctx, pitch, iw >= ih, 0.03, Color.Parse("#E8F6EC"));
        }

        // Vordere Tribünen: Fassade sichtbar
        Slope(ctx, o, ix, iy + ih, ix + iw, sy + sh, Height("South"), 2, horizontal: true, seat, rising: true);
        Front(ctx, o, ix, sy + sh, ix + iw, sy + sh, Height("South"), Facade);
        if (s.BoxSides.Contains("South")) FrontBoxes(ctx, o, ix + iw * 0.2, sy + sh, ix + iw * 0.8, sy + sh, Height("South") * 0.55);
        EndFace(ctx, o, ix + iw, iy + ih, sy + sh, Height("South"), alongY: true, rising: true);
        Slope(ctx, o, ix + iw, iy, sx + sw, iy + ih, Height("East"), 2, horizontal: false, seat, rising: true);
        Front(ctx, o, sx + sw, iy, sx + sw, iy + ih, Height("East"), FacadeSide);
        if (s.BoxSides.Contains("East")) FrontBoxes(ctx, o, sx + sw, iy + ih * 0.2, sx + sw, iy + ih * 0.8, Height("East") * 0.55);
        EndFace(ctx, o, iy + ih, ix + iw, sx + sw, Height("East"), alongY: false, rising: true);

        // Dach: ein Glasring über den überdachten Tribünen, getragen von Stützen an den Ecken
        if (s.RoofSides.Count > 0)
        {
            var z = hi + 6;
            var outer = new[] { P(o, sx, sy, z), P(o, sx + sw, sy, z), P(o, sx + sw, sy + sh, z), P(o, sx, sy + sh, z) };
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.SetFillRule(FillRule.EvenOdd);
                g.BeginFigure(outer[0], true); foreach (var q in outer.Skip(1)) g.LineTo(q); g.EndFigure(true);
                // Offene Seiten (ohne Dach) bleiben frei: das innere Loch reicht dort bis zum Außenrand
                double l = s.RoofSides.Contains("West") ? ix : sx, r = s.RoofSides.Contains("East") ? ix + iw : sx + sw;
                double tp = s.RoofSides.Contains("North") ? iy : sy, bt = s.RoofSides.Contains("South") ? iy + ih : sy + sh;
                g.BeginFigure(P(o, l, tp, z), true); g.LineTo(P(o, r, tp, z)); g.LineTo(P(o, r, bt, z)); g.LineTo(P(o, l, bt, z)); g.EndFigure(true);
            }
            foreach (var (cx, cy) in new[] { (sx + 0.1, sy + sh - 0.1), (sx + sw - 0.1, sy + sh - 0.1), (sx + sw - 0.1, sy + 0.1) })
                ctx.DrawLine(new Pen(B("#E8EDF7"), 2), P(o, cx, cy), P(o, cx, cy, z));
            ctx.DrawGeometry(B(Color.FromArgb(60, 225, 235, 248)), new Pen(B(Color.FromArgb(190, 255, 255, 255)), 1.2), geo);
        }

        // Flutlichtmasten an den Ecken
        if (s.Floodlight > 0)
        {
            var mast = 46 + 8 * s.Floodlight;
            foreach (var (mx, my) in new[] { (sx + 0.08, sy + 0.08), (sx + sw - 0.08, sy + 0.08), (sx + 0.08, sy + sh - 0.08), (sx + sw - 0.08, sy + sh - 0.08) })
            {
                var foot = P(o, mx, my);
                var head = foot - new Vector(0, mast);
                ctx.DrawLine(new Pen(B("#5B6170"), 2.2), foot, head);
                ctx.DrawEllipse(B(Color.FromArgb(55, 255, 243, 176)), null, head, 16, 11);
                ctx.DrawRectangle(B("#3A4256"), new Pen(B("#FFF3B0"), 1), new Rect(head.X - 8, head.Y - 5, 16, 8), 2, 2);
                for (var i = 0; i < 3; i++) ctx.FillRectangle(B("#FFF3B0"), new Rect(head.X - 6 + i * 4.5, head.Y - 3, 3, 4));
            }
        }
        return P(o, sx + sw / 2, sy + sh / 2, hi + 26);
    }

    /// <summary>Tribünenränge zwischen (x0,y0) und (x1,y1); steigen nach außen an.</summary>
    private static void Slope(DrawingContext ctx, Vector o, double x0, double y0, double x1, double y1, double high, double low, bool horizontal, Color seat, bool rising = false)
    {
        // horizontal: Tribüne entlang x (Nord/Süd), Höhe ändert sich mit y; sonst entlang y (West/Ost), Höhe mit x
        Point Q(double x, double y)
        {
            double t = horizontal ? (y - y0) / (y1 - y0) : (x - x0) / (x1 - x0);
            var z = rising ? low + (high - low) * t : high - (high - low) * t;
            return P(o, x, y, z);
        }
        ctx.DrawGeometry(B(Shade(Concrete, 0.8)), null, Poly(Q(x0, y0), Q(x1, y0), Q(x1, y1), Q(x0, y1)));
        var rows = Math.Max(3, (int)(high / 3.2));
        for (var i = 0; i < rows; i++)
        {
            double a = i / (double)rows, b = (i + 0.72) / rows;
            var col = i % 5 == 4 ? Color.Parse("#E8EDF7") : seat;
            if (horizontal)
            {
                double ya = y0 + (y1 - y0) * a, yb = y0 + (y1 - y0) * b;
                ctx.DrawGeometry(B(col), null, Poly(Q(x0 + 0.03, ya), Q(x1 - 0.03, ya), Q(x1 - 0.03, yb), Q(x0 + 0.03, yb)));
            }
            else
            {
                double xa = x0 + (x1 - x0) * a, xb = x0 + (x1 - x0) * b;
                ctx.DrawGeometry(B(col), null, Poly(Q(xa, y0 + 0.03), Q(xb, y0 + 0.03), Q(xb, y1 - 0.03), Q(xa, y1 - 0.03)));
            }
        }
        // Treppenaufgänge
        var pen = new Pen(B(Shade(Concrete, 0.7)), 1.2);
        for (var j = 1; j < 5; j++)
        {
            if (horizontal) { var x = x0 + (x1 - x0) * j / 5; ctx.DrawLine(pen, Q(x, y0), Q(x, y1)); }
            else { var y = y0 + (y1 - y0) * j / 5; ctx.DrawLine(pen, Q(x0, y), Q(x1, y)); }
        }
    }

    /// <summary>Außenfassade einer vorderen Tribüne (Rechteck von a nach b, Höhe h) mit Pfeilern.</summary>
    private static void Front(DrawingContext ctx, Vector o, double ax, double ay, double bx, double by, double h, Color c)
    {
        ctx.DrawGeometry(B(c), null, Poly(P(o, ax, ay), P(o, bx, by), P(o, bx, by, h), P(o, ax, ay, h)));
        var pen = new Pen(B(Shade(c, 0.82)), 1.4);
        for (var i = 1; i < 8; i++)
        {
            double x = ax + (bx - ax) * i / 8, y = ay + (by - ay) * i / 8;
            ctx.DrawLine(pen, P(o, x, y), P(o, x, y, h));
        }
        ctx.DrawLine(new Pen(B(Alpha(Colors.White, 120)), 1.2), P(o, ax, ay, h), P(o, bx, by, h));
    }

    /// <summary>Logen einer vorderen Tribüne als Glasband auf der Fassade.</summary>
    private static void FrontBoxes(DrawingContext ctx, Vector o, double ax, double ay, double bx, double by, double z) =>
        ctx.DrawGeometry(B("#9FD3F5"), new Pen(B("#E8F6FF"), 0.8), Poly(P(o, ax, ay, z), P(o, bx, by, z), P(o, bx, by, z + 5), P(o, ax, ay, z + 5)));

    /// <summary>Seitliche Stirnfläche einer Tribüne (Dreieck/Trapez).</summary>
    private static void EndFace(DrawingContext ctx, Vector o, double fixedCoord, double from, double to, double high, bool alongY, bool rising = false)
    {
        double zf = rising ? 2 : high, zt = rising ? high : 2;
        var pts = alongY
            ? new[] { P(o, fixedCoord, from), P(o, fixedCoord, to), P(o, fixedCoord, to, zt), P(o, fixedCoord, from, zf) }
            : new[] { P(o, from, fixedCoord), P(o, to, fixedCoord), P(o, to, fixedCoord, zt), P(o, from, fixedCoord, zf) };
        ctx.DrawGeometry(B(FacadeSide), null, Poly(pts));
    }
}
