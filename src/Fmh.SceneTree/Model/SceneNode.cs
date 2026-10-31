namespace Fmh.SceneTree;

/// <summary>
/// Ein Knoten des Szenenbaums. Die 44 Parameterbytes bleiben erhalten; die Eigenschaften lesen sie einzeln aus.
/// </summary>
public sealed class SceneNode
{
    public const int ParameterLength = 44;

    public SceneNode(int index, int id, int[] lineRefs, byte[] parameters, int childCount)
    {
        if (parameters.Length != ParameterLength)
            throw new ArgumentException($"Parameterblock muss {ParameterLength} Byte haben.", nameof(parameters));
        Index = index;
        Id = id;
        LineRefs = lineRefs;
        Parameters = parameters;
        ChildCount = childCount;
        Children = Array.Empty<int>();
    }

    public int Index { get; }
    public int Id { get; }
    public IReadOnlyList<int> LineRefs { get; }
    public byte[] Parameters { get; }
    public int ChildCount { get; }
    public IReadOnlyList<int> Children { get; private set; }
    public int Depth { get; private set; }
    public int CategoryNode { get; private set; }
    /// <summary>1–29 wie das Folgekategorie-Byte, 0 an der Wurzel.</summary>
    public int CategoryNumber { get; private set; }

    public bool IsLeaf => (Parameters[25] & 128) != 0 || ChildCount == 0;
    /// <summary>Bits 0–1 von Byte 25: 1 rechts, 2 links, 3 zentral.</summary>
    public int Zone => Parameters[25] & 3;

    public RoleRef ModARole => RoleRef.Decode(Parameters[0]);
    public RoleRef ModBRole => RoleRef.Decode(Parameters[1]);
    public byte ModAAttr => Parameters[2];
    public byte ModBAttr => Parameters[3];
    public RoleRef ModCRole => RoleRef.Decode(Parameters[4]);
    public byte ModCAttr => Parameters[6];
    public bool HasModifier => !ModARole.IsEmpty || !ModBRole.IsEmpty || !ModCRole.IsEmpty;

    public byte MoodAtt => Parameters[8];
    public byte MoodDef => Parameters[9];
    public int? ShotDistance => Parameters[10] == 255 ? null : Parameters[10];
    public PassKind Pass => (PassKind)Parameters[11];
    public bool OffsideTrap => Parameters[12] == 1;
    public SetPieceKind SetPiece => (SetPieceKind)Parameters[13];
    public SceneResult Result => new(Parameters[14], Parameters[15]);
    public RefDecision Referee => (RefDecision)Parameters[18];
    public RoleRef Victim => RoleRef.Decode(Parameters[19]);
    public byte FoulValue => Parameters[20];
    public byte FkZone => Parameters[22];
    public SceneGoalType GoalType => Parameters[23] <= 7 ? (SceneGoalType)Parameters[23] : SceneGoalType.None;
    public int? FollowUp => Parameters[26] == 0 ? null : Parameters[26];
    public SceneCondition Conditions => (SceneCondition)Parameters[27];
    public byte Mood28 => Parameters[28];
    public byte SpecialId => Parameters[29];
    public SceneAction Action => (SceneAction)Parameters[30];
    public byte RefStyle => Parameters[31];
    public byte BaseWeight => Parameters[32];
    public RoleRef Actor => RoleRef.Decode(Parameters[33]);
    public byte RefSeverity => Parameters[34];
    public RoleRef OffenderPlay => RoleRef.Decode(Parameters[35]);
    public RoleRef OffenderConduct => RoleRef.Decode(Parameters[36]);
    public byte Reason => Parameters[37];
    public RoleRef Injured => RoleRef.Decode(Parameters[38]);
    public byte InjuryTypeCode => Parameters[39];
    public RoleRef RatingARole => RoleRef.Decode(Parameters[40]);
    public RoleRef RatingBRole => RoleRef.Decode(Parameters[41]);
    public byte RatingA => Parameters[42];
    public byte RatingB => Parameters[43];

    internal void Bind(int[] children, int depth, int categoryNode, int categoryNumber)
    {
        Children = children;
        Depth = depth;
        CategoryNode = categoryNode;
        CategoryNumber = categoryNumber;
    }
}

/// <summary>Eine der 29 Wurzelkategorien. <see cref="Number"/> ist der Wert von Byte 26.</summary>
public sealed record SceneCategory(int Number, int NodeIndex, int Id, string Name);
