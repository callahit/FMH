using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Personal“.</summary>
public sealed partial class GameSession
{
    public static int BaseSlots(StaffRole role) => StaffModule.BaseSlots(role);
    public int StaffSlots(Club club, StaffRole role) => _staff.StaffSlots(club, role);
    public IReadOnlyList<StaffMember> StaffOf(Club club) => _staff.StaffOf(club);
    public IEnumerable<StaffMember> StaffMarket => _staff.StaffMarket;
    public int StaffAbility(Club club, StaffRole role) => _staff.StaffAbility(club, role);
    public static int StaffWage(int ability) => StaffModule.StaffWage(ability);
    public void CreateStaff(Club club) => _staff.CreateStaff(club);
    public string? HireStaff(StaffMember s, int years = 2) => _staff.HireStaff(s, years);
    public string? FireStaff(StaffMember s) => _staff.FireStaff(s);
    public static long Severance(StaffMember s, DateOnly today) => StaffModule.Severance(s, today);
    public void RefreshStaffEffects(Club club) => _staff.RefreshStaffEffects(club);
}
