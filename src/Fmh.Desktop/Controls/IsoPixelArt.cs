using Avalonia;
using Avalonia.Media;
using static Fmh.Desktop.Controls.IsoArt;

namespace Fmh.Desktop.Controls;

/// <summary>
/// Zeichnet die Karte mit den Pixel-Art-Sprites aus <see cref="IsoAtlas"/>: Rasen- und Wegkacheln, Bäume,
/// Gebäude je Art, Bereich, Größe und Ausbaustufe sowie das Stadion aus Tribünen-Bausteinen.
/// </summary>
public static class IsoPixelArt
{
    private static readonly string[] Sides = { "North", "West", "East", "South" };

    /// <summary>Stockwerke eines Hauses aus der Ausbaustufe (1–2: eins, 3–4: zwei, ab 5: drei).</summary>
    public static int Floors(int level) => level <= 2 ? 1 : level <= 4 ? 2 : 3;

    /// <summary>Sprite-Name für ein Gebäude; Flächen ohne passende Größe werden aus 1×1-Kacheln gelegt (Name mit „_1x1“).</summary>
    public static string SpriteFor(IsoBuildingVisual b)
    {
        var size = $"{b.Width}x{b.Height}";
        var cat = string.IsNullOrEmpty(b.Category) ? "none" : b.Category;
        var floors = Floors(b.Level);
        if (b.UnderConstruction) return $"construction_{size}";
        return KindOf(b.Type, b.Width, b.Height) switch
        {
            IsoArtKind.Pitch => $"pitch_{size}",
            IsoArtKind.Turf => $"turf_{size}",
            IsoArtKind.Parking => $"parking_{size}",
            IsoArtKind.Road => $"road_{size}",
            IsoArtKind.Court => "court_1x1",
            IsoArtKind.Track => "track_1x1",
            IsoArtKind.Solar => "solar_1x1",
            IsoArtKind.Park => "park_1x1",
            IsoArtKind.Pool => b.Type == "icePool" ? "icepool_1x1" : "pool_1x1",
            IsoArtKind.Garden => "garden_1x1",
            IsoArtKind.Tower => b.Type == "ledTower" ? "ledtower_1x1" : "floodlight_1x1",
            IsoArtKind.Tank => b.Type == "chp" ? "chp_1x1" : "tank_1x1",
            IsoArtKind.Monument => b.Type == "chapel" ? "chapel_1x1" : "statue_1x1",
            IsoArtKind.Garage => size == "2x2" ? "hall_infrastructure_2x2_3" : $"house_infrastructure_{size}_3",
            IsoArtKind.Hall when size == "2x2" => $"hall_{cat}_2x2_{floors}",
            _ => $"house_{cat}_{size}_{floors}",
        };
    }

    /// <summary>Zeichnet ein Gebäude; Rückgabe: Punkt über dem Dach für Beschriftung und Warnzeichen, null ohne passendes Sprite.</summary>
    public static Point? Building(DrawingContext ctx, Vector o, IsoBuildingVisual b, IsoAtlas atlas)
    {
        var name = SpriteFor(b);
        var top = P(o, b.X + b.Width / 2.0, b.Y + b.Height / 2.0);
        if (atlas.Has(name) && (!name.EndsWith("_1x1") || (b.Width == 1 && b.Height == 1)))
        {
            atlas.Draw(ctx, name, P(o, b.X, b.Y));
            return new Point(top.X, P(o, b.X, b.Y).Y - atlas.Above(name) + 14);
        }
        if (!name.EndsWith("_1x1") || !atlas.Has(name)) return null;
        // Größere Fläche: Kacheln von hinten nach vorn legen
        var above = 0;
        for (var s = 0; s < b.Width + b.Height - 1; s++)
            for (var x = 0; x < b.Width; x++)
            {
                var y = s - x;
                if (y < 0 || y >= b.Height) continue;
                atlas.Draw(ctx, name, P(o, b.X + x, b.Y + y));
                above = atlas.Above(name);
            }
        return new Point(top.X, P(o, b.X, b.Y).Y - above + 14);
    }

    /// <summary>Stadion aus Sockel, Tribünen (Größe nach Plätzen, mit/ohne Dach) und Flutlichtmasten; null, wenn die Grundfläche nicht 4×3 ist.</summary>
    public static Point? Stadium(DrawingContext ctx, Vector o, IsoMapState s, IsoAtlas atlas)
    {
        if (s.StadiumWidth != 4 || s.StadiumHeight != 3 || !atlas.Has("stadium_base")) return null;
        var anchor = P(o, s.StadiumX, s.StadiumY);
        atlas.Draw(ctx, "stadium_base", anchor);
        foreach (var side in Sides)
        {
            var level = StandLevel(s.StandCapacities.GetValueOrDefault(side, 0));
            if (level == 0) continue;
            var roof = s.RoofSides.Contains(side) || s.StadiumRoof ? 1 : 0;
            atlas.Draw(ctx, $"stand_{side}_{level}_{roof}", anchor, s.SeatColor);
        }
        if (s.Floodlight > 0)
        {
            var mast = $"mast_{Math.Clamp(s.Floodlight, 1, 4)}";
            foreach (var (dx, dy) in new[] { (0, 0), (3, 0), (0, 2), (3, 2) })
                atlas.Draw(ctx, mast, P(o, s.StadiumX + dx, s.StadiumY + dy));
        }
        return P(o, s.StadiumX + 2, s.StadiumY + 1.5, 64);
    }

