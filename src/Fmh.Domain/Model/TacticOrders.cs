namespace Fmh.Core.Model;

/// <summary>Rolle, Aufgabe und Spieleranweisungen für einen Formations-Slot (Kapitel 5, FM-Stil).</summary>
public sealed class SlotOrder
{
    /// <summary>Rollen-Id aus rules/roles.json, leer = Standardrolle der Position.</summary>
    public string Role { get; set; } = "";
    public Duty? Duty { get; set; }
    public bool MoreShots { get; set; }
    public bool TightMarking { get; set; }
    public bool CrossEarly { get; set; }
    public bool HoldPosition { get; set; }
    /// <summary>Freie Positionierung: verschobener Platz auf dem 5x7-Raster (null = Formationsvorgabe).</summary>
    public int? Col { get; set; }
    public int? Row { get; set; }

    public SlotOrder Clone() => (SlotOrder)MemberwiseClone();
}

/// <summary>Varianten bei Ecken (Kapitel 5): kurz, langer Pfosten, Kopfballspieler vorn.</summary>
public enum CornerRoutine { Mixed, Short, FarPost, TallPlayersForward }

public enum FreeKickRoutine { Auto, Direct, Cross }
