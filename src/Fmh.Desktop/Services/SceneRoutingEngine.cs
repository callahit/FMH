using Fmh.Core.Match;

namespace Fmh.Desktop.Services;

/// <summary>
/// Hintergrundspiele und der normale Kommentar laufen über die schnelle Engine.
/// Nur ein angeschautes Spiel im Textmodus geht an den Szenenbaum. Ohne bereitgestellte Textmodus-Dateien
/// läuft auch dieses Spiel über die schnelle Engine.
/// </summary>
public sealed class SceneRoutingEngine : IMatchEngine
{
    private readonly IMatchEngine _fast;
    private readonly IMatchEngine? _scene;

    public SceneRoutingEngine(IMatchEngine fast, IMatchEngine? scene)
    {
        _fast = fast;
        _scene = scene;
    }

    public string Name => _fast.Name;

    /// <summary>Ob der Textmodus verfügbar ist.</summary>
    public bool HasScene => _scene is not null;

    public IMatchSimulation Create(MatchSetup setup) =>
        setup.UseSceneText && _scene is not null ? _scene.Create(setup) : _fast.Create(setup);
}
