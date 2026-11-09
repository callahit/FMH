using Avalonia;
using Avalonia.Media;

namespace Fmh.Desktop.Controls;

/// <summary>Art der Zeichnung für ein Gebäude auf der Iso-Karte.</summary>
public enum IsoArtKind { House, Hall, Pitch, Turf, Court, Track, Parking, Garage, Road, Solar, Park, Pool, Garden, Tower, Tank, Monument }

/// <summary>
/// Zeichenbausteine der isometrischen Vereinsgelände-Karte. Flache Dinge (Rasen, Wege, Linien) werden in Rasterkoordinaten
/// mit der Iso-Matrix gezeichnet, Körper (Gebäude, Bäume, Masten) aus Flächen in Bildschirmkoordinaten.
/// </summary>
public static class IsoArt
{
    public const double Hw = IsoMapControl.TileW / 2.0;
    public const double Hh = IsoMapControl.TileH / 2.0;

    /// <summary>Kategorie: Dach hell, Dach dunkel, Symbol (24x24, Linie).</summary>
    public static (Color light, Color dark, Geometry icon) Category(string category) => category switch
    {
        "training" => (Color.Parse("#3F7FD9"), Color.Parse("#2C63B3"), Icon("M3,10 H14 C17.5,10 20,12.5 20,15.5 C20,18.5 17.5,21 14.5,21 C11.5,21 9,18.5 9,15.5 V14 H3 Z M12,10 V6 M15,6.5 L13.5,8.5 M9,6.5 L10.5,8.5")),
        "medical" => (Color.Parse("#E0565C"), Color.Parse("#BC3B42"), Icon("M9,4 H15 V9 H20 V15 H15 V20 H9 V15 H4 V9 H9 Z")),
        "youth" => (Color.Parse("#9A5BD6"), Color.Parse("#7A3FB5"), Icon("M12,3 L14.6,8.6 L20.6,9.2 L16,13.2 L17.4,19.2 L12,16 L6.6,19.2 L8,13.2 L3.4,9.2 L9.4,8.6 Z")),
        "fans" => (Color.Parse("#F08A3C"), Color.Parse("#CC6A22"), Icon("M6,21 V3 M6,4 H18 L15,8 L18,12 H6")),
        "commerce" => (Color.Parse("#E8B23A"), Color.Parse("#C2901F"), Icon("M5,8 H19 L18,21 H6 Z M9,8 V6.5 C9,3 15,3 15,6.5 V8")),
        "admin" => (Color.Parse("#6F8296"), Color.Parse("#55677A"), Icon("M3,8 H21 V19 H3 Z M9,8 V5 H15 V8 M3,13 H21")),
        "infrastructure" => (Color.Parse("#8B6B55"), Color.Parse("#6E523F"), Icon("M8,20 V4 H13 C17.5,4 17.5,12.5 13,12.5 H8")),
        "energy" => (Color.Parse("#2CB5C8"), Color.Parse("#1E91A3"), Icon("M13,2 L4,14 H11 L10,22 L20,9 H13 Z")),
        "teams" => (Color.Parse("#4CAF62"), Color.Parse("#36904B"), Icon("M12,3 L20,6 V12 C20,16.5 16.5,19.8 12,21 C7.5,19.8 4,16.5 4,12 V6 Z")),
        _ => (Color.Parse("#8A94A6"), Color.Parse("#6B7487"), Icon("M4,10 L12,4 L20,10 V20 H4 Z")),
    };

    private static readonly Dictionary<string, Geometry> Icons = new();

    private static Geometry Icon(string data)
    {
        if (!Icons.TryGetValue(data, out var g)) Icons[data] = g = StreamGeometry.Parse(data);
        return g;
    }

    /// <summary>Zeichenart je Gebäudetyp; alles Übrige ist ein Haus (bzw. eine Halle ab 2x2).</summary>
    public static IsoArtKind KindOf(string type, int w, int h) => type switch
    {
        "trainingPitch" or "youthPitches" or "reservePitch" or "goalkeeperArea" or "setPieceArea" => IsoArtKind.Pitch,
        "artificialPitch" => IsoArtKind.Turf,
        "soccerCourt" => IsoArtKind.Court,
        "sprintTrack" => IsoArtKind.Track,
        "parking" or "busParking" or "evCharging" or "bikeRacks" => IsoArtKind.Parking,
        "parkingGarage" => IsoArtKind.Garage,
        "accessRoad" or "shuttle" => IsoArtKind.Road,
        "solar" => IsoArtKind.Solar,
        "park" or "land" => IsoArtKind.Park,
        "pool" or "icePool" => IsoArtKind.Pool,
        "beerGarden" or "walkOfFame" => IsoArtKind.Garden,
        "ledTower" or "trainingFloodlight" => IsoArtKind.Tower,
        "cistern" or "chp" => IsoArtKind.Tank,
        "statue" or "chapel" => IsoArtKind.Monument,
        "smallStadium" or "trainingHall" or "sportsComplex" => IsoArtKind.Hall,
        _ => w >= 2 && h >= 2 ? IsoArtKind.Hall : IsoArtKind.House,
    };

