namespace Fmh.Core.Model;

/// <summary>Mentalität in 7 Stufen von sehr defensiv (-3) bis sehr offensiv (+3).</summary>
public enum Mentality { VeryDefensive = -3, Defensive = -2, Cautious = -1, Balanced = 0, Positive = 1, Attacking = 2, VeryAttacking = 3 }

public enum PassingLength { Short, Mixed, Long }
public enum Tempo { Low, Normal, High }
public enum Width { Narrow, Normal, Wide }
public enum AttackFocus { Mixed, Wings, Centre }
public enum PressingHeight { Low, Mid, High }
public enum DefensiveLine { Deep, Normal, High }
public enum TacklingStyle { Fair, Normal, Hard }
public enum Distribution { Slow, Fast }

/// <summary>Mannschaftsanweisungen (Kapitel 5): mit Ball, gegen den Ball, Umschaltspiel.</summary>
public sealed class TeamInstructions
{
    public PassingLength PassingLength { get; set; } = PassingLength.Mixed;
    public Tempo Tempo { get; set; } = Tempo.Normal;
    public Width Width { get; set; } = Width.Normal;
    public AttackFocus Focus { get; set; } = AttackFocus.Mixed;
    public bool Counter { get; set; }
    public PressingHeight Pressing { get; set; } = PressingHeight.Mid;
    public DefensiveLine Line { get; set; } = DefensiveLine.Normal;
    public bool OffsideTrap { get; set; }
    public TacklingStyle Tackling { get; set; } = TacklingStyle.Normal;
    public bool Gegenpressing { get; set; }
    public Distribution GoalkeeperDistribution { get; set; } = Distribution.Slow;

    public TeamInstructions Clone() => (TeamInstructions)MemberwiseClone();
}

public sealed class Tactic
{
    public string FormationId { get; set; } = "442";
    public Mentality Mentality { get; set; } = Mentality.Balanced;
    public TeamInstructions Instructions { get; set; } = new();
    /// <summary>Spieler-Ids je Formations-Slot (Index = Slot), null = unbesetzt.</summary>
    public List<int?> Lineup { get; set; } = new();
    /// <summary>Ersatzbank, bis zu 9 Spieler.</summary>
    public List<int> Bench { get; set; } = new();
    public int? CaptainId { get; set; }
    public int? PenaltyTakerId { get; set; }
    public int? FreeKickTakerId { get; set; }
    public int? CornerTakerId { get; set; }

    public Tactic Clone() => new()
    {
        FormationId = FormationId,
        Mentality = Mentality,
        Instructions = Instructions.Clone(),
        Lineup = new List<int?>(Lineup),
        Bench = new List<int>(Bench),
        CaptainId = CaptainId,
        PenaltyTakerId = PenaltyTakerId,
        FreeKickTakerId = FreeKickTakerId,
        CornerTakerId = CornerTakerId,
    };
}

/// <summary>Formationsvorlage aus den Daten. Raster 5 Spalten (0 = links) x 7 Reihen (0 = eigenes Tor).</summary>
public sealed class Formation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<FormationSlot> Slots { get; set; } = new();
}

public sealed class FormationSlot
{
    public Position Position { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public Duty Duty { get; set; } = Duty.Support;
}

public static class Grid
{
    public const int Cols = 5;
    public const int Rows = 7;
}
