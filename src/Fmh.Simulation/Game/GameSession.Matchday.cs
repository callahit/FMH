using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Spielbetrieb“.</summary>
public sealed partial class GameSession
{
    public Fixture? PendingHumanFixture => _matchday.PendingHumanFixture;
    public void PlayOtherFixturesToday() => _matchday.PlayOtherFixturesToday();
    public void PlayInstant(Fixture f) => _matchday.PlayInstant(f);
    public IMatchSimulation StartMatch(Fixture f, bool humanControlled, bool sceneText = false) => _matchday.StartMatch(f, humanControlled, sceneText);
    public Weather WeatherFor(Fixture f) => _matchday.WeatherFor(f);
    public void FinishMatch(Fixture f, IMatchSimulation sim) => _matchday.FinishMatch(f, sim);
}
