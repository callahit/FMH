using Fmh.Core;
using Fmh.Core.Data;

namespace Fmh.Tests;

/// <summary>Die Anzeigewährung ist global; diese Tests laufen deshalb nicht parallel zu anderen.</summary>
[CollectionDefinition("Anzeigewährung", DisableParallelization = true)]
public sealed class DisplayCurrencyCollection;

/// <summary>Euro ist Leitwährung: alle Beträge in Daten und Spiel sind Euro, Pfund und Dollar dienen nur der Anzeige.</summary>
[Collection("Anzeigewährung")]
public class CurrencyTests
{
    [Fact]
    public void AllCountriesUseTheBaseCurrency()
    {
        Assert.All(TestData.AllData.Countries, c => Assert.Equal(CurrencyConfig.Base, c.Currency));
        var rates = TestData.AllData.Currencies;
        Assert.Equal(1.0, rates.PerEuro("EUR"));
        Assert.InRange(rates.PerEuro("GBP"), 0.7, 1.0);
        Assert.InRange(rates.PerEuro("USD"), 0.9, 1.4);
    }

    [Fact]
    public void ValidatorRejectsOtherCurrencies()
    {
        var de = System.Text.Json.JsonSerializer.Deserialize<CountryConfig>(
            System.Text.Json.JsonSerializer.Serialize(TestData.Data.Countries.Single(c => c.Code == "DE"), Json.Compact), Json.Compact)!;
        de.Currency = "GBP";
        var data = new GameData { Countries = new() { de }, Clubs = TestData.Data.Clubs, Names = TestData.Data.Names };
        Assert.Contains(DataValidator.Validate(data), e => e.Contains("Leitwährung"));
    }

    /// <summary>Anzeige in Pfund: Beträge werden umgerechnet und mit dem Pfund-Code beschriftet; Eingaben rechnen zurück in Euro.</summary>
    [Fact]
    public void DisplayConvertsFromEuro()
    {
        try
        {
            Fmt.UseCurrency("GBP", 0.87);
            var shown = Fmt.Money(1_000_000);
            Assert.Contains("870", shown);
            Assert.Contains("GBP", shown);
            Assert.Equal(1_000_000, Math.Round(Fmt.FromDisplay(Fmt.ToDisplay(1_000_000))));
            Fmt.UseCurrency("EUR", 0.5);
            Assert.Equal(1.0, Fmt.DisplayRate);
        }
        finally { Fmt.UseCurrency(CurrencyConfig.Base, 1.0); }
    }
}
