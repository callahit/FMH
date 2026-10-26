namespace Fmh.Core.Model;

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
}

/// <summary>Vereinsgelände: Raster aus Bauplätzen um Stadion und Trainingszentrum.</summary>
public sealed class Grounds
{
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

    public int EntranceX => StadiumX + StadiumWidth / 2;
    public int EntranceY => StadiumY + StadiumHeight;
}
