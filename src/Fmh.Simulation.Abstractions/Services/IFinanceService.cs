using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Finanzen“ für andere Module.</summary>
public interface IFinanceService
{
    DivisionFinance DivisionFinance(Club club);
    bool HasFinances(Club club);
    /// <summary>Eigenkapital: Kontostand plus Festgeld minus Restschuld der Kredite.</summary>
    long Equity(Club club);
    /// <summary>Eigenkapital plus noch ausstehender TV-Leistungsanteil nach aktuellem Tabellenplatz (Lizenzprüfung vor Saisonende).</summary>
    long LicenseEquity(Club club);
    /// <summary>Saisoneinnahmen (letzte Saison, sonst geschätzt) als Basis für Kreditrahmen und Budgets.</summary>
    long ExpectedIncome(Club club);
    /// <summary>Kreditrahmen der Hausbank: so weit darf das Konto ins Minus.</summary>
    long CreditLimit(Club club);
    long LoanCapacity(Club club);
    void Book(Club club, TransactionType type, long amount, TextRef text);
    /// <summary>Zuschauernachfrage 0–1 je Bereich aus Reputation, Tabelle, Gegner, Wetter, Fanstimmung, Preis und Umfeld.</summary>
    (int standing, int seating, int boxes) Attendance(Club home, Club away, Fixture f, Weather weather);
    bool IsTopMatch(Club home, Club away, Fixture f);
    /// <summary>Einnahmen eines Heimspiels: Tickets (ohne Dauerkarten), Logen, Catering, Merchandising, Umfeld.</summary>
    void MatchdayFinances(Fixture f, MatchResult result, int standing, int seating, int boxes);
    void RollSeasonFinances();
    /// <summary>Pokalprämie für das Erreichen einer Runde bzw. den Sieg.</summary>
    void CupPrize(Cup cup, int roundReached, IEnumerable<int> clubs);
    string? TakeLoan(long amount, int years);
    string? TakeLoan(Club club, long amount, int years, bool silent, long? capacity = null);
    /// <summary>Saisonbudget aus erwarteten Einnahmen: Gehaltsanteil je Schwierigkeit, Transferbudget aus dem Kontostand.</summary>
    void SetSeasonBudget(Club club);
}
