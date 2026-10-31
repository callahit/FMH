using Fmh.Core.Match;

namespace Fmh.SceneTree;

/// <summary>Matchengine, die ein angeschautes Spiel über den FM2002-Szenenbaum laufen lässt.</summary>
public sealed class SceneMatchEngine : IMatchEngine
{
    private readonly SceneTreeAsset _tree;
    private readonly SceneWeightOptions _options;

    public SceneMatchEngine(SceneTreeAsset? tree = null, SceneWeightOptions? options = null)
    {
        _tree = tree ?? SceneTreeAsset.LoadDefault();
        _options = options ?? SceneWeightOptions.Default;
    }

    public string Name => "FM2002-Szenenbaum";

    public IMatchSimulation Create(MatchSetup setup) => new SceneSimulation(setup, _tree, _options);
}
