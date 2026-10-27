using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Vorstand und Karriere“ für andere Module.</summary>
public interface IBoardService
{
    void CreateBoard(Club club);
    /// <summary>Erwartung des Umfelds aus der Reputationsrangfolge in der Liga.</summary>
    SeasonGoal ExpectedGoal(Club club);
    /// <summary>Tabellenplatz, der für das Saisonziel mindestens erreicht werden muss.</summary>
    int GoalPosition(Club club, SeasonGoal goal);
    /// <summary>Saisonziel vereinbaren: ehrgeizige Ziele bringen mehr Budget und höhere Erwartungen.</summary>
    void ChooseSeasonGoal(SeasonGoal goal);
    void OfferSeasonGoal();
    void ChangeTrust(Club club, double delta);
    /// <summary>Nach jedem Spiel des Menschen: Vertrauen, Fans, Ultimatum, Karriere, Erfolge.</summary>
    void AfterHumanMatch(Fixture f);
    /// <summary>Wöchentlich: Tabellenplatz gegen Saisonziel, Finanzen, Jugend; Fanstimmung.</summary>
    void BoardWeekly();
    /// <summary>Saisonende: Bewertung des Saisonziels durch den Vorstand.</summary>
    bool EvaluateSeason(Club club, int position);
    void CreateFans(Club club);
    /// <summary>Vor Topspielen zu Hause: Choreografie der Fans (Kapitel 10).</summary>
    void ChoreoBeforeMatch(Fixture f);
    /// <summary>Publikumslieblinge der neuen Saison: die prägenden Spieler der letzten.</summary>
    void UpdateFavourites(Club club);
    /// <summary>Jahreshauptversammlung im November: Stimmung, kritische Fragen, Abstimmung über Großprojekte.</summary>
    void AnnualMeeting();
    void Achieve(string id);
    /// <summary>Entlassung: Station endet, Jobangebote kleinerer Vereine (Pflicht-Entscheidung).</summary>
    void SackManager(TextRef reason);
    bool AwaitingNewJob { get; }
    /// <summary>Jobangebot annehmen: der Manager wechselt den Verein, der alte wird von der KI geführt.</summary>
    string? AcceptJob(JobOffer offer);
    void StartStation(Club club);
    /// <summary>Saisonende: Titel und Erfolge, Jobangebote nach guter Saison, Vertragsende des Managers.</summary>
    void CareerSeasonEnd(Club club, int position, bool goalReached, bool promoted, bool champion);
}
