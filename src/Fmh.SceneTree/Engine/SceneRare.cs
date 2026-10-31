using Fmh.Core.Model;

namespace Fmh.SceneTree;

/// <summary>Wann ein sonstiger Text erlaubt ist. Easter Eggs bleiben eine eigene Einstellung.</summary>
public static class SceneRare
{
    public static bool Allows(RareKind kind, Weather weather, bool floodlit, bool easterEggs) => kind switch
    {
        RareKind.Easter => easterEggs,
        RareKind.Thunder => weather is Weather.Rain or Weather.Wind,
        RareKind.Floodlight => floodlit,
        RareKind.Wet => weather is Weather.Rain or Weather.Snow,
        _ => true,
    };

    public static IReadOnlyList<string> RequiredTags { get; } =
    [
        "gegenstaende", "hund", "flitzer", "donner", "voegel", "elster", "flieger", "flutlicht",
        "feuerwerk", "megaphon", "banner", "hubschrauber", "hubschrauber-west", "schuhe-st", "schuhe-cd", "hose", "trikot",
    ];
}
