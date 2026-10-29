using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>Kadergrößen je Spielklasse aus den Daten und zweite Mannschaften ohne eigene Kasse.</summary>
public class SquadTests
{
    [Theory]
    [InlineData(25)]
    [InlineData(29)]
    [InlineData(22)]
    public void TemplateHasTheRequestedSize(int size)
    {
        var template = PlayerGenerator.Template(size);
        Assert.Equal(size, template.Sum(t => t.count));
        Assert.All(template, t => Assert.True(t.count >= 1));
    }

    [Fact]
    public void SquadsFollowTheDivisionSize()
    {
        var w = TestData.NewSession().World;
        foreach (var (div, size) in new[] { ("DE1", 29), ("DE2", 28), ("DE3", 26) })
            Assert.All(w.League(div).ClubIds, id => Assert.Equal(size, w.Clubs[id].PlayerIds.Count));
        Assert.All(w.Leagues.Where(l => l.Level == 4).SelectMany(l => l.ClubIds), id => Assert.Equal(25, w.Clubs[id].PlayerIds.Count));
    }

    /// <summary>Fällt ein KI-Verein unter den Mindestkader der Spielklasse, verpflichtet er am nächsten Montag nach (Review F-06).</summary>
    [Fact]
    public void AiRefillsBelowTheMinimumSquad()
    {
        var s = TestData.NewSession(12);
        var w = s.World;
        var club = w.Clubs[w.League("DE3").ClubIds.First(id => id != s.HumanClub.Id)];
        var minimum = ClubRules.DivisionFinance(w, club).MinSquad;
        foreach (var p in w.SquadOf(club).Where(p => p.Loan is null).Take(club.PlayerIds.Count - 15).ToList())
        {
            club.PlayerIds.Remove(p.Id);
            p.ClubId = null;
            p.Contract = null;
        }
        Assert.Equal(15, club.PlayerIds.Count);
        for (var i = 0; i < 8; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
        Assert.True(club.PlayerIds.Count >= minimum, $"{club.PlayerIds.Count} Spieler, Mindestkader {minimum}");
    }

    /// <summary>Der Mindestkader muss einen Spieltagskader ergeben und darf den erlaubten Kader nicht übersteigen.</summary>
    [Fact]
    public void MinimumSquadIsValidated()
    {
        var de = System.Text.Json.JsonSerializer.Deserialize<Fmh.Core.Data.CountryConfig>(
            System.Text.Json.JsonSerializer.Serialize(TestData.Data.Countries.Single(c => c.Code == "DE"), Fmh.Core.Data.Json.Compact), Fmh.Core.Data.Json.Compact)!;
        de.Finance.Divisions["DE2"].MinSquad = 15;
        var errors = Fmh.Core.Data.DataValidator.Validate(new Fmh.Core.Data.GameData { Countries = new() { de }, Clubs = TestData.Data.Clubs, Names = TestData.Data.Names });
        Assert.Contains(errors, e => e.Contains("Mindestkader 'DE2'"));
    }

    /// <summary>Gehälter und Einnahmen der zweiten Mannschaft bucht der Hauptverein, gekennzeichnet mit ihrem Namen.</summary>
    [Fact]
    public void ReserveTeamsBookOnTheParentClub()
    {
        var s = TestData.NewSession(club: "lindenau");
        var w = s.World;
        var reserve = w.Clubs.Values.Single(c => c.ReserveOf == s.HumanClub.Id);
        var balance = reserve.Budget.Balance;
        TestData.PlayUntil(s, _ => w.Transactions.Any(t => t.TextRef?.Key == "tx.reserve"), 200);
        var booked = w.Transactions.Where(t => t.TextRef?.Key == "tx.reserve").ToList();
        Assert.All(booked, t => Assert.Equal(s.HumanClub.Id, t.ClubId));
        Assert.Contains(booked, t => t.Type == TransactionType.Wages && t.Amount < 0);
        Assert.Equal(balance, reserve.Budget.Balance);
        Assert.Empty(reserve.Finance.Season);
    }

    /// <summary>Verkauft eine zweite Mannschaft einen Spieler, fließt der Budgetanteil der Ablöse an den Hauptverein.</summary>
    [Fact]
    public void ReserveTransferFeesRaiseTheParentBudget()
    {
        var s = TestData.NewSession(7);
        var w = s.World;
        var reserve = w.Clubs.Values.First(c => c.ReserveOf is not null && c.ReserveOf != s.HumanClub.Id);
        var parent = w.Clubs[reserve.ReserveOf!.Value];
        var p = w.SquadOf(reserve).Where(x => x.Loan is null).OrderByDescending(x => x.Contract!.WeeklyWage).First();
        var budget = parent.Budget.TransferBudget;
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        var ask = Negotiations.AskingPrice(p, reserve, w, s.Data);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer { Fee = ask }).Kind);
        Assert.Equal(ResponseKind.Accept, s.SubmitContract(n!, n!.Contract).Kind);
        Assert.Equal(budget + ask / 2, parent.Budget.TransferBudget);
        Assert.Equal(0, reserve.Budget.TransferBudget);
    }
}
