using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Presse“.</summary>
public sealed partial class GameSession
{
    public const int PressQuestionsPerConference = PressModule.PressQuestionsPerConference;
    public void SchedulePress(Fixture f, bool after) => _press.SchedulePress(f, after);
    public void AnswerPress(IReadOnlyList<PressAnswer> answers, bool delegated = false) => _press.AnswerPress(answers, delegated);
}