    // ---------------------------------------------------------------- Grundlagen

    /// <summary>Bildschirmpunkt eines Rasterpunkts (x, y) in Höhe z (Pixel).</summary>
    public static Point P(Vector origin, double x, double y, double z = 0) => new((x - y) * Hw + origin.X, (x + y) * Hh + origin.Y - z);

    /// <summary>Iso-Matrix: zeichnet in Rasterkoordinaten (1 = eine Kachel).</summary>
    public static Matrix Ground(Vector origin, double z = 0) => new(Hw, Hh, -Hw, Hh, origin.X, origin.Y - z);

    public static StreamGeometry Poly(params Point[] pts)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(pts[0], true);
        for (var i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
        g.EndFigure(true);
        return geo;
    }

    public static Color Shade(Color c, double f) => Color.FromArgb(c.A, (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));
    public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    public static IBrush B(Color c) => new SolidColorBrush(c);
    public static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>Weicher Schlagschatten einer Grundfläche (nach rechts unten versetzt).</summary>
    public static void Shadow(DrawingContext ctx, Vector o, double x, double y, double w, double h, double height)
    {
        var d = Math.Min(0.6, height / 40.0);
        ctx.DrawGeometry(B(Color.FromArgb(46, 0, 0, 0)), null,
            Poly(P(o, x, y), P(o, x + w + d, y), P(o, x + w + d, y + h + d * 0.6), P(o, x, y + h + d * 0.6)));
    }

    /// <summary>Quader: Vorderseiten links (y+h) und rechts (x+w), Deckfläche. Gibt die Höhe der Oberkante zurück.</summary>
    public static void Box(DrawingContext ctx, Vector o, double x, double y, double w, double h, double z0, double z1, Color left, Color right, Color? top)
    {
        ctx.DrawGeometry(B(left), null, Poly(P(o, x, y + h, z0), P(o, x + w, y + h, z0), P(o, x + w, y + h, z1), P(o, x, y + h, z1)));
        ctx.DrawGeometry(B(right), null, Poly(P(o, x + w, y + h, z0), P(o, x + w, y, z0), P(o, x + w, y, z1), P(o, x + w, y + h, z1)));
        if (top is { } t) ctx.DrawGeometry(B(t), null, Poly(P(o, x, y, z1), P(o, x + w, y, z1), P(o, x + w, y + h, z1), P(o, x, y + h, z1)));
    }

    /// <summary>Fensterreihen auf beiden Vorderseiten.</summary>
    public static void WindowRows(DrawingContext ctx, Vector o, double x, double y, double w, double h, double z0, int floors, double floorH, Color glass)
    {
        var brush = B(glass);
        for (var f = 0; f < floors; f++)
        {
            var zb = z0 + f * floorH + floorH * 0.35;
            var zt = zb + floorH * 0.4;
            var nl = Math.Max(1, (int)(w * 3));
            for (var i = 0; i < nl; i++)
            {
                var a = x + (i + 0.3) / nl * w;
                var b = x + (i + 0.7) / nl * w;
                ctx.DrawGeometry(brush, null, Poly(P(o, a, y + h, zb), P(o, b, y + h, zb), P(o, b, y + h, zt), P(o, a, y + h, zt)));
            }
            var nr = Math.Max(1, (int)(h * 3));
            for (var i = 0; i < nr; i++)
            {
                if (f == 0 && i == nr / 2) continue; // Platz für die Tür
                var a = y + h - (i + 0.3) / nr * h;
                var b = y + h - (i + 0.7) / nr * h;
                ctx.DrawGeometry(brush, null, Poly(P(o, x + w, a, zb), P(o, x + w, b, zb), P(o, x + w, b, zt), P(o, x + w, a, zt)));
            }
        }
        // Tür mittig auf der rechten Vorderseite
        var dm = y + h / 2;
        ctx.DrawGeometry(B("#3B3F4A"), null, Poly(P(o, x + w, dm + 0.09, z0), P(o, x + w, dm - 0.09, z0), P(o, x + w, dm - 0.09, z0 + floorH * 0.75), P(o, x + w, dm + 0.09, z0 + floorH * 0.75)));
    }

