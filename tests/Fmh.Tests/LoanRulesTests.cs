using System.Text.Json;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;
using Xunit;

namespace Fmh.Tests;

/// <summary>Leihgrenzen nach FIFA-Transferstatut und nationalen Regeln (Review F-07).</summary>
public class LoanRulesTests
{
    /// <summary>Eigene Welt, weil die Tests Leihen eintragen.</summary>
    private static (World world, GameData data) NewWorld()
    {
        var data = TestData.LoadAll();
        return (WorldGenerator.Create(data, new NewGameOptions { Seed = 7, ClubKey = "nordheide", ManagerName = "Test" }), data);
    }

    private static List<Club> Clubs(World w, string league) => w.League(league).ClubIds.Select(id => w.Clubs[id]).ToList();

    /// <summary>Spieler des Vereins, der für keine Ausnahme in Frage kommt (älter als 23).</summary>
    private static List<Player> Seniors(World w, Club c) =>
        w.SquadOf(c).Where(p => p.Loan is null && p.Age(w.Date) >= 25).ToList();

    private static void Lend(World w, Player p, Club parent, Club borrower)
    {
        parent.PlayerIds.Remove(p.Id);
        borrower.PlayerIds.Add(p.Id);
        p.ClubId = borrower.Id;
        p.Loan = new Loan { ParentClubId = parent.Id, Start = w.Date, Until = new DateOnly(w.SeasonYear + 1, 6, 30) };
    }

    [Fact]
    public void GermanClubsLendAtMostSixAndThreeToTheSameClub()
    {
        var (w, data) = NewWorld();
        var de1 = Clubs(w, "DE1");
        var de2 = Clubs(w, "DE2");
        var parent = de1[0];
        var seniors = Seniors(w, parent);
        // Drei an denselben Verein, dann ist mit diesem Verein Schluss.
        for (var i = 0; i < 3; i++) Lend(w, seniors[i], parent, de2[0]);
        Assert.Equal("neg.error.loanClubLimit", LoanLimits.Check(w, data, seniors[3], parent, de2[0], 12));
        // Drei weitere an andere Vereine, danach ist das Kontingent von sechs erschöpft.
        for (var i = 3; i < 6; i++) Lend(w, seniors[i], parent, de2[i - 2]);
        Assert.Equal("neg.error.loanOutLimit", LoanLimits.Check(w, data, seniors[6], parent, de2[5], 12));

        // Selbst ausgebildete Spieler bis zur Saison, in der sie 21 werden, sind ausgenommen.
        var young = seniors[6];
        young.BirthDate = new DateOnly(w.SeasonYear - 20, 9, 1);
        young.Registration.Formative[parent.Id] = Registration.TrainedSeasons;
        Assert.Null(LoanLimits.Check(w, data, young, parent, de2[5], 12));
        // Ohne Ausbildung im Verein gilt die Ausnahme nicht.
        young.Registration.Formative.Remove(parent.Id);
        Assert.Equal("neg.error.loanOutLimit", LoanLimits.Check(w, data, young, parent, de2[5], 12));
    }

    [Fact]
    public void InternationalLoansHaveTheirOwnFifaQuota()
    {
        var (w, data) = NewWorld();
        var parent = Clubs(w, "DE1")[0];
        var de2 = Clubs(w, "DE2");
        var seniors = Seniors(w, parent);
        for (var i = 0; i < 6; i++) Lend(w, seniors[i], parent, de2[i]);
        // National ausgeschöpft, ins Ausland geht es weiter (eigenes FIFA-Kontingent).
        Assert.Equal("neg.error.loanOutLimit", LoanLimits.Check(w, data, seniors[6], parent, de2[6], 12));
        Assert.Same(data.TransferRules.InternationalLoans, LoanLimits.RulesFor(w, data, parent, Clubs(w, "ENG2")[0]));
        Assert.Null(LoanLimits.Check(w, data, seniors[6], parent, Clubs(w, "ENG2")[0], 12));
    }

