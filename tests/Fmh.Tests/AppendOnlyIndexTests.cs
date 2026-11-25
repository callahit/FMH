using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>Index über Listen der Welt, an die nur angehängt wird (Vertragshistorie, Klauseln, Versprechen).</summary>
public sealed class AppendOnlyIndexTests
{
    private sealed record Entry(int Key, string Name);

    [Fact]
    public void KeepsListOrderPicksUpNewEntriesAndRebuildsForAReplacedList()
    {
        var index = new AppendOnlyIndex<Entry>(e => e.Key);
        var list = new List<Entry> { new(1, "a"), new(2, "b"), new(1, "c") };
        Assert.Equal(new[] { "a", "c" }, index.Of(list, 1).Select(e => e.Name));
        Assert.Empty(index.Of(list, 3));

        list.Add(new Entry(1, "d"));
        Assert.Equal(new[] { "a", "c", "d" }, index.Of(list, 1).Select(e => e.Name));

        // Neuer Spielstand: eine andere Liste ersetzt die alte.
        var loaded = new List<Entry> { new(1, "x") };
        Assert.Equal(new[] { "x" }, index.Of(loaded, 1).Select(e => e.Name));
        Assert.Empty(index.Of(loaded, 2));
    }
}