    /// <summary>Satteldach mit First entlang der längeren Seite.</summary>
    public static void GableRoof(DrawingContext ctx, Vector o, double x, double y, double w, double h, double z, double rise, Color light, Color dark, Color wall)
    {
        const double eave = 0.06;
        x -= eave; y -= eave; w += 2 * eave; h += 2 * eave;
        if (w >= h)
        {
            var ym = y + h / 2;
            ctx.DrawGeometry(B(light), null, Poly(P(o, x, y, z), P(o, x + w, y, z), P(o, x + w, ym, z + rise), P(o, x, ym, z + rise)));
            ctx.DrawGeometry(B(Shade(wall, 0.92)), null, Poly(P(o, x + w, y + eave, z), P(o, x + w, y + h - eave, z), P(o, x + w, ym, z + rise)));
            ctx.DrawGeometry(B(dark), null, Poly(P(o, x, ym, z + rise), P(o, x + w, ym, z + rise), P(o, x + w, y + h, z), P(o, x, y + h, z)));
            ctx.DrawLine(new Pen(B(Alpha(Colors.White, 90)), 1.2), P(o, x, ym, z + rise), P(o, x + w, ym, z + rise));
        }
        else
        {
            var xm = x + w / 2;
            ctx.DrawGeometry(B(light), null, Poly(P(o, x, y, z), P(o, xm, y, z + rise), P(o, xm, y + h, z + rise), P(o, x, y + h, z)));
            ctx.DrawGeometry(B(Shade(wall, 0.82)), null, Poly(P(o, x + eave, y + h, z), P(o, x + w - eave, y + h, z), P(o, xm, y + h, z + rise)));
            ctx.DrawGeometry(B(dark), null, Poly(P(o, xm, y, z + rise), P(o, x + w, y, z), P(o, x + w, y + h, z), P(o, xm, y + h, z + rise)));
            ctx.DrawLine(new Pen(B(Alpha(Colors.White, 90)), 1.2), P(o, xm, y, z + rise), P(o, xm, y + h, z + rise));
        }
    }

    /// <summary>Rundes Symbol über einem Gebäude.</summary>
    public static void Badge(DrawingContext ctx, Point c, Geometry icon, Color bg, double r = 9)
    {
        ctx.DrawEllipse(B(Color.FromArgb(70, 0, 0, 0)), null, new Point(c.X + 1, c.Y + 1.5), r, r);
        ctx.DrawEllipse(B(bg), new Pen(B(Alpha(Colors.White, 200)), 1.2), c, r, r);
        var s = r * 1.15 / 12;
        using (ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(c.X - 12 * s, c.Y - 12 * s)))
            ctx.DrawGeometry(null, new Pen(Brushes.White, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
    }

    /// <summary>Stufe als kleine Ziffer neben dem Symbol.</summary>
    public static void Level(DrawingContext ctx, Point c, int level, Color ring)
    {
        if (level <= 0) return;
        ctx.DrawEllipse(B("#0E1524"), new Pen(B(ring), 1.5), c, 6.5, 6.5);
        var t = Text(level.ToString(), 9, Brushes.White, FontWeight.Bold);
        ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
    }

    public static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.SemiBold) =>
        new(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    /// <summary>Beschriftung als dunkle Pille, zentriert über einem Punkt.</summary>
    public static void Label(DrawingContext ctx, Point c, string s, double size = 11)
    {
        var t = Text(s, size, B("#E8EDF7"));
        var r = new Rect(c.X - t.Width / 2 - 7, c.Y - t.Height / 2 - 3, t.Width + 14, t.Height + 6);
        ctx.DrawRectangle(B(Color.FromArgb(215, 14, 21, 36)), null, r, r.Height / 2, r.Height / 2);
        ctx.DrawText(t, new Point(r.X + 7, r.Y + 3));
    }

    /// <summary>Baum: Schatten, Stamm, Krone aus zwei Kreisen.</summary>
    public static void Tree(DrawingContext ctx, Point foot, double size)
    {
        ctx.DrawEllipse(B(Color.FromArgb(50, 0, 0, 0)), null, new Point(foot.X + size * 0.5, foot.Y), size * 0.95, size * 0.45);
        ctx.DrawRectangle(B("#6B4E35"), null, new Rect(foot.X - 1.2, foot.Y - size * 0.9, 2.4, size * 0.9));
        ctx.DrawEllipse(B("#2F7A3A"), null, new Point(foot.X, foot.Y - size * 1.25), size * 0.85, size * 0.8);
        ctx.DrawEllipse(B("#43994C"), null, new Point(foot.X - size * 0.25, foot.Y - size * 1.45), size * 0.5, size * 0.45);
    }
}
