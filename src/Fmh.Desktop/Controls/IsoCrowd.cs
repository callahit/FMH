using Avalonia.Threading;

namespace Fmh.Desktop.Controls;

/// <summary>
/// Leben auf dem Vereinsgelände: Besucher und Fans laufen über das Wegenetz, Spieler trainieren auf den Plätzen.
/// Reine Darstellung ohne Wirkung im Spiel; mit festem Seed reproduzierbar. Ein Zeitgeber bewegt die Figuren
/// etwa zehnmal je Sekunde und lässt die Karte neu zeichnen.
/// </summary>
public sealed class IsoCrowd
{
    private static readonly string[] Visitors = { "fan", "red", "blue", "white", "yellow", "staff", "fan" };
    private readonly List<Walker> _walkers = new();
    private HashSet<(int, int)> _paths = new();
    private Random _rnd = new(1);
    private DispatcherTimer? _timer;

    public int Count => _walkers.Count;

    private sealed class Walker
    {
        public double X, Y, TargetX, TargetY, Speed, Phase, Offset;
        public int TileX, TileY, PrevX, PrevY;
        public string Variant = "fan";
        public bool Player;
        public double AreaX0, AreaY0, AreaX1, AreaY1;
        public int Key;
        public bool Front, Right;
    }

    /// <summary>Figuren für einen neuen Kartenstand aufstellen: Besucher auf Wegen, Spieler auf Plätzen.</summary>
    public void Reset(IsoMapState? s, int seed = 7)
    {
        _walkers.Clear();
        _rnd = new Random(seed);
        if (s is null) return;
        _paths = s.Paths.Select(p => (p.X, p.Y)).Concat(s.Gates.Select(g => (g.X, g.Y))).ToHashSet();
        var tiles = _paths.OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToList();
        var visitors = tiles.Count == 0 ? 0 : Math.Min(40, tiles.Count / 3 + 4);
        for (var i = 0; i < visitors; i++)
        {
            var (tx, ty) = tiles[_rnd.Next(tiles.Count)];
            var w = new Walker
            {
                TileX = tx, TileY = ty, PrevX = tx, PrevY = ty, X = tx + 0.5, Y = ty + 0.5,
                Speed = 0.45 + _rnd.NextDouble() * 0.4, Phase = _rnd.NextDouble(),
                Offset = (_rnd.NextDouble() - 0.5) * 0.5, Variant = Visitors[_rnd.Next(Visitors.Length)],
            };
            NextTile(w);
            _walkers.Add(w);
        }
        foreach (var b in s.Buildings.Where(b => !b.UnderConstruction && IsPitch(b)))
        {
            var n = Math.Min(14, 4 + 2 * b.Level);
            for (var i = 0; i < n; i++)
            {
                var w = new Walker
                {
                    Player = true, Variant = "player", Speed = 0.8 + _rnd.NextDouble() * 0.8, Phase = _rnd.NextDouble(),
                    AreaX0 = b.X + 0.3, AreaY0 = b.Y + 0.3, AreaX1 = b.X + b.Width - 0.3, AreaY1 = b.Y + b.Height - 0.3,
                    Key = b.X + b.Width + b.Y + b.Height,
                };
                (w.X, w.Y) = RandomInArea(w);
                (w.TargetX, w.TargetY) = RandomInArea(w);
                _walkers.Add(w);
            }
        }
    }

    private static bool IsPitch(IsoBuildingVisual b) =>
        IsoArt.KindOf(b.Type, b.Width, b.Height) is IsoArtKind.Pitch or IsoArtKind.Turf or IsoArtKind.Court;

    private (double, double) RandomInArea(Walker w) =>
        (w.AreaX0 + _rnd.NextDouble() * (w.AreaX1 - w.AreaX0), w.AreaY0 + _rnd.NextDouble() * (w.AreaY1 - w.AreaY0));

    /// <summary>Nächstes Wegfeld: ein Nachbar auf dem Wegenetz, möglichst nicht zurück.</summary>
    private void NextTile(Walker w)
    {
        var options = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
            .Select(d => (w.TileX + d.Item1, w.TileY + d.Item2)).Where(_paths.Contains).ToList();
        if (options.Count > 1) options.Remove((w.PrevX, w.PrevY));
        var (nx, ny) = options.Count == 0 ? (w.TileX, w.TileY) : options[_rnd.Next(options.Count)];
        (w.PrevX, w.PrevY, w.TileX, w.TileY) = (w.TileX, w.TileY, nx, ny);
        // Seitlicher Versatz quer zur Laufrichtung, damit nicht alle auf der Mittellinie gehen
        var sideX = ny != w.PrevY ? w.Offset : 0;
        var sideY = nx != w.PrevX ? w.Offset : 0;
        (w.TargetX, w.TargetY) = (nx + 0.5 + sideX, ny + 0.5 + sideY);
    }

    /// <summary>Alle Figuren um <paramref name="dt"/> Sekunden weiterbewegen.</summary>
    public void Step(double dt)
    {
        foreach (var w in _walkers)
        {
            var dx = w.TargetX - w.X;
            var dy = w.TargetY - w.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var step = w.Speed * dt;
            if (dist <= step)
            {
                (w.X, w.Y) = (w.TargetX, w.TargetY);
                if (w.Player) (w.TargetX, w.TargetY) = RandomInArea(w);
                else NextTile(w);
                continue;
            }
            w.X += dx / dist * step;
            w.Y += dy / dist * step;
            w.Phase += dt * w.Speed * 3;
            // Blickrichtung aus der Bewegung auf dem Bildschirm: nach unten = von vorn, nach rechts = rechts
            w.Front = dx + dy > 0;
            w.Right = dx - dy > 0;
        }
    }

    /// <summary>Figuren als Zeichenaufträge, einsortierbar zwischen Gebäude (Spieler direkt über ihrem Platz).</summary>
    public IEnumerable<IsoDecoration> Sprites() => _walkers.Select(w => new IsoDecoration(
        w.Player ? w.Key : (int)Math.Floor(w.X) + (int)Math.Floor(w.Y) + 2, w.X, w.Y,
        $"person_{w.Variant}_{(w.Front ? 'f' : 'b')}{(w.Right ? 'r' : 'l')}_{(int)(w.Phase * 2) % 2}",
        w.Variant is "fan" or "player"));

    /// <summary>Position jeder Figur (für Tests).</summary>
    public IEnumerable<(double x, double y, bool player)> Positions => _walkers.Select(w => (w.X, w.Y, w.Player));

    public void Start(Action redraw)
    {
        Stop();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            Step(0.1);
            redraw();
        });
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}
