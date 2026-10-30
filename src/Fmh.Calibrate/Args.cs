namespace Fmh.Calibrate;

/// <summary>Einfacher Parser für --schlüssel [wert].</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new();

    public static Args Parse(string[] args)
    {
        var a = new Args();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) value = args[++i];
            a._values[key] = value;
        }
        return a;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
}
