using Fmh.Core.Match;

namespace Fmh.Desktop.Services;

/// <summary>
/// Hintergrundspiele und der normale Kommentar laufen über die schnelle Engine.
/// Nur ein angeschautes Spiel im Textmodus geht an den Szenenbaum.
/// </summary>
public sealed class SceneRoutingEngine : IMatchEngine
{
    private readonly IMatchEngine _fast;
    private readonly IMatchEngine _scene;

    public SceneRoutingEngine(IMatchEngine fast, IMatchEngine scene)
    {
        _fast = fast;
        _scene = scene;
    }

    public string Name => _fast.Name;

    public IMatchSimulation Create(MatchSetup setup) =>
        setup.UseSceneText ? _scene.Create(setup) : _fast.Create(setup);
}
