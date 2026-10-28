using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>
/// Auf- und Abstiegsklauseln: Spielerverträge und Sponsorenverträge tragen die übliche Klausel der Spielklasse, in Verhandlungen
/// ist sie veränderbar und wirkt auf die Gehaltsforderung.
/// </summary>
public class ContractClausesTests
{
    [Fact]
    public void NewContractsCarryTheDivisionClause()
    {
        var s = TestData.NewSession();
        var w = s.World;
        foreach (var (div, rel, pro) in new[] { ("DE1", 40, 0), ("DE2", 50, 40), ("DE3", 70, 80) })
        {
            var club = w.Clubs[w.League(div).ClubIds[0]];
            Assert.All(w.SquadOf(club), p =>
            {
                Assert.Equal(rel, p.Contract!.RelegationPercent);
                Assert.Equal(pro, p.Contract.PromotionPercent);
            });
            var offer = Negotiations.SuggestContract(w.SquadOf(club).First(), club, w, TestData.Data);
            Assert.Equal(rel, offer.RelegationPercent);
        }
    }

    [Fact]
    public void FactorAppliesPerLevel()
    {
        Assert.Equal(0.6, ContractClauses.Factor(1, 40, 25), 6);
        Assert.Equal(0.36, ContractClauses.Factor(2, 40, 25), 6);
        Assert.Equal(1.25, ContractClauses.Factor(-1, 40, 25), 6);
        Assert.Equal(1.0, ContractClauses.Factor(0, 40, 25), 6);
    }

    /// <summary>Höherer Abschlag als üblich verteuert den Vertrag, ein höherer Aufstiegszuschlag macht ihn billiger.</summary>
    [Fact]
    public void ClausesChangeTheWageDemand()
    {
        var s = TestData.NewSession();
        var club = s.World.Clubs[s.World.League("DE2").ClubIds[0]];
        double F(int? rel, int? pro) => Negotiations.ClauseFactor(new ContractOffer { RelegationPercent = rel, PromotionPercent = pro }, club, s.World);
        Assert.Equal(1.0, F(null, null), 6);
        Assert.Equal(1.0, F(50, 40), 6);
        Assert.True(F(70, 40) > 1.05);
        Assert.True(F(50, 100) < 0.95);
    }

    [Fact]
    public void SponsorDealsCarryTheCountryClause()
    {
        var s = TestData.NewSession();
        var deals = s.World.Clubs.Values.Where(c => c.CountryCode == "DE").SelectMany(c => c.Sponsors.Concat(c.SponsorOffers)).ToList();
        Assert.NotEmpty(deals);
        Assert.All(deals, d => Assert.Equal((40, 25), (d.RelegationPercent, d.PromotionPercent)));
    }
}
