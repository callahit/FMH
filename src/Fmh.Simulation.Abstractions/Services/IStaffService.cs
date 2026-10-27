using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Personal“ für andere Module.</summary>
public interface IStaffService
{
    /// <summary>Beste Fähigkeit einer Rolle im Verein (0 = unbesetzt).</summary>
    int StaffAbility(Club club, StaffRole role);
    /// <summary>Startpersonal nach Vereinsgröße; kleine Vereine besetzen nicht jede Stelle.</summary>
    void CreateStaff(Club club);
    /// <summary>Arbeitsmarkt für Personal: jedes Jahr frische Kandidaten.</summary>
    void RefreshStaffMarket();
    /// <summary>Überträgt Personal und Anlagen auf die Kennwerte, die Training und Jugend verwenden.</summary>
    void RefreshStaffEffects(Club club);
    /// <summary>Saisonwechsel: auslaufende Personalverträge. KI verlängert gute Leute, ersetzt schwache.</summary>
    void StaffSeasonStart();
}
