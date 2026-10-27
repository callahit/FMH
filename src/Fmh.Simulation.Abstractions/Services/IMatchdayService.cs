using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Spielbetrieb“ für andere Module.</summary>
public interface IMatchdayService
{
    void InjurePlayer(Player p, string type, int days, bool notify);
}
