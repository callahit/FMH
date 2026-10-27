namespace Fmh.Core;

/// <summary>
/// Deterministischer Zufallsgenerator (xoshiro256**). Unabhängig von der .NET-Version,
/// damit gleicher Seed + gleiche Eingaben immer dasselbe Ergebnis liefern (Kapitel 12, Determinismus).
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        var sm = seed;
        _s0 = SplitMix(ref sm);
        _s1 = SplitMix(ref sm);
        _s2 = SplitMix(ref sm);
        _s3 = SplitMix(ref sm);
    }

    private Rng() { }

    public static Rng FromState(ulong[] state)
    {
        if (state.Length != 4) throw new ArgumentException("Rng-Zustand braucht 4 Werte.", nameof(state));
        return new Rng { _s0 = state[0], _s1 = state[1], _s2 = state[2], _s3 = state[3] };
    }

    public ulong[] GetState() => new[] { _s0, _s1, _s2, _s3 };

    /// <summary>Kombiniert zwei Werte zu einem neuen Seed (z. B. Welt-Seed + Spiel-Id).</summary>
    public static ulong Mix(ulong a, ulong b)
    {
        var x = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
        return SplitMix(ref x);
    }

    private static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        var result = RotL(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    /// <summary>Gleichverteilt in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Gleichverteilt in [0, maxExclusive).</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextDouble() * maxExclusive);
    }

    /// <summary>Gleichverteilt in [min, maxInclusive].</summary>
    public int Next(int min, int maxInclusive) => min + Next(maxInclusive - min + 1);

    public double Range(double min, double max) => min + NextDouble() * (max - min);

    public bool Chance(double p) => NextDouble() < p;

    public double Normal(double mean, double sd)
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];

    /// <summary>Wählt einen Index proportional zu den Gewichten; negative Gewichte zählen als 0.</summary>
    public int WeightedIndex(IReadOnlyList<double> weights)
    {
        double sum = 0;
        foreach (var w in weights) sum += Math.Max(0, w);
        if (sum <= 0) return Next(weights.Count);
        var r = NextDouble() * sum;
        for (var i = 0; i < weights.Count; i++)
        {
            r -= Math.Max(0, weights[i]);
            if (r < 0) return i;
        }
        return weights.Count - 1;
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
