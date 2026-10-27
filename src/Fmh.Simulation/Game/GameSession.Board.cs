using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Vorstand und Karriere“.</summary>
public sealed partial class GameSession
{
    public static string[] AchievementIds => BoardModule.AchievementIds;
    public void CreateBoard(Club club) => _board.CreateBoard(club);
    public SeasonGoal ExpectedGoal(Club club) => _board.ExpectedGoal(club);
    public int GoalPosition(Club club, SeasonGoal goal) => _board.GoalPosition(club, goal);
    public IReadOnlyList<SeasonGoal> GoalChoices(Club club) => _board.GoalChoices(club);
    public void ChooseSeasonGoal(SeasonGoal goal) => _board.ChooseSeasonGoal(goal);
    public static string[] BoardRequests => BoardModule.BoardRequests;
    public TextRef BoardRequest(string kind) => _board.BoardRequest(kind);
    public void CreateFans(Club club) => _board.CreateFans(club);
    public static CoachingLicense StartLicense(int background) => BoardModule.StartLicense(background);
    public CoachingLicense RequiredLicense(Club club) => _board.RequiredLicense(club);
    public static int CourseReputation(CoachingLicense next) => BoardModule.CourseReputation(next);
    public string? StartCourse() => _board.StartCourse();
    public bool AwaitingNewJob => _board.AwaitingNewJob;
    public void Resign() => _board.Resign();
    public string? AcceptJob(JobOffer offer) => _board.AcceptJob(offer);
    public void DeclineJob(JobOffer offer) => _board.DeclineJob(offer);
}
