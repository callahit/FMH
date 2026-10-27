using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Scouting“.</summary>
public sealed partial class GameSession
{
    public const int WatchDays = ScoutingModule.WatchDays;
    public const int SearchDays = ScoutingModule.SearchDays;
    public const int OpponentDays = ScoutingModule.OpponentDays;
    public IReadOnlyList<Scout> HumanScouts => _scouting.HumanScouts;
    public int ScoutSlots(Club club) => _scouting.ScoutSlots(club);
    public string? HireScout(int quality) => _scouting.HireScout(quality);
    public void FireScout(Scout scout) => _scouting.FireScout(scout);
    public ScoutAssignment? AssignmentOf(Scout s) => _scouting.AssignmentOf(s);
    public int Knowledge(Player p) => _scouting.Knowledge(p);
    public (int min, int max) AttributeRange(Player p, Attr a) => _scouting.AttributeRange(p, a);
    public bool StrengthKnown(Player p) => _scouting.StrengthKnown(p);
    public string? AssignScout(Scout scout, ScoutTask task, int? playerId = null, string? leagueId = null, Position? position = null, int maxAge = 40) => _scouting.AssignScout(scout, task, playerId, leagueId, position, maxAge);
    public void CancelAssignment(Scout scout) => _scouting.CancelAssignment(scout);
    public ScoutReport MakeReport(Scout scout, Player p) => _scouting.MakeReport(scout, p);
    public ScoutReport? ReportFor(Player p) => _scouting.ReportFor(p);
}
