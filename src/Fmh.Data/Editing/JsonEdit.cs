using System.Text.Json.Nodes;

namespace Fmh.Core.Data.Editing;

/// <summary>Pfad zu einem Wert in einer JSON-Datei: Folge aus Schlüsseln (Objekt) und Indizes (Liste).</summary>
public sealed record JsonPath(IReadOnlyList<object> Segments)
{
    public static readonly JsonPath Root = new(Array.Empty<object>());

    public JsonPath Then(object segment) => new(Segments.Append(segment).ToList());

    public JsonPath Parent => new(Segments.Take(Segments.Count - 1).ToList());

    public object Last => Segments[^1];

    /// <summary>Lesbare Form, z. B. „[3].name“ oder „leagues.DE1.size“.</summary>
    public override string ToString() =>
        string.Concat(Segments.Select((s, i) => s is int n ? $"[{n}]" : (i == 0 ? "" : ".") + s));

    public JsonNode? Resolve(JsonNode? root)
    {
        var node = root;
        foreach (var s in Segments)
            node = s switch
            {
                int i when node is JsonArray a && i >= 0 && i < a.Count => a[i],
                string k when node is JsonObject o => o[k],
                _ => throw new InvalidOperationException($"Pfad '{this}' existiert nicht."),
            };
        return node;
    }

    public bool Equals(JsonPath? other) => other is not null && Segments.SequenceEqual(other.Segments);

    public override int GetHashCode() => Segments.Aggregate(17, (h, s) => h * 31 + s.GetHashCode());
}

/// <summary>Bearbeitungsschritt in einer Datei; kennt seine Umkehrung für Rückgängig.</summary>
public abstract record JsonEdit(string File)
{
    /// <summary>Führt den Schritt aus und gibt die Umkehrung zurück.</summary>
    public abstract JsonEdit Apply(PackageWorkspace ws);

    /// <summary>Kurzbeschreibung für Verlauf und Statuszeile.</summary>
    public abstract string Describe();
}

/// <summary>Setzt einen Wert (oder entfernt den Schlüssel, wenn <see cref="Value"/> null und <see cref="Remove"/> gesetzt ist).</summary>
public sealed record SetValue(string File, JsonPath Path, JsonNode? Value, bool Remove = false) : JsonEdit(File)
{
    public override JsonEdit Apply(PackageWorkspace ws)
    {
        var root = ws.Document(File);
        if (Path.Segments.Count == 0)
        {
            var oldRoot = root?.DeepClone();
            ws.ReplaceDocument(File, Value?.DeepClone());
            return new SetValue(File, Path, oldRoot);
        }
        var parent = Path.Parent.Resolve(root);
        switch (parent, Path.Last)
        {
            case (JsonObject o, string key):
                var existed = o.ContainsKey(key);
                var old = existed ? o[key]?.DeepClone() : null;
                if (Remove) o.Remove(key);
                else o[key] = Value?.DeepClone();
                return existed ? new SetValue(File, Path, old) : new SetValue(File, Path, null, Remove: true);
            case (JsonArray a, int i):
                var prev = a[i]?.DeepClone();
                a[i] = Value?.DeepClone();
                return new SetValue(File, Path, prev);
            default:
                throw new InvalidOperationException($"Pfad '{Path}' in '{File}' passt nicht.");
        }
    }

    public override string Describe() => Remove ? $"{File}: {Path} entfernt" : $"{File}: {Path} geändert";
}

/// <summary>Fügt ein Element in eine Liste ein.</summary>
public sealed record InsertItem(string File, JsonPath ListPath, int Index, JsonNode? Item) : JsonEdit(File)
{
    public override JsonEdit Apply(PackageWorkspace ws)
    {
        var list = ListPath.Resolve(ws.Document(File)) as JsonArray ?? throw new InvalidOperationException($"'{ListPath}' ist keine Liste.");
        list.Insert(Math.Clamp(Index, 0, list.Count), Item?.DeepClone());
        return new RemoveItem(File, ListPath, Math.Clamp(Index, 0, list.Count - 1));
    }

    public override string Describe() => $"{File}: Eintrag in {ListPath} hinzugefügt";
}

/// <summary>Entfernt ein Element aus einer Liste.</summary>
public sealed record RemoveItem(string File, JsonPath ListPath, int Index) : JsonEdit(File)
{
    public override JsonEdit Apply(PackageWorkspace ws)
    {
        var list = ListPath.Resolve(ws.Document(File)) as JsonArray ?? throw new InvalidOperationException($"'{ListPath}' ist keine Liste.");
        var item = list[Index]?.DeepClone();
        list.RemoveAt(Index);
        return new InsertItem(File, ListPath, Index, item);
    }

    public override string Describe() => $"{File}: Eintrag {Index} aus {ListPath} entfernt";
}

/// <summary>Mehrere Schritte als einer (z. B. CSV-Import), gemeinsam rückgängig zu machen.</summary>
public sealed record EditBatch(string Label, IReadOnlyList<JsonEdit> Edits) : JsonEdit(Edits.Count > 0 ? Edits[0].File : "")
{
    public override JsonEdit Apply(PackageWorkspace ws)
    {
        var inverse = Edits.Select(e => e.Apply(ws)).Reverse().ToList();
        return new EditBatch(Label, inverse);
    }

    public override string Describe() => Label;
}
