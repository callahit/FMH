using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Training“.</summary>
public sealed partial class GameSession
{
    public double Familiarity(Club club, string formationId) => _training.Familiarity(club, formationId);
    public void AddFamiliarity(Club club, string formationId, double amount) => _training.AddFamiliarity(club, formationId, amount);
    public static double IntensityFactor(TrainingIntensity i) => TrainingModule.IntensityFactor(i);
    public static double ConditionCost(TrainingBlock b, TrainingIntensity i) => TrainingModule.ConditionCost(b, i);
    public static long CampCost(Club club, int days) => TrainingModule.CampCost(club, days);
    public string? BookCamp(CampGoal goal, DateOnly start, int days, string location) => _training.BookCamp(goal, start, days, location);
    public void CancelCamp() => _training.CancelCamp();
}
