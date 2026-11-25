namespace Fmh.Core.Model;

/// <summary>
/// Index je Spieler (oder anderem ganzzahligen Schlüssel) über eine Liste der Welt, an die nur angehängt wird
/// (Vertragshistorie, Klauseln, Versprechen). Neue Einträge werden beim nächsten Zugriff nachgetragen; wird die Liste
/// ersetzt (Laden) oder kürzer, baut sich der Index neu auf. Die Reihenfolge je Schlüssel ist die der Liste.
/// Der Schlüssel eines Eintrags darf sich nicht ändern.
/// </summary>
public sealed class AppendOnlyIndex<T>
{
    private readonly Func<T, int> _key;
    private readonly Dictionary<int, List<T>> _byKey = new();
    private List<T>? _list;
    private int _count;

    public AppendOnlyIndex(Func<T, int> key) => _key = key;

    /// <summary>Alle Einträge mit diesem Schlüssel in Listenreihenfolge (leer, wenn keine).</summary>
    public IReadOnlyList<T> Of(List<T> list, int key)
    {
        if (!ReferenceEquals(list, _list) || list.Count < _count)
        {
            _byKey.Clear();
            _list = list;
            _count = 0;
        }
        for (; _count < list.Count; _count++)
        {
            var item = list[_count];
            var k = _key(item);
            if (!_byKey.TryGetValue(k, out var entries)) _byKey[k] = entries = new List<T>();
            entries.Add(item);
        }
        return _byKey.TryGetValue(key, out var found) ? found : Array.Empty<T>();
    }
}
