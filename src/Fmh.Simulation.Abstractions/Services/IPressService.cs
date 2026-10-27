using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Presse“ für andere Module.</summary>
public interface IPressService
{
    bool StyleFits(List<MediaStyle> styles);
    /// <summary>Pressekonferenz ansetzen; bei Delegation antwortet der Pressesprecher.</summary>
    void SchedulePress(Fixture f, bool after);
    /// <summary>Antworten auswerten: Moral, Gegnermotivation, Vorstandsvertrauen, Medienruf, Fehden, Schlagzeile.</summary>
    void AnswerPress(IReadOnlyList<PressAnswer> answers, bool delegated = false);
    /// <summary>Moral eines Teams: Bruchteile wirken als Wahrscheinlichkeit für eine Stufe.</summary>
    void TeamMorale(Club club, double steps);
    /// <summary>Schlagzeile nach einem eigenen Spiel (nicht nach jedem, sonst nutzt sie sich ab).</summary>
    void MatchHeadline(Fixture f);
    /// <summary>Wöchentlich: Gerüchte und Spielerinterviews.</summary>
    void PressWeekly();
}
