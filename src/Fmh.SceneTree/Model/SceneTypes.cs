namespace Fmh.SceneTree;

/// <summary>Rollencode aus dem FM2002-Szenenbaum (untere 7 Bit). Bit 0x80 am Byte markiert Team 1 (angreifend).</summary>
public enum FmRole
{
    Gk = 1, Rb = 2, Lb = 3, Cd = 4, Sw = 5, Rwb = 6, Lwb = 7, Anc = 8,
    Dm = 9, Rm = 10, Lm = 11, Cm = 12, Rw = 13, Lw = 14, Am = 15, Cf = 16, St = 17,
    Pen = 18, Fkc = 19, Fkr = 20, Fkl = 21, Ckr = 22, Ckl = 23,
}

/// <summary>Rolle plus Teambit. Leer, wenn das Byte 0 ist.</summary>
public readonly record struct RoleRef(bool Attacking, int Code)
{
    public bool IsEmpty => Code == 0;

    public static RoleRef Decode(byte value) =>
        value == 0 ? default : new RoleRef((value & 128) != 0, value & 127);
}

/// <summary>Torart, Byte 23. 7 = kein Tor.</summary>
public enum SceneGoalType
{
    Header = 0, LongRange = 1, Volley = 2, DirectFreeKick = 3, Penalty = 4, OwnGoal = 5, Finish = 6, None = 7,
}

/// <summary>Ergebnis-Flags aus Byte 14 (Low) und 15 (High). Belegt im Katalog.</summary>
public readonly record struct SceneResult(byte Lo, byte Hi)
{
    public bool Goal => (Lo & 0x01) != 0;
    public bool Shot => (Lo & 0x02) != 0;
    public bool OnTarget => (Lo & 0x04) != 0;
    public bool OwnGoal => (Lo & 0x08) != 0;
    public bool KeeperBall => (Lo & 0x40) != 0;
    public bool Post => (Lo & 0x80) != 0;
    public bool Bar => (Hi & 0x01) != 0;
    public bool CornerLeft => (Hi & 0x02) != 0;
    public bool CornerRight => (Hi & 0x04) != 0;
    public bool Corner => CornerLeft || CornerRight;
    public bool Foul => (Hi & 0x08) != 0;
    public bool Wide => (Hi & 0x10) != 0;

    public bool Scores => Goal || OwnGoal;
    public bool ShotLike => Scores || Shot || OnTarget || KeeperBall || Post || Bar || Wide;
}

[Flags]
public enum PassKind : byte
{
    None = 0, Unknown1 = 1, BallWin = 2, Offside = 4, High = 8, Short = 16, Long = 32, Stands = 64, Through = 128,
}

[Flags]
public enum SceneAction : byte
{
    None = 0, Shot = 1, Pass = 2, Dribble = 4, Clearance = 8, Tackle = 16, Unsporting = 64, FoulWhistle = 128,
}

[Flags]
public enum SetPieceKind : byte
{
    None = 0, CornerOrHeader = 1, DirectFreeKick = 2, Indirect = 4, Penalty = 8,
}

[Flags]
public enum RefDecision : byte
{
    None = 0, Penalty = 1, FreeKick = 16, PlayOn = 32, NoFoul = 64,
}

/// <summary>Harte Bedingungen, Byte 27. Bit 32 ist unbelegt und wird nicht gefiltert.</summary>
[Flags]
public enum SceneCondition : byte
{
    None = 0, Wet = 1, Icy = 2, Floodlit = 4, Unknown = 32, LivePlay = 64, ClearLead = 128,
}

/// <summary>Austauschbare Gewichte der Kindauswahl. Die Defaults kommen aus der Kalibrierung (docs/textmodus.md).</summary>
public sealed class SceneWeightOptions
{
    /// <summary>Grundgewicht, das zu b32 addiert wird, damit Kinder mit b32 = 0 erreichbar bleiben.</summary>
    public double Ground { get; init; } = 1;

    public double BaseScale { get; init; } = 1;

    /// <summary>Skalierung eines Attributterms. Der Term ist der Spielerwert 1–20, invertiert 21 − Wert, wenn der Index ≥ 50 ist.</summary>
    public double AttributeScale { get; init; } = 0.22;

    /// <summary>Untergrenze, wenn b32 = 0 und ein Modifikator-Paar gesetzt ist.</summary>
    public double ModifierFloor { get; init; } = 1;

    /// <summary>Wie stark die Stimmungsbytes b8/b9 (Hypothese) das Gewicht dämpfen.</summary>
    public double MoodSlope { get; init; } = 0.06;

    /// <summary>Minuten, die eine offene Szene auf der Uhr kostet. Folgeszenen (Ecke, Freistoß) kosten nichts extra.</summary>
    public double MinutesPerScene { get; init; } = 6.4;

    /// <summary>Gelbe Karte je Foulknoten mit Täter, skaliert mit der Schiri-Strenge.</summary>
    public double CardChance { get; init; } = 0.42;

    public double RedChance { get; init; } = 0.010;
    public double SeriousRedChance { get; init; } = 0.045;

    /// <summary>Verletzung, wenn ein Blatt die Verletzungsrolle trägt. Mal der Welthäufigkeit.</summary>
    public double InjuryChance { get; init; } = 0.35;

    /// <summary>Platzhaltertexte erzeugen. Die Kalibrierung schaltet das aus, die Statistik bleibt.</summary>
    public bool EmitNarrative { get; init; } = true;

    public static SceneWeightOptions Default { get; } = new();
}
