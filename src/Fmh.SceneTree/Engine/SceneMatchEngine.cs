using Fmh.Core.Match;

namespace Fmh.SceneTree;

/// <summary>Matchengine, die ein angeschautes Spiel über den Szenenbaum des Textmodus laufen lässt.</summary>
public sealed class SceneMatchEngine : IMatchEngine
{
    private readonly SceneTreeAsset _tree;
    private readonly SceneWeightOptions _options;

    public SceneMatchEngine(SceneTreeAsset? tree = null, SceneWeightOptions? options = null)
    {
        _tree = tree ?? SceneTreeAsset.TryLoadDefault()
            ?? throw new FileNotFoundException("Textmodus-Dateien nicht gefunden (siehe docs/textmodus.md).");
        _options = options ?? SceneWeightOptions.Default;
    }

    /// <summary>Engine mit dem bereitgestellten Szenenbaum, oder null, wenn der Nutzer keine Dateien hinterlegt hat.</summary>
    public static SceneMatchEngine? TryCreate(SceneWeightOptions? options = null) =>
        SceneTreeAsset.TryLoadDefault() is { } tree ? new SceneMatchEngine(tree, options) : null;

    public string Name => "Szenenbaum";

    public IMatchSimulation Create(MatchSetup setup) => new SceneSimulation(setup, _tree, _options);
}
