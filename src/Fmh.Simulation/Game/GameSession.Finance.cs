using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Finanzen“.</summary>
public sealed partial class GameSession
{
    public DivisionFinance DivisionFinance(Club club) => _finance.DivisionFinance(club);
    public long Equity(Club club) => _finance.Equity(club);
    public long LicenseEquity(Club club) => _finance.LicenseEquity(club);
    public long ExpectedIncome(Club club) => _finance.ExpectedIncome(club);
    public long EstimateIncome(Club club) => _finance.EstimateIncome(club);
    public long CreditLimit(Club club) => _finance.CreditLimit(club);
    public double InterestRate(Club club) => _finance.InterestRate(club);
    public long LoanCapacity(Club club) => _finance.LoanCapacity(club);
    public (int standing, int seating, int boxes) Attendance(Club home, Club away, Fixture f, Weather weather) => _finance.Attendance(home, away, f, weather);
    public bool IsTopMatch(Club home, Club away, Fixture f) => _finance.IsTopMatch(home, away, f);
    public string? TakeLoan(long amount, int years) => _finance.TakeLoan(amount, years);
    public string? RepayLoan(BankLoan loan) => _finance.RepayLoan(loan);
    public static double DepositRate(int months) => FinanceModule.DepositRate(months);
    public string? MakeDeposit(long amount, int months) => _finance.MakeDeposit(amount, months);
    public string? ShiftBudget(long transferDelta) => _finance.ShiftBudget(transferDelta);
    public string? SetTicketPrices(TicketPrices prices) => _finance.SetTicketPrices(prices);
    public void SetSeasonBudget(Club club) => _finance.SetSeasonBudget(club);
}
