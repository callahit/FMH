using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Jugend“ für andere Module.</summary>
public interface IYouthService
{
    IReadOnlyList<Player> YouthOf(Club club);
    /// <summary>Startwerte nach Vereinsgröße (neue Welt oder älterer Spielstand).</summary>
    void InitAcademy(Club club);
    /// <summary>Jugendtrainer ohne Altersstufe übernehmen die erste freie (U19, U17, U15).</summary>
    void AssignYouthCoaches(Club club);
    /// <summary>Saisonwechsel: Absolventen, Freigaben mit 19, Aussortieren bei vollen Altersstufen, Abbrecher.</summary>
    void YouthSeasonStart(Club club);
    void PromoteYouthInternal(Club club, Player p);
}
