namespace Fmh.SceneTree;

/// <summary>Blätter für kurze Szenen: Foul, Abseits, Abschluss, Einwurf, Ecke, sonstige Texte.</summary>
public sealed class SceneLibrary
{
    public int[] Fouls { get; }
    public int[] Offsides { get; }
    public int[] Wide { get; }
    public int[] OnTarget { get; }
    public int[] ThrowIns { get; }
    public int[] CornerShort { get; }
    public RareSpot[] Rare { get; }

    private SceneLibrary(int[] fouls, int[] offsides, int[] wide, int[] onTarget, int[] throwIns, int[] cornerShort, RareSpot[] rare)
    {
        Fouls = fouls;
        Offsides = offsides;
        Wide = wide;
        OnTarget = onTarget;
        ThrowIns = throwIns;
        CornerShort = cornerShort;
        Rare = rare;
    }

    public static SceneLibrary For(SceneTreeAsset tree)
    {
        lock (Gate)
        {
            if (_for != tree) { _for = tree; _cache = Build(tree); }
            return _cache!;
        }
    }

    private static readonly object Gate = new();
    private static SceneTreeAsset? _for;
    private static SceneLibrary? _cache;

    private static SceneLibrary Build(SceneTreeAsset tree)
    {
        var fouls = new List<int>();
        var offsides = new List<int>();
        var wide = new List<int>();
        var onTarget = new List<int>();
        var throwIns = new List<int>();
        var corners = new List<int>();
        foreach (var node in tree.Nodes)
        {
            if (!node.IsLeaf || node.Result.Scores) continue;
            if (node.Result.Foul) fouls.Add(node.Index);
            if ((node.Pass & PassKind.Offside) != 0) offsides.Add(node.Index);
            if (node.CategoryNumber is >= 10 and <= 21 && node.Result.ShotLike)
            {
                if (node.Result.Wide) wide.Add(node.Index);
                else onTarget.Add(node.Index);
            }
            if (node.CategoryNumber is 15 or 16) throwIns.Add(node.Index);
            if (node.CategoryNumber is 13 or 14) corners.Add(node.Index);
        }
        var rare = new List<RareSpot>();
        foreach (var child in tree.Nodes[tree.Category(29).NodeIndex].Children)
            rare.Add(RareSpot.From(tree, tree.Nodes[child]));
        return new SceneLibrary(fouls.ToArray(), offsides.ToArray(), wide.ToArray(), onTarget.ToArray(), throwIns.ToArray(), corners.ToArray(), rare.ToArray());
    }
}

public sealed class RareSpot
{
    public int Node { get; }
    public string Tag { get; }
    public bool Easter { get; }
    public double Stoppage { get; }
    public RareKind Kind { get; }

    private RareSpot(int node, string tag, bool easter, double stoppage, RareKind kind)
    {
        Node = node;
        Tag = tag;
        Easter = easter;
        Stoppage = stoppage;
        Kind = kind;
    }

    public static RareSpot From(SceneTreeAsset tree, SceneNode node)
    {
        var text = tree.Text(node);
        var easter = node.SpecialId is 2 or 4
                     || text.Contains("Karim", StringComparison.Ordinal)
                     || text.Contains("Marcel", StringComparison.Ordinal)
                     || text.Contains("Pothmann", StringComparison.Ordinal)
                     || text.Contains("Geburtstag", StringComparison.Ordinal);
        var (tag, kind, stop) = Classify(text, easter);
        return new RareSpot(node.Index, tag, easter, stop, kind);
    }

    private static (string Tag, RareKind Kind, double Stoppage) Classify(string text, bool easter)
    {
        if (easter && text.Contains("Karim", StringComparison.Ordinal)) return ("karim", RareKind.Easter, 1.2);
        if (easter && (text.Contains("Marcel", StringComparison.Ordinal) || text.Contains("Pothmann", StringComparison.Ordinal))) return ("marcel", RareKind.Easter, 1.2);
        if (text.Contains("Megaphon", StringComparison.Ordinal)) return ("megaphon", RareKind.Crowd, 1.1);
        if (text.Contains("Riesenbanner", StringComparison.Ordinal)) return ("banner", RareKind.Flavour, 0);
        if (text.Contains("Gegenstände", StringComparison.Ordinal)) return ("gegenstaende", RareKind.Crowd, 1.3);
        if (text.Contains("Polizeihund", StringComparison.Ordinal)) return ("hund", RareKind.Flavour, 1.4);
        if (text.Contains("Fan auf den Platz", StringComparison.Ordinal)) return ("flitzer", RareKind.Crowd, 1.6);
        if (text.Contains("Donner", StringComparison.Ordinal) || text.Contains("Gewitter", StringComparison.Ordinal)) return ("donner", RareKind.Thunder, 0);
        if (text.Contains("Vogel", StringComparison.Ordinal) || text.Contains("Gänse", StringComparison.Ordinal) || text.Contains("Greif", StringComparison.Ordinal)) return ("voegel", RareKind.Flavour, 0);
        if (text.Contains("Elster", StringComparison.Ordinal)) return ("elster", RareKind.Flavour, 2.2);
        if (text.Contains("Tiefflieger", StringComparison.Ordinal) || text.Contains("Zeppelin", StringComparison.Ordinal)) return ("flieger", RareKind.Flavour, 0);
        if (text.Contains("Flutlicht", StringComparison.Ordinal)) return ("flutlicht", RareKind.Floodlight, 5.5);
        if (text.Contains("Feuerwerk", StringComparison.Ordinal)) return ("feuerwerk", RareKind.Crowd, 1.4);
        if (text.Contains("fast lautlos", StringComparison.Ordinal)) return ("hubschrauber", RareKind.Flavour, 0);
        if (text.Contains("Hubschrauber", StringComparison.Ordinal)) return ("hubschrauber-west", RareKind.Flavour, 0);
        if (text.Contains("Fußballschuhe", StringComparison.Ordinal) && text.Contains("1-ST", StringComparison.Ordinal)) return ("schuhe-st", RareKind.Wet, 0.8);
        if (text.Contains("Fußballschuhe", StringComparison.Ordinal)) return ("schuhe-cd", RareKind.Wet, 0.8);
        if (text.Contains("Hose", StringComparison.Ordinal)) return ("hose", RareKind.Flavour, 0.8);
        if (text.Contains("Trikot", StringComparison.Ordinal)) return ("trikot", RareKind.Flavour, 0.8);
        return (easter ? "easter" : "sonst", easter ? RareKind.Easter : RareKind.Flavour, easter ? 1 : 0);
    }
}

public enum RareKind { Flavour, Crowd, Thunder, Floodlight, Wet, Easter }
