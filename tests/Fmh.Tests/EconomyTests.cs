using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;

namespace Fmh.Tests;

/// <summary>
/// Realistische Wirtschaft je Spielklasse: Gehälter nach Stärke und Gehaltsniveau der Liga, Obergrenzen, Betriebskosten als
/// Anteil der Einnahmen und ein Marktniveau, an dem sich Gehälter und Marktwerte ausrichten.
/// </summary>
public class EconomyTests
{
    /// <summary>Zum Spielstart liegt das Ø-Jahresgehalt je Spielklasse im Korridor ±30 % um das Ziel aus den Daten.</summary>
    [Fact]
    public void StartingWagesMatchLeagueTargets()
    {
        var w = TestData.NewSession().World;
        foreach (var div in w.Leagues.GroupBy(l => l.Format.Id))
        {
            var clubs = div.SelectMany(l => l.ClubIds).Select(id => w.Clubs[id]).ToList();
            var finance = ClubRules.DivisionFinance(w, clubs[0]);
            var avg = clubs.SelectMany(c => w.SquadOf(c)).Average(p => p.Contract!.WeeklyWage * 52.0);
            Assert.InRange(avg / finance.TargetAnnualWage, 0.7, 1.3);
            Assert.All(clubs.SelectMany(c => w.SquadOf(c)), p => Assert.True(p.Contract!.WeeklyWage <= finance.MaxWeeklyWage));
        }
        // Abstand der Ligen wie real: Ein Erstligaspieler verdient im Schnitt ein Vielfaches eines Regionalligaspielers.
        double Avg(string div) => w.Leagues.Where(l => l.Format.Id == div).SelectMany(l => l.ClubIds)
            .SelectMany(id => w.SquadOf(w.Clubs[id])).Average(p => p.Contract!.WeeklyWage);
        Assert.True(Avg("DE1") > 5 * Avg("DE2") && Avg("DE2") > 2 * Avg("DE3") && Avg("DE3") > 4 * Avg("DE4"));
    }

    /// <summary>Gehaltsforderungen folgen dem Niveau der Spielklasse des Käufers und überschreiten nie deren Obergrenze.</summary>
    [Fact]
    public void WageDemandFollowsTheBuyersLeague()
    {
        var s = TestData.NewSession();
        var w = s.World;
        var star = w.Leagues.Single(l => l.Id == "DE1").ClubIds.SelectMany(id => w.SquadOf(w.Clubs[id]))
            .OrderByDescending(p => p.Contract!.WeeklyWage).First();
        var top = w.Clubs[w.League("DE2").ClubIds[0]];
        var small = w.Clubs[w.League("DE4N").ClubIds.First(id => w.Clubs[id].ReserveOf is null)];
        var high = Negotiations.WageDemand(star, top, SquadStatus.Star, w, TestData.Data);
        var low = Negotiations.WageDemand(star, small, SquadStatus.Star, w, TestData.Data);
        Assert.True(low < high);
        Assert.True(low <= ClubRules.DivisionFinance(w, small).MaxWeeklyWage);
        Assert.True(high <= ClubRules.DivisionFinance(w, top).MaxWeeklyWage);
    }

    /// <summary>Ein Vertragsangebot über dem Spitzengehalt der Spielklasse wird nicht angenommen, sondern auf die Grenze gekontert.</summary>
    [Fact]
    public void OffersAboveTheWageCapAreCountered()
    {
        var s = TestData.NewSession();
        var w = s.World;
        var club = w.Clubs[w.League("DE4N").ClubIds.First(id => w.Clubs[id].ReserveOf is null)];
        var p = w.SquadOf(club).First();
        var max = ClubRules.DivisionFinance(w, club).MaxWeeklyWage;
        var n = new Negotiation { Contract = new ContractOffer { Years = 2, WeeklyWage = max * 3, Status = SquadStatus.Regular } };
        var r = Negotiations.EvaluateContract(n, p, club, w, TestData.Data);
        Assert.Equal((ResponseKind.Counter, "neg.player.wageCap", (long)max), (r.Kind, r.TextKey, r.Amount));
    }

    /// <summary>Betriebskosten werden monatlich als Anteil der laufenden Einnahmen der Saison gebucht.</summary>
    [Fact]
    public void OperatingCostsFollowIncome()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        var share = ClubRules.DivisionFinance(s.World, club).OperatingCostShare;
        Assert.True(share > 0);
        // Tagesgenau bis zum Monatsersten, an dem die Betriebskosten gebucht werden.
        while (s.World.Date < new DateOnly(2026, 10, 1))
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
        var season = club.Finance.Season;
        var income = new[] { TransactionType.Tickets, TransactionType.SeasonTickets, TransactionType.Tv, TransactionType.Sponsorship,
            TransactionType.Merchandise, TransactionType.Catering, TransactionType.BuildingIncome, TransactionType.PrizeMoney, TransactionType.European }
            .Sum(t => Math.Max(0, season.GetValueOrDefault(t)));
        var operations = -season.GetValueOrDefault(TransactionType.Operations);
        // Gebucht wird zum Monatsersten vor den Einnahmen dieses Tages (Spieltag, Raten), die erst im nächsten Monat zählen.
        Assert.InRange(operations, (long)(income * share * 0.8), (long)(income * share) + 1);
    }

    /// <summary>
    /// Werden alle Spieler einer Liga stärker, steigt ihr Marktniveau: Gehaltsforderungen und Marktwerte bleiben dann stabil,
    /// statt mit der Stärke mitzuwachsen. Das Marktniveau übersteht Speichern und Laden.
    /// </summary>
    [Fact]
    public void MarketLevelAnchorsWagesAndSurvivesSaving()
    {
        var s = TestData.NewSession();
        var w = s.World;
        Assert.Contains("DE1", w.MarketLevels.Keys);
        Assert.Equal(w.MarketBases["DE1"], w.MarketLevels["DE1"]);
        var p = w.SquadOf(s.HumanClub).OrderByDescending(x => x.Contract!.WeeklyWage).First();
        var wage = MarketValue.FairWage(p, w, TestData.Data.PositionProfiles);
        var value = MarketValue.Of(p, w, TestData.Data.PositionProfiles);
        w.MarketLevels["DE1"] += 1.0;
        Assert.True(MarketValue.FairWage(p, w, TestData.Data.PositionProfiles) < wage * 0.6);
        Assert.True(MarketValue.Of(p, w, TestData.Data.PositionProfiles) < value * 0.6);

        var path = Path.Combine(Path.GetTempPath(), "fmh-eco-" + Guid.NewGuid() + ".db");
        try
        {
            SqliteStore.Save(w, path);
            var loaded = SqliteStore.Load(path);
            Assert.Equal(w.MarketLevels, loaded.MarketLevels);
            Assert.Equal(w.MarketBases, loaded.MarketBases);
        }
        finally { File.Delete(path); }
    }
}