    /// <summary>Tribünengröße aus den Plätzen: bis 4.000 klein, bis 12.000 mittel, darüber groß; 0 = keine Tribüne.</summary>
    public static int StandLevel(int capacity) => capacity <= 0 ? 0 : capacity <= 4000 ? 1 : capacity <= 12000 ? 2 : 3;

    /// <summary>Rasenkacheln mit Erdkante; Wege und Tore als Pflasterkacheln.</summary>
    public static void Ground(DrawingContext ctx, Vector o, IsoMapState s, IsoAtlas atlas)
    {
        var depth = 6.0;
        ctx.DrawGeometry(B("#6B4E35"), null, Poly(P(o, 0, s.Height), P(o, s.Width, s.Height),
            P(o, s.Width, s.Height) + new Vector(0, depth), P(o, 0, s.Height) + new Vector(0, depth)));
        ctx.DrawGeometry(B("#5A412C"), null, Poly(P(o, s.Width, s.Height), P(o, s.Width, 0),
            P(o, s.Width, 0) + new Vector(0, depth), P(o, s.Width, s.Height) + new Vector(0, depth)));
        var paths = s.ShowPaths ? s.Paths.Select(p => (p.X, p.Y)).ToHashSet() : new HashSet<(int, int)>();
        var gates = s.ShowPaths ? s.Gates.Select(p => (p.X, p.Y)).ToHashSet() : new HashSet<(int, int)>();
        for (var y = 0; y < s.Height; y++)
            for (var x = 0; x < s.Width; x++)
            {
                var paved = paths.Contains((x, y)) || gates.Contains((x, y));
                var name = !paved ? $"grass_{(x * 7 + y * 3) % 4}"
                    : IsPlaza(paths, x, y) ? $"plaza_{(x + y) % 2}" : $"path_{(x + y) % 2}";
                atlas.Draw(ctx, name, P(o, x, y));
            }
        // Bordstein, wo Pflaster an Rasen grenzt
        var curb = new Pen(B("#9C8C70"), 1);
        foreach (var (x, y) in paths)
        {
            if (!paths.Contains((x, y - 1)) && !gates.Contains((x, y - 1))) ctx.DrawLine(curb, P(o, x, y), P(o, x + 1, y));
            if (!paths.Contains((x - 1, y)) && !gates.Contains((x - 1, y))) ctx.DrawLine(curb, P(o, x, y), P(o, x, y + 1));
            if (!paths.Contains((x, y + 1)) && !gates.Contains((x, y + 1))) ctx.DrawLine(curb, P(o, x, y + 1), P(o, x + 1, y + 1));
            if (!paths.Contains((x + 1, y)) && !gates.Contains((x + 1, y))) ctx.DrawLine(curb, P(o, x + 1, y), P(o, x + 1, y + 1));
        }
        using (ctx.PushTransform(IsoArt.Ground(o)))
        {
            if (s.BaseWidth < s.Width)
                ctx.DrawLine(new Pen(B(Color.FromArgb(110, 255, 255, 255)), 0.03, new DashStyle(new double[] { 4, 4 }, 0)),
                    new Point(s.BaseWidth, 0), new Point(s.BaseWidth, s.Height));
            // Kaufbare Spalte rechts davon
            ctx.FillRectangle(B(Color.FromArgb(40, 120, 150, 110)), new Rect(s.Width, 0, 1, s.Height));
            ctx.DrawRectangle(null, new Pen(B(Color.FromArgb(90, 255, 255, 255)), 0.025, new DashStyle(new double[] { 3, 3 }, 0)), new Rect(s.Width, 0, 1, s.Height));
        }
    }

