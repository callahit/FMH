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

    /// <summary>Umgebungsvariable mit dem Ordner der Textmodus-Dateien (Textm.sav und Textm.txt oder scene-tree.json).</summary>
    public const string DirectoryVariable = "FMH_TEXTMODE_DIR";

    private static readonly object Gate = new();
    private static SceneTreeAsset? _cached;
    private static bool _searched;

    /// <summary>
    /// Lädt den Szenenbaum aus dem Ordner, den der Nutzer bereitstellt; null, wenn keiner gefunden wird.
    /// Die Dateien gehören nicht zum Repository.
    /// </summary>
    public static SceneTreeAsset? TryLoadDefault()
    {
        lock (Gate)
        {
            if (_searched) return _cached;
            if (Locate() is { } dir) _cached = TryLoad(dir, out _);
            _searched = true;
            return _cached;
        }
    }

    /// <summary>
    /// Lädt den Szenenbaum aus einem Ordner. Sind die Dateien beschädigt oder unvollständig, gibt es keinen Szenenbaum
    /// (null, Grund in <paramref name="error"/>, auch auf der Konsole): Das Spiel startet dann mit der schnellen Engine,
    /// statt am Start abzubrechen.
    /// </summary>
    public static SceneTreeAsset? TryLoad(string dir, out string? error)
    {
        error = null;
        try
        {
            var json = Path.Combine(dir, "scene-tree.json");
            return File.Exists(json) ? SceneAssetStore.Read(json) : TextmImporter.Load(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or System.Text.Json.JsonException or ArgumentException or IndexOutOfRangeException
                                       or InvalidOperationException or OverflowException or FormatException)
        {
            error = $"Szenenbaum in {dir} nicht lesbar, Textmodus ohne Szenenbaum: {ex.Message}";
            Console.Error.WriteLine(error);
            return null;
        }
    }

    /// <summary>Nur für Tests, die den Cache nach dem Schreiben der Datei neu laden.</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            _cached = null;
            _searched = false;
        }
    }

    /// <summary>
    /// Sucht den Ordner: zuerst <see cref="DirectoryVariable"/>, dann den Benutzerordner (…/FMH/textmode),
    /// dann <c>data/textmode</c> oberhalb des Programms (lokale Kopie, per .gitignore ausgeschlossen).
    /// </summary>
    public static string? Locate()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } env) candidates.Add(env);
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMH", "textmode"));
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "data", "textmode"));
        return candidates.FirstOrDefault(Holds);
    }

    private static bool Holds(string dir) =>
        File.Exists(Path.Combine(dir, "scene-tree.json")) ||
        (File.Exists(Path.Combine(dir, "Textm.sav")) && File.Exists(Path.Combine(dir, "Textm.txt")));
}
