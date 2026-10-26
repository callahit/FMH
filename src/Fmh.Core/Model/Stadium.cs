namespace Fmh.Core.Model;

public enum StandSide { North, South, East, West }

/// <summary>Eine Tribüne mit Steh- und Sitzplätzen, Dach und Logen (Kapitel 8).</summary>
public sealed class Stand
{
    public StandSide Side { get; set; }
    public int Standing { get; set; }
    public int Seating { get; set; }
    public bool Roof { get; set; }
    public int Boxes { get; set; }
    /// <summary>Plätze, die während eines Umbaus gesperrt sind.</summary>
    public int Closed { get; set; }

    public int Capacity => Standing + Seating;
}

/// <summary>Stadion als Bausteinsystem: vier Tribünen, Flutlicht, Rasenheizung, Anzeigetafel.</summary>
public sealed class Stadium
{
    public List<Stand> Stands { get; set; } = new();
    /// <summary>Flutlicht 0–4 (Pflicht ab 2. Liga, TV-Spiele ab Stufe 3).</summary>
    public int Floodlight { get; set; }
    public bool UndersoilHeating { get; set; }
    /// <summary>Anzeigetafel 0–3 (Werbeeinnahmen).</summary>
    public int Scoreboard { get; set; }
    /// <summary>Zustand des Rasens 0–100.</summary>
    public double Pitch { get; set; } = 85;

    public int Capacity => Stands.Sum(s => s.Capacity);
    public int Available => Stands.Sum(s => Math.Max(0, s.Capacity - s.Closed));
    public int StandingTotal => Stands.Sum(s => s.Standing);
    public int SeatingTotal => Stands.Sum(s => s.Seating);
    public int Boxes => Stands.Sum(s => s.Boxes);
    public double RoofShare => Stands.Count == 0 ? 0 : Stands.Where(s => s.Roof).Sum(s => s.Capacity) / (double)Math.Max(1, Capacity);
    public Stand Stand(StandSide side) => Stands.First(s => s.Side == side);
}

public enum ProjectKind { Standing, Seating, Roof, Boxes, Floodlight, Heating, Scoreboard, NewBuilding, UpgradeBuilding }

public enum ProjectState { AwaitingBoard, AwaitingPermit, Building, Done, Rejected }

/// <summary>Bauunternehmen: günstig und langsam, normal oder teuer und schnell (Kapitel 9).</summary>
public enum Contractor { Cheap, Standard, Premium }

/// <summary>Bauauftrag für Stadion oder Vereinsgelände.</summary>
public sealed class ConstructionProject
{
    public int Id { get; set; }
    public ProjectKind Kind { get; set; }
    public StandSide? Side { get; set; }
    public string? BuildingType { get; set; }
    public int? BuildingId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Zielstufe bzw. zusätzliche Plätze (Tribünenausbau in 1.000er-Schritten).</summary>
    public int Amount { get; set; }
    public long Cost { get; set; }
    public Contractor Contractor { get; set; }
    public ProjectState State { get; set; }
    public DateOnly Ordered { get; set; }
    public DateOnly? PermitDate { get; set; }
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    /// <summary>Finanzierung über Kredit statt aus der Kasse.</summary>
    public bool Financed { get; set; }
}