    /// <summary>
    /// Ausstattung der Wege, fest aus der Lage abgeleitet und ohne Wirkung im Spiel: Laternen in festen Abständen am
    /// hinteren und linken Wegrand, Bänke an der vorderen Kante, Pflanzkübel und Abfalleimer auf inneren Feldern.
    /// Dazu die Tore (in Vereinsfarbe). Nichts steht neben einem Tor.
    /// Rückgabe sortiert nach Zeichenreihenfolge (Schlüssel = vordere Ecke des Feldes, x + y + 2).
    /// </summary>
    public static List<IsoDecoration> Decorations(IsoMapState s)
    {
        var list = new List<IsoDecoration>();
        if (!s.ShowPaths) return list;
        var paths = s.Paths.Select(p => (p.X, p.Y)).ToHashSet();
        var gates = s.Gates.Select(p => (p.X, p.Y)).ToHashSet();
        bool P(int x, int y) => paths.Contains((x, y)) || gates.Contains((x, y));
        bool NearGate(int x, int y) => gates.Any(g => Math.Abs(g.X - x) <= 1 && Math.Abs(g.Y - y) <= 1);
        foreach (var g in gates) list.Add(new(g.X + g.Y + 2, g.X, g.Y, "gate", true));
        foreach (var (x, y) in paths)
        {
            if (gates.Contains((x, y)) || NearGate(x, y)) continue;
            var key = x + y + 2;
            var inner = true;
            for (var dy = -1; dy <= 1 && inner; dy++)
                for (var dx = -1; dx <= 1 && inner; dx++)
                    inner = P(x + dx, y + dy);
            var h = (x * 92821) ^ (y * 68917);
            if (!P(x, y - 1) && x % 3 == 0) list.Add(new(key, x + 0.5, y + 0.1, "deco_lamp", false));
            else if (!P(x - 1, y) && y % 3 == 0) list.Add(new(key, x + 0.1, y + 0.5, "deco_lamp", false));
            else if (!P(x, y + 1) && x % 4 == 2) list.Add(new(key, x + 0.5, y + 0.85, "deco_bench", false));
            else if (inner && h % 4 == 0) list.Add(new(key, x + 0.5, y + 0.5, $"deco_planter_{(x + y) % 2}", false));
            else if (inner && h % 7 == 3) list.Add(new(key, x + 0.5, y + 0.5, "deco_bin", false));
        }
        list.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.X.CompareTo(b.X));
        return list;
    }

    /// <summary>Zeichnet ein Ausstattungsstück; die Sprites sind auf die Feldmitte ausgerichtet.</summary>
    public static void Decoration(DrawingContext ctx, Vector o, IsoDecoration d, IsoMapState s, IsoAtlas atlas) =>
        atlas.Draw(ctx, d.Name, d.Name == "gate" ? P(o, d.X, d.Y) : P(o, d.X - 0.5, d.Y - 0.5), d.Tinted ? s.SeatColor : null);

    /// <summary>Platzfeld: Weg, dessen acht Nachbarn ebenfalls Weg sind (große Fläche statt Gehweg).</summary>
    public static bool IsPlaza(HashSet<(int, int)> paths, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (!paths.Contains((x + dx, y + dy))) return false;
        return true;
    }

    /// <summary>Baum-Sprite an einem Rasterpunkt; die Art ergibt sich fest aus der Lage.</summary>
    public static void Tree(DrawingContext ctx, Vector o, double x, double y, IsoAtlas atlas)
    {
        var names = new[] { "tree_oak_0", "tree_pine_0", "tree_oak_1", "tree_bush_0", "tree_oak_2", "tree_pine_1" };
        var k = (int)Math.Abs(Math.Floor(x * 7 + y * 13)) % names.Length;
        atlas.Draw(ctx, names[k], P(o, x - 0.5, y - 0.5));
    }

    /// <summary>Baumreihe rund um das Gelände (hinten vor dem Boden, vorn zuletzt).</summary>
    public static void Trees(DrawingContext ctx, Vector o, IsoMapState s, bool back, IsoAtlas? atlas)
    {
        var rnd = new Random(17);
        var spots = new List<(double x, double y)>();
        for (var x = 0; x <= s.Width + 1; x++) spots.Add((x + rnd.NextDouble() * 0.6, -0.55 - rnd.NextDouble() * 0.3));
        for (var y = 0; y < s.Height; y++) spots.Add((-0.55 - rnd.NextDouble() * 0.3, y + rnd.NextDouble() * 0.6));
        var front = new List<(double x, double y)>();
        for (var x = 0; x <= s.Width; x++) front.Add((x + rnd.NextDouble() * 0.6, s.Height + 0.45 + rnd.NextDouble() * 0.3));
        for (var y = 0; y < s.Height; y++) front.Add((s.Width + 1.45 + rnd.NextDouble() * 0.3, y + rnd.NextDouble() * 0.6));
        foreach (var (x, y) in (back ? spots : front).OrderBy(p => p.x + p.y))
            if (atlas is not null) Tree(ctx, o, x, y, atlas);
            else IsoArt.Tree(ctx, P(o, x, y), 6 + (x * 7 + y * 3) % 3);
    }
}

/// <summary>Ausstattungsstück oder Tor auf der Karte (Rasterposition des Fußpunkts, Zeichenschlüssel).</summary>
public sealed record IsoDecoration(int Key, double X, double Y, string Name, bool Tinted);
