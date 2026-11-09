namespace Fmh.Core.Model;

/// <summary>Rasterkoordinate auf dem Vereinsgelände (Wegenetz, Tore, Distanz).</summary>
public readonly record struct GridPos(int X, int Y)
{
    public int Manhattan(GridPos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

/// <summary>Ein Gebäude auf dem Vereinsgelände (Kapitel 9). Typ, Kosten und Wirkung stehen in rules/buildings.json.</summary>
public sealed class Building
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Level { get; set; } = 1;
    /// <summary>Zustand 0–100 %; sinkt ohne Instandhaltung und schwächt die Wirkung.</summary>
    public double Condition { get; set; } = 100;
    /// <summary>Namensrecht eines Sponsors am Gebäude.</summary>
    public string? SponsorName { get; set; }
    /// <summary>Im Bau (Neubau) bzw. im Ausbau auf die nächste Stufe.</summary>
    public bool UnderConstruction { get; set; }
    public bool Leased { get; set; }
    /// <summary>Drehung in Grad (0/90/180/270); bei 90/270 tauschen Breite und Höhe.</summary>
    public int Rotation { get; set; }
}

/// <summary>Vereinsgelände: Raster aus Bauplätzen um Stadion und Trainingszentrum.</summary>
public sealed class Grounds
{
    /// <summary>Schemaversion der Geländekarte; 0 = Altstand ohne Wegenetz, 1 = Paths/Gates/Rotation.</summary>
    public int MapVersion { get; set; }
    public int Width { get; set; } = 12;
    public int Height { get; set; } = 8;
    /// <summary>Stadion auf dem Raster (belegt Felder, Eingang liegt unten mittig).</summary>
    public int StadiumX { get; set; } = 4;
    public int StadiumY { get; set; } = 0;
    public int StadiumWidth { get; set; } = 4;
    public int StadiumHeight { get; set; } = 3;
    public List<Building> Buildings { get; set; } = new();
    public List<ConstructionProject> Projects { get; set; } = new();
    /// <summary>Zusätzlich gekaufte Spalten am rechten Rand.</summary>
    public int ExtraColumns { get; set; }
    public bool Maintenance { get; set; } = true;
    /// <summary>Von der Stadt angebotenes Grundstück für einen Stadionneubau.</summary>
    public StadiumSite? Site { get; set; }
    /// <summary>Wegenetz: begehbare Kacheln (SimCity-/Anstoß-Pfadlogik).</summary>
    public List<GridPos> Paths { get; set; } = new();
    /// <summary>Eingänge/Tore: Startpunkte der Erreichbarkeit (BFS).</summary>
    public List<GridPos> Gates { get; set; } = new();

    public int EntranceX => StadiumX + StadiumWidth / 2;
    public int EntranceY => StadiumY + StadiumHeight;

    /// <summary>
    /// Alte Spielstände ohne MapVersion auf Schema 1 heben: Gate am Stadioneingang,
    /// fehlendes Wegenetz und fehlende Rotation mit Defaults füllen.
    /// </summary>
    public void EnsureCurrent()
    {
        if (MapVersion >= 1)
        {
            if (Gates.Count == 0) Gates.Add(new GridPos(EntranceX, Math.Min(Height - 1, EntranceY)));
            return;
        }

        RotationDefaults();
        if (Gates.Count == 0)
            Gates.Add(new GridPos(EntranceX, Math.Min(Height - 1, Math.Max(0, EntranceY))));
        if (Paths.Count == 0)
        {
            // Ohne Katalog nur die Ankerzelle jedes Gebäudes sperren (Migration).
            var blocked = new HashSet<(int x, int y)>(Buildings.Select(b => (b.X, b.Y)));
            SeedDefaultPaths(blocked);
        }
        MapVersion = 1;
    }

    private void RotationDefaults()
    {
        foreach (var b in Buildings)
            if (b.Rotation is not (0 or 90 or 180 or 270))
                b.Rotation = 0;
    }

    /// <summary>Hauptweg vom Stadioneingang nach Süden und kurze Stichwege zu bestehenden Gebäuden.</summary>
    /// <param name="blocked">Zusätzlich gesperrte Kacheln (Gebäudegrundflächen).</param>
    public void SeedDefaultPaths(HashSet<(int x, int y)>? blocked = null)
    {
        var width = Width + ExtraColumns;
        var gate = Gates.Count > 0 ? Gates[0] : new GridPos(EntranceX, Math.Min(Height - 1, EntranceY));
        var path = new HashSet<(int x, int y)>();
        // Südlicher Hauptweg vom Gate
        for (var y = gate.Y; y < Height; y++)
            TryAddPath(path, gate.X, y, width, blocked);
        // Querverbindung auf mittlerer Höhe
        var midY = Math.Min(Height - 1, Math.Max(gate.Y, Height / 2));
        for (var x = 0; x < width; x++)
            TryAddPath(path, x, midY, width, blocked);
        foreach (var b in Buildings)
        {
            // Stichweg zu einer freien Nachbarzelle des Gebäudes (nicht auf dem Gebäude)
            var anchor = PathAnchor(b.X, b.Y, width, blocked) ?? (b.X, Math.Min(Height - 1, Math.Max(0, b.Y)));
            ConnectL(path, gate.X, gate.Y, anchor.Item1, anchor.Item2, width, blocked);
        }
        Paths = path.Select(p => new GridPos(p.x, p.y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
    }

    private (int x, int y)? PathAnchor(int bx, int by, int width, HashSet<(int x, int y)>? blocked)
    {
        foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0), (0, 2), (2, 0) })
        {
            var x = bx + dx;
            var y = by + dy;
            if (x < 0 || y < 0 || x >= width || y >= Height) continue;
            if (IsStadiumCell(x, y)) continue;
            if (blocked is not null && blocked.Contains((x, y))) continue;
            return (x, y);
        }
        return null;
    }

    private void TryAddPath(HashSet<(int x, int y)> path, int x, int y, int width, HashSet<(int x, int y)>? blocked)
    {
        if (x < 0 || y < 0 || x >= width || y >= Height) return;
        if (IsStadiumCell(x, y)) return;
        if (blocked is not null && blocked.Contains((x, y))) return;
        path.Add((x, y));
    }

    public bool IsStadiumCell(int x, int y) =>
        x >= StadiumX && x < StadiumX + StadiumWidth && y >= StadiumY && y < StadiumY + StadiumHeight;

    private void ConnectL(HashSet<(int x, int y)> path, int x0, int y0, int x1, int y1, int width, HashSet<(int x, int y)>? blocked)
    {
        var x = x0;
        var y = y0;
        while (x != x1)
        {
            TryAddPath(path, x, y, width, blocked);
            x += Math.Sign(x1 - x);
        }
        while (y != y1)
        {
            TryAddPath(path, x, y, width, blocked);
            y += Math.Sign(y1 - y);
        }
        TryAddPath(path, x1, y1, width, blocked);
    }
}
