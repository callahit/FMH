using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Tests;

/// <summary>Should·Next E2: Anfrage, parallele Verhandlung, Bieterkonkurrenz, Deadline Day.</summary>
public class TransferMarketE2Tests
{
    private static Player BuyTarget(GameSession s) =>
        s.World.Players.Values
            .Where(p => p.ClubId is { } c && c != s.HumanClub.Id && s.World.FindLeague(s.World.Clubs[c].LeagueId) is not null)
            .Where(p => !p.IsYouth && p.Loan is null && Negotiations.RefusalReason(p, s.HumanClub, s.World, s.Data) is null)
            .OrderBy(p => s.ValueOf(p))
            .First(p => s.ValueOf(p) > 100_000);

    private static void WaitForEnquiry(GameSession s, Negotiation n)
    {
        Assert.Equal(NegotiationStage.Enquiry, n.Stage);
        Assert.NotNull(n.EnquiryDue);
        for (var i = 0; i < 10 && n.Stage == NegotiationStage.Enquiry; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
        Assert.NotEqual(NegotiationStage.Enquiry, n.Stage);
    }

    [Fact]
    public void EnquiryThenParallelTalksCompleteATransfer()
    {
        var s = TestData.NewSession(11);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        p.Transfer.Listed = true;
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        Assert.Equal(NegotiationStage.Enquiry, n!.Stage);
        WaitForEnquiry(s, n);
        Assert.Equal(NegotiationStage.Talks, n.Stage);
        Assert.True(Negotiations.CanSubmitClub(n));
        Assert.True(Negotiations.CanSubmitPlayer(n));

        var ask = Negotiations.AskingPrice(p, seller, s.World, s.Data);
        var club = s.SubmitClubOffer(n, new ClubOffer { Fee = ask });
        Assert.Equal(ResponseKind.Accept, club.Kind);
        Assert.True(n.ClubAgreed);
        var player = s.SubmitContract(n, n.Contract);
        Assert.Equal(ResponseKind.Accept, player.Kind);
        Assert.Equal(NegotiationStage.Completed, n.Stage);
        Assert.Equal(s.HumanClub.Id, p.ClubId);
    }

    [Fact]
    public void SellerPicksHigherOfferAmongTwoBidders()
    {
        var s = TestData.NewSession(12);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        p.Transfer.Listed = true;
        var ask = Negotiations.AskingPrice(p, seller, s.World, s.Data);

        var human = new Negotiation
        {
            Id = s.World.NextNegotiationId++, Type = DealType.Transfer, Stage = NegotiationStage.Talks,
            PlayerId = p.Id, BuyerId = s.HumanClub.Id, SellerId = seller.Id, Started = s.World.Date,
            Offer = new ClubOffer { Fee = ask },
            Contract = Negotiations.SuggestContract(p, s.HumanClub, s.World, s.Data),
        };
        var rivalClub = s.World.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.Id != seller.Id && c.ReserveOf is null
                                                        && c.Budget.TransferBudget >= ask * 2);
        var rival = new Negotiation
        {
            Id = s.World.NextNegotiationId++, Type = DealType.Transfer, Stage = NegotiationStage.Talks,
            PlayerId = p.Id, BuyerId = rivalClub.Id, SellerId = seller.Id, Started = s.World.Date,
            Offer = new ClubOffer { Fee = ask * 2 },
            Contract = Negotiations.SuggestContract(p, rivalClub, s.World, s.Data),
            PlayerAgreed = true,
        };
        s.World.Negotiations.Add(human);
        s.World.Negotiations.Add(rival);

        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(human, human.Offer.Clone()).Kind);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(rival, rival.Offer.Clone()).Kind);
        Assert.True(human.ClubAgreed && rival.ClubAgreed);

        // Bieterfenster künstlich ablaufen lassen und einen Markttag auslösen.
        human.ClubAgreedOn = s.World.Date.AddDays(-3);
        rival.ClubAgreedOn = s.World.Date.AddDays(-3);
        s.DelegateDecisions();
        for (var i = 0; i < 3; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason is StopReason.Decision or StopReason.Messages) s.DelegateDecisions();
            if (human.Stage == NegotiationStage.Failed) break;
        }

        Assert.Equal(NegotiationStage.Failed, human.Stage);
        Assert.True(s.World.NegotiationLocks.ContainsKey(Negotiations.LockKey(p.Id, s.HumanClub.Id)));
        Assert.True(rival.SellerChosen || rival.Stage == NegotiationStage.Completed);
    }

    [Fact]
    public void BuyerWithdrawDoesNotLockPlayer()
    {
        var s = TestData.NewSession(13);
        var p = BuyTarget(s);
        p.Transfer.Listed = true;
        var (n, _) = s.StartNegotiation(p, DealType.Transfer);
        WaitForEnquiry(s, n!);
        s.Withdraw(n!);
        Assert.Equal(NegotiationStage.Withdrawn, n!.Stage);
        var (again, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        Assert.NotNull(again);
    }

    [Fact]
    public void DeadlineDayPostsTickerOnWindowEnd()
    {
        var s = TestData.NewSession(14);
        var country = s.World.Country(s.HumanClub.CountryCode);
        var end = ScheduleGenerator.ParseDate(country.TransferWindows.SummerEnd, s.World.SeasonYear, country.Season.StartMonth);
        // Direkt am Deadline-Tag starten: ProcessDay läuft für World.Date, danach +1.
        s.World.Date = end;
        s.DelegateDecisions();
        var r = s.Advance(1);
        if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
        if (r.Reason is StopReason.Decision or StopReason.Messages) { /* Nachricht erwartet */ }
        Assert.Contains(s.World.Messages, m => m.SubjectRef?.Key == "msg.deadline.subject");
    }
}
