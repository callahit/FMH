using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Jugend“.</summary>
public sealed partial class GameSession
{
    public const int ReleaseAge = YouthModule.ReleaseAge;
    public IReadOnlyList<Player> YouthOf(Club club) => _youth.YouthOf(club);
    public static YouthAgeGroup GroupOf(int age) => YouthModule.GroupOf(age);
    public YouthAgeGroup AgeGroup(Player p) => _youth.AgeGroup(p);
    public IReadOnlyList<Player> YouthGroup(Club club, YouthAgeGroup group) => _youth.YouthGroup(club, group);
    public int GroupCapacity(Club club, YouthAgeGroup group) => _youth.GroupCapacity(club, group);
    public int YouthCapacity(Club club) => _youth.YouthCapacity(club);
    public StaffMember? GroupCoach(Club club, YouthAgeGroup group) => _youth.GroupCoach(club, group);
    public long ReferenceAcademyBudget(Club club) => _youth.ReferenceAcademyBudget(club);
    public (double coaching, double facilities, double recruitment, double education) AcademyTargets(Club club) => _youth.AcademyTargets(club);
    public string? SetAcademyBudget(long budget, int coaching, int recruitment, int education) => _youth.SetAcademyBudget(budget, coaching, recruitment, education);
    public string? AssignYouthCoach(StaffMember coach, YouthAgeGroup group) => _youth.AssignYouthCoach(coach, group);
    public string? PromoteYouth(Player p) => _youth.PromoteYouth(p);
    public string? ReleaseYouth(Player p) => _youth.ReleaseYouth(p);
    public string? DemoteToYouth(Player p) => _youth.DemoteToYouth(p);
}
