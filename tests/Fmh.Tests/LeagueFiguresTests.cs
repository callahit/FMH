using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>
/// Finanzen nach veröffentlichten Ligazahlen: Referenzwerte je Spielklasse in den Daten (DFL, DFB; Premier League nur nach
/// Deloitte, nicht amtlich), Zuordnung der Buchungsarten zu den DFL-Kategorien und Lizenzprüfung mit ausstehendem TV-Geld.
/// </summary>
public class LeagueFiguresTests
{
    [Fact]
    public void ReferenceFiguresComeWithSources()
    {
        DivisionFinance Div(string country, string id) => TestData.AllData.Countries.Single(c => c.Code == country).Finance.Divisions[id];
        var bl = Div("DE", "DE1").Reference!;
        Assert.True(bl.Official);
        Assert.Contains("DFL", bl.Source);
        // Summen passen zu den Kategorien.
        Assert.Equal(bl.Income["total"], bl.Income.Where(kv => kv.Key != "total").Sum(kv => kv.Value));
        Assert.Equal(bl.Expenses["total"], bl.Expenses.Where(kv => kv.Key != "total").Sum(kv => kv.Value));
        Assert.True(Div("DE", "DE2").Reference!.Official);
        Assert.Contains("DFB", Div("DE", "DE3").Reference!.Source);
        Assert.False(Div("ENG", "ENG1").Reference!.Official);
    }

    [Fact]
    public void ValidatorRejectsUnknownCategories()
    {
        var de = System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(
            System.Text.Json.JsonSerializer.Serialize(TestData.Data.Countries.Single(c => c.Code == "DE"), Json.Compact), Json.Compact)!;
        de.Finance.Divisions["DE1"].Reference!.Income["catering"] = 1;
        de.Finance.Divisions["DE2"].Reference!.Source = "";
        var errors = DataValidator.Validate(new GameData { Countries = new() { de }, Clubs = TestData.Data.Clubs, Names = TestData.Data.Names });
        Assert.Contains(errors, e => e.Contains("'catering'"));
        Assert.Contains(errors, e => e.Contains("'DE2'") && e.Contains("Quelle"));
    }

    /// <summary>Catering zählt wie im DFL-Bericht zu den sonstigen Erlösen; Transfers und Kredite bleiben außen vor.</summary>
    [Fact]
    public void TransactionsMapToReportCategories()
    {
        Assert.Equal("matchday", FinanceReference.Category(TransactionType.SeasonTickets));
        Assert.Equal("media", FinanceReference.Category(TransactionType.European));
        Assert.Equal("other", FinanceReference.Category(TransactionType.Catering));
        Assert.Equal("squad", FinanceReference.Category(TransactionType.StaffWages));
        Assert.Equal("operations", FinanceReference.Category(TransactionType.Operations));
        Assert.Null(FinanceReference.Category(TransactionType.TransferOut));
        Assert.Null(FinanceReference.Category(TransactionType.LoanPayout));
    }

    /// <summary>Der TV-Leistungsanteil fließt erst zum Saisonende; bis dahin zählt er bei der Lizenzprüfung als Forderung.</summary>
    [Fact]
    public void LicenseCountsTheOutstandingTvShare()
    {
        var s = TestData.NewSession();
        var w = s.World;
        var league = w.Leagues.Single(l => l.Id == "DE1");
        var div = s.DivisionFinance(w.Clubs[league.ClubIds[0]]);
        var receivables = league.ClubIds.Select(id => s.LicenseEquity(w.Clubs[id]) - s.Equity(w.Clubs[id])).ToList();
        Assert.All(receivables, r => Assert.True(r > 0));
        Assert.InRange(receivables.Sum(), div.TvPool * (1 - div.TvEqualShare) * 0.99, div.TvPool * (1 - div.TvEqualShare));
        w.SeasonFinished = true;
        Assert.All(league.ClubIds, id => Assert.Equal(s.Equity(w.Clubs[id]), s.LicenseEquity(w.Clubs[id])));
    }

    /// <summary>Das Nachwuchsbudget folgt dem Faktor der Spielklasse (DFL: rund 14 Mio. je Erstligist für Nachwuchs, Amateure, Frauen).</summary>
    [Fact]
    public void AcademyBudgetFollowsTheDivisionFactor()
    {
        var s = TestData.NewSession();
        var w = s.World;
        var bl = w.Leagues.Single(l => l.Id == "DE1").ClubIds.Select(id => w.Clubs[id]).ToList();
        Assert.InRange(bl.Average(c => (double)s.ReferenceAcademyBudget(c)), 7_000_000, 21_000_000);
    }
}
