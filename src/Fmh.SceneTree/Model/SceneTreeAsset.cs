namespace Fmh.SceneTree;

/// <summary>Versionierter Szenenbaum: Textzeilen und Knoten in Preorder, mit aufgelösten Kindern und Kategorien.</summary>
public sealed class SceneTreeAsset
{
    public const int FormatVersion = 1;
    public const int ExpectedNodes = 9906;
    public const int ExpectedCategories = 29;
    public const int ExpectedLines = 13432;

    private SceneTreeAsset(string magic, uint headerVersion, uint headerFlags, string[] lines, SceneNode[] nodes, SceneCategory[] categories)
    {
        Magic = magic;
        HeaderVersion = headerVersion;
        HeaderFlags = headerFlags;
        Lines = lines;
        Nodes = nodes;
        Categories = categories;
    }

    public string Magic { get; }
    public uint HeaderVersion { get; }
    public uint HeaderFlags { get; }
    /// <summary>Index = Zeilennummer aus Textm.txt. Eintrag 0 ist leer.</summary>
    public string[] Lines { get; }
    public IReadOnlyList<SceneNode> Nodes { get; }
    public IReadOnlyList<SceneCategory> Categories { get; }

    public string Line(int number) => number > 0 && number < Lines.Length ? Lines[number] : "";

    public SceneCategory Category(int number)
    {
        if (number < 1 || number > Categories.Count)
            throw new ArgumentOutOfRangeException(nameof(number));
        return Categories[number - 1];
    }

    public string Text(SceneNode node)
    {
        if (node.LineRefs.Count == 0) return "";
        return Line(node.LineRefs[0]);
    }

    /// <summary>Baut Eltern, Tiefe und die 29 Kategorien aus der Preorder-Reihenfolge.</summary>
    public static SceneTreeAsset Build(string magic, uint headerVersion, uint headerFlags, string[] lines, IReadOnlyList<SceneNode> nodes)
    {
        if (nodes.Count == 0) throw new InvalidDataException("Szenenbaum ist leer.");
        var list = nodes as SceneNode[] ?? nodes.ToArray();
        var cursor = 0;
        void Link(int idx)
        {
            var node = list[idx];
            var kids = new int[node.ChildCount];
            for (var k = 0; k < node.ChildCount; k++)
            {
                cursor++;
                if (cursor >= list.Length)
                    throw new InvalidDataException($"Kindzeiger läuft bei Knoten {idx} über das Dateiende.");
                kids[k] = cursor;
                Link(cursor);
            }
            node.Bind(kids, 0, 0, 0);
        }
        Link(0);
        if (cursor != list.Length - 1)
            throw new InvalidDataException($"Preorder endet bei {cursor + 1}, erwartet {list.Length}.");

        var categories = new List<SceneCategory>();
        void Annotate(int idx, int depth, int catNode, int catNo)
        {
            var node = list[idx];
            if (depth == 1)
            {
                catNode = idx;
                catNo = categories.Count + 1;
                var name = node.LineRefs.Count == 0 ? "" : (node.LineRefs[0] > 0 && node.LineRefs[0] < lines.Length ? lines[node.LineRefs[0]] : "");
                categories.Add(new SceneCategory(catNo, idx, node.Id, name));
            }
            node.Bind(node.Children.ToArray(), depth, catNode, catNo);
            foreach (var child in node.Children)
                Annotate(child, depth + 1, catNode, catNo);
        }
        Annotate(0, 0, 0, 0);
        return new SceneTreeAsset(magic, headerVersion, headerFlags, lines, list, categories.ToArray());
    }

    private static SceneTreeAsset? _cached;

    /// <summary>Lädt data/textmode/scene-tree.json, sonst die Originaldateien daneben.</summary>
    public static SceneTreeAsset LoadDefault()
    {
        if (_cached is not null) return _cached;
        var dir = Locate();
        var json = Path.Combine(dir, "scene-tree.json");
        _cached = File.Exists(json) ? SceneAssetStore.Read(json) : TextmImporter.Load(dir);
        return _cached;
    }

    /// <summary>Nur für Tests, die den Cache nach dem Schreiben der Datei neu laden.</summary>
    public static void ClearCache() => _cached = null;

    public static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "textmode");
            if (File.Exists(Path.Combine(candidate, "Textm.sav")) || File.Exists(Path.Combine(candidate, "scene-tree.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("data/textmode nicht gefunden.");
    }
}