    [Fact]
    public void PremierLeagueClubsBorrowAtMostTwoFromEachOther()
    {
        var (w, data) = NewWorld();
        var eng1 = Clubs(w, "ENG1");
        var borrower = eng1[0];
        Lend(w, Seniors(w, eng1[1])[0], eng1[1], borrower);
        // Höchstens einer vom selben Premier-League-Verein.
        Assert.Equal("neg.error.loanClubLimit", LoanLimits.Check(w, data, Seniors(w, eng1[1])[0], eng1[1], borrower, 12));
        Lend(w, Seniors(w, eng1[2])[0], eng1[2], borrower);
        Assert.Equal("neg.error.loanInLimit", LoanLimits.Check(w, data, Seniors(w, eng1[3])[0], eng1[3], borrower, 12));
        // Leihen aus der Championship zählen nach den allgemeinen Regeln.
        var eng2 = Clubs(w, "ENG2")[0];
        Assert.Null(LoanLimits.Check(w, data, Seniors(w, eng2)[0], eng2, borrower, 12));
    }

    [Fact]
    public void ItalyExemptsUnder23AndCountsThemBetweenTheSameClubs()
    {
        var (w, data) = NewWorld();
        var parent = Clubs(w, "ITA1")[0];
        var borrower = Clubs(w, "ITA2")[0];
        var rules = LoanLimits.RulesFor(w, data, parent, borrower);
        Assert.Equal((9, 9), rules.Limits(2026));
        Assert.Equal((8, 8), rules.Limits(2027));

        var squad = w.SquadOf(parent).ToList();
        var young = squad.Take(4).ToList();
        foreach (var p in young) { p.BirthDate = new DateOnly(w.SeasonYear - 21, 3, 1); p.Registration.Formative.Clear(); }
        Assert.True(LoanLimits.IsExempt(young[0], parent, rules, w.Date));
        for (var i = 0; i < 3; i++) Lend(w, young[i], parent, borrower);
        // Unter 23 ohne Ausbildung im Verein ausgenommen, aber höchstens drei zwischen denselben Vereinen.
        Assert.Equal("neg.error.loanClubLimit", LoanLimits.Check(w, data, young[3], parent, borrower, 12));
        Assert.Null(LoanLimits.Check(w, data, young[3], parent, Clubs(w, "ITA2")[1], 12));
        // Länger als 24 Monate (MaxMonths) geht nicht.
        Assert.Equal("neg.error.loanMonths", LoanLimits.Check(w, data, young[3], parent, Clubs(w, "ITA2")[1], 30));
    }

    [Fact]
    public void HumanLoanRequestsRespectTheLimits()
    {
        var data = TestData.LoadAll();
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 7, ClubKey = "nordheide", ManagerName = "Test" });
        var s = new GameSession(world, data, new TextMatchEngine());
        var human = s.HumanClub;
        var parent = world.Clubs.Values.First(c => c.CountryCode == human.CountryCode && c.Id != human.Id && Seniors(world, c).Count > 8);
        var lenders = world.Clubs.Values.Where(c => c.CountryCode == human.CountryCode && c.Id != human.Id && c.Id != parent.Id).Take(6).ToList();
        foreach (var lender in lenders) Lend(world, Seniors(world, lender)[0], lender, human);
        var (n, error) = s.StartNegotiation(Seniors(world, parent)[0], DealType.Loan);
        Assert.Null(n);
        Assert.Equal("neg.error.loanInLimit", error);
    }

    [Fact]
    public void LoansFromOlderSavesHaveNoStartDate()
    {
        var loan = JsonSerializer.Deserialize<Loan>("{\"parentClubId\":3,\"until\":\"2027-06-30\",\"wageSharePercent\":50}", Json.Options)!;
        Assert.Null(loan.Start);
        Assert.Equal(3, loan.ParentClubId);
    }

    [Fact]
    public void EveryCountryHasSourcedLoanRules()
    {
        var data = TestData.LoadAll();
        Assert.Empty(DataValidator.Validate(data));
        foreach (var c in data.Countries) Assert.False(string.IsNullOrWhiteSpace(c.Loans.Default.Source), c.Code);
        var broken = TestData.LoadAll();
        broken.TransferRules.InternationalLoans.Source = null;
        broken.TransferRules.InternationalLoans.MaxMonths = 0;
        Assert.Equal(2, DataValidator.Validate(broken).Count(e => e.StartsWith("Internationale Leihregeln")));
    }
}
