using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Xunit;

namespace Fmh.Tests;

/// <summary>Transferstrategien und Leihmarkt der KI (Review F-07).</summary>
public class MarketTests
{
    [Fact]
    public void ClubsGetAStrategyByLevelReputationAndBudget()
    {
        var s = TestData.NewSession(5);
        var w = s.World;
        var de1 = w.League("DE1").ClubIds.Select(id => w.Clubs[id]).OrderByDescending(c => c.Reputation).ToList();
        Assert.Equal(TransferStrategy.Elite, ClubStrategy.Of(w, s.Data, de1[0]));
        Assert.NotEqual(TransferStrategy.Elite, ClubStrategy.Of(w, s.Data, de1[^1]));
        var de3 = w.League("DE3").ClubIds.Select(id => w.Clubs[id]).ToList();
        Assert.All(de3, c => Assert.Equal(TransferStrategy.Borrower, ClubStrategy.Of(w, s.Data, c)));
        var amateur = w.Clubs.Values.First(c => w.FindLeague(c.LeagueId) is null or { Level: > 3 });
        Assert.Equal(TransferStrategy.Bargain, ClubStrategy.Of(w, s.Data, amateur));
        // Jede Strategie hat ein Profil in den Daten.
        Assert.All(Enum.GetValues<TransferStrategy>(), st => Assert.True(s.Data.TransferRules.Strategies.ContainsKey(st), st.ToString()));
    }

    [Fact]
    public void SummerWindowBringsSigningsAndLoansWithinTheLimits()
    {
        var s = TestData.NewSession(5);
        var w = s.World;
        var end = new DateOnly(w.SeasonYear, 9, 2);
        TestData.PlayUntil(s, _ => w.Date >= end);
        var season = w.Transfers.Where(t => t.SeasonYear == w.SeasonYear).ToList();
        var de1 = w.League("DE1").ClubIds.ToHashSet();
        // Schon im ersten Sommer wird gekauft (Review F-07: Saison 1 war fast leer).
        Assert.True(season.Count(t => !t.Loan && t.Fee > 0 && t.ToClubId is { } to && de1.Contains(to)) >= de1.Count / 2);
        Assert.True(season.Count(t => t.Loan) > 0);

        // Kein Verein überschreitet die Leihgrenzen (ausgenommene Spieler zählen nicht).
        var loans = w.Players.Values.Where(p => p.Loan is not null).ToList();
        foreach (var club in w.Clubs.Values)
        {
            int Count(Func<Player, bool> side, Func<Player, (Club lender, Club holder)> pair) => loans.Where(side).Count(p =>
            {
                var (lender, holder) = pair(p);
                var rules = LoanLimits.RulesFor(w, s.Data, lender, holder);
                return ReferenceEquals(rules, w.Country(club.CountryCode).Loans.Default) && !LoanLimits.IsExempt(p, lender, rules, p.Loan!.Start ?? w.Date);
            });
            (Club, Club) Pair(Player p) => (w.Clubs[p.Loan!.ParentClubId], w.Clubs[p.ClubId!.Value]);
            var (maxOut, maxIn) = w.Country(club.CountryCode).Loans.Default.Limits(w.SeasonYear);
            Assert.True(Count(p => p.Loan!.ParentClubId == club.Id, Pair) <= maxOut, $"{club.Name} verleiht zu viele");
            Assert.True(Count(p => p.ClubId == club.Id, Pair) <= maxIn, $"{club.Name} leiht zu viele aus");
        }
    }
}
