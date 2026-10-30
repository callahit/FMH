using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Transferbedingungen, die nach dem Abschluss wirken (Befunde der Analyse, P0): Wechselfenster je Land für die KI,
/// erfolgsabhängige Zahlungen an den abgebenden Verein, Einsatzgarantie bei Leihen und die Verlängerungsoption.
/// </summary>
public class TransferTermsTests
{
    private static Player BuyTarget(GameSession s) =>
        s.World.Players.Values
            .Where(p => p.ClubId is { } c && c != s.HumanClub.Id && s.World.FindLeague(s.World.Clubs[c].LeagueId) is not null)
            .Where(p => !p.IsYouth && p.Loan is null && p.MainPosition != Position.GK && Negotiations.RefusalReason(p, s.HumanClub, s.World, s.Data) is null)
            .OrderBy(p => s.ValueOf(p))
            .First(p => s.ValueOf(p) > 100_000);

    private static bool HasMessage(GameSession s, string key) => s.World.Messages.Any(m => m.SubjectRef?.Key == key);

    // ------------------------------------------------------------------ Wechselfenster

    [Fact]
    public void AiClubsTradeOnlyInTheirOwnCountrysWindow()
    {
        // Eigene Datenkopie: Italiens Sommerfenster schließt früh, das deutsche des Managers bleibt bis Ende August offen.
        var data = TestData.LoadAll();
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 3, ClubKey = "nordheide", ManagerName = "Test" });
        world.Country("ITA").TransferWindows.SummerEnd = "07-10";
        var s = new GameSession(world, data, new TextMatchEngine());
        var close = new DateOnly(world.SeasonYear, 7, 10);
        TestData.PlayUntil(s, _ => s.World.Date >= new DateOnly(world.SeasonYear, 8, 31));

        bool Italian(int? id) => id is { } c && s.World.Clubs[c].CountryCode == "ITA";
        var late = s.World.Transfers.Where(t => t.Date > close && t.FromClubId is not null && t.FromClubId != s.HumanClub.Id
                                                && t.ToClubId != s.HumanClub.Id && (Italian(t.FromClubId) || Italian(t.ToClubId))).ToList();
        Assert.Empty(late);
        Assert.Contains(s.World.Transfers, t => t.Date <= close && (Italian(t.FromClubId) || Italian(t.ToClubId)));
        // Die übrigen Länder handeln im offenen Fenster weiter.
        Assert.Contains(s.World.Transfers, t => t.Date > close && t.FromClubId is not null && t.ToClubId != s.HumanClub.Id
                                                && s.World.Clubs[t.ToClubId!.Value].CountryCode == "DE");
    }

    // ------------------------------------------------------------------ Zusatzzahlungen an den Verkäufer

    [Fact]
    public void AddOnsRaiseTheOffersValueForTheSeller()
    {
        var s = TestData.NewSession(21);
        var p = BuyTarget(s);
        var plain = new ClubOffer { Fee = 500_000 };
        var addOns = new ClubOffer { Fee = 500_000, AppearanceBonus = 10_000, GoalBonus = 20_000 };
        Assert.Equal(0, TransferTerms.AddOnValue(plain, p));
        var expected = TransferTerms.ExpectedAppearances(p) * TransferTerms.AddOnYears * TransferTerms.AddOnDiscount
                       * (10_000 + 20_000 * TransferTerms.ExpectedGoalsPerAppearance(p));
        Assert.Equal(expected, TransferTerms.AddOnValue(addOns, p), 6);
        Assert.Equal(Negotiations.EffectiveValue(plain, p, s.World, s.Data) + expected, Negotiations.EffectiveValue(addOns, p, s.World, s.Data), 6);
    }

    [Fact]
    public void AddOnsArePaidToTheSellerForAppearancesAndGoals()
    {
        var s = TestData.NewSession(22);
        var p = BuyTarget(s);
        var seller = s.World.Clubs[p.ClubId!.Value];
        var (n, error) = s.StartNegotiation(p, DealType.Transfer);
        Assert.Null(error);
        var ask = Negotiations.AskingPrice(p, seller, s.World, s.Data);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer { Fee = ask, AppearanceBonus = 5_000, GoalBonus = 25_000 }).Kind);
        Assert.Equal(ResponseKind.Accept, s.SubmitContract(n!, n!.Contract).Kind);
        Assert.Equal(s.HumanClub.Id, p.ClubId);

        var conditions = s.World.TransferConditions.Where(c => c.PlayerId == p.Id).ToList();
        Assert.Equal(2, conditions.Count);
        Assert.All(conditions, c => Assert.Equal((s.HumanClub.Id, seller.Id, p.Contract!.Until), (c.PayerId, c.PayeeId, c.Until)));
        var apps = conditions.Single(c => c.Kind == TransferConditionKind.Appearance);
        var goals = conditions.Single(c => c.Kind == TransferConditionKind.Goal);

        // Spieler in die Startelf, damit er Einsätze sammelt.
        var lineup = s.HumanClub.Tactic.Lineup;
        lineup[lineup.Count - 1] = p.Id;
        var played = 0;
        TestData.PlayUntil(s, r =>
        {
            if (r.Reason == StopReason.HumanMatch) played++;
            if (!lineup.Contains(p.Id) && p.IsAvailable) lineup[lineup.Count - 1] = p.Id;
            return played >= 6;
        });

        Assert.True(apps.Count > 0);
        Assert.Equal(apps.Count * apps.Amount, apps.Paid);
        Assert.Equal(goals.Count * goals.Amount, goals.Paid);
        long Booked(int club, string key) => s.World.Transactions.Where(t => t.ClubId == club && t.TextRef?.Key == key).Sum(t => t.Amount);
        Assert.Equal(-(apps.Paid + goals.Paid), Booked(s.HumanClub.Id, "tx.addOnPaid"));
        Assert.Equal(apps.Paid + goals.Paid, Booked(seller.Id, "tx.addOnReceived"));

        // Verlässt der Spieler den Käufer, entfallen die Bedingungen am nächsten Tag.
        s.HumanClub.PlayerIds.Remove(p.Id);
        var idx = lineup.IndexOf(p.Id);
        if (idx >= 0) lineup[idx] = null;
        s.HumanClub.Tactic.Bench.Remove(p.Id);
        p.ClubId = seller.Id;
        seller.PlayerIds.Add(p.Id);
        var day = s.World.Date;
        TestData.PlayUntil(s, _ => s.World.Date > day.AddDays(1));
        Assert.DoesNotContain(s.World.TransferConditions, c => c.PlayerId == p.Id);
    }

    // ------------------------------------------------------------------ Einsatzgarantie

    [Fact]
    public void GuaranteeLowersTheRequiredWageShare()
    {
        var s = TestData.NewSession(23);
        var p = BuyTarget(s);
        var parent = s.World.Clubs[p.ClubId!.Value];
        p.Transfer.LoanListed = false;
        p.BirthDate = s.World.Date.AddYears(-25);
        var squad = s.World.SquadOf(parent).OrderByDescending(x => Ratings.Strength(x, s.Data.PositionProfiles)).ToList();
        if (squad.IndexOf(p) < 11) p.Transfer.LoanListed = true;
        var share = p.Transfer.LoanListed ? 50 : 80;
        var n = new Negotiation { PlayerId = p.Id, BuyerId = s.HumanClub.Id, SellerId = parent.Id, Type = DealType.Loan };
        n.Offer = new ClubOffer { LoanMonths = 12, WageSharePercent = share - TransferTerms.GuaranteeWageShareDiscount };
        Assert.Equal(ResponseKind.Counter, Negotiations.EvaluateClubOffer(n, p, parent, s.World, s.Data).Kind);
        n.Offer.PlayingTimeGuarantee = true;
        Assert.Equal(ResponseKind.Accept, Negotiations.EvaluateClubOffer(n, p, parent, s.World, s.Data).Kind);
    }

    [Fact]
    public void BreachedGuaranteeLetsTheParentRecallInWinterAndOptionsAreExercised()
    {
        var s = TestData.NewSession(24);
        var p = BuyTarget(s);
        var parent = s.World.Clubs[p.ClubId!.Value];
        p.Transfer.LoanListed = true;
        var (n, error) = s.StartNegotiation(p, DealType.Loan);
        Assert.Null(error);
        Assert.Equal(ResponseKind.Accept, s.SubmitClubOffer(n!, new ClubOffer { LoanMonths = 12, WageSharePercent = 50, PlayingTimeGuarantee = true }).Kind);
        var loan = p.Loan!;
        Assert.True(loan.PlayingTimeGuarantee);
        Assert.Equal(s.World.Date, loan.Start);
        Assert.Equal("neg.error.noRecall", s.RecallLoan(p));

        // Verlängerungsoption: eigener Spieler mit auslaufendem Vertrag; ein KI-Leistungsträger ebenso.
        var own = s.World.SquadOf(s.HumanClub).First(x => x.Loan is null && !x.IsYouth && x.Id != p.Id);
        own.Contract!.Until = new DateOnly(s.World.SeasonYear + 1, 6, 30);
        own.Contract.ExtensionOptionYears = 1;
        var aiClub = s.World.Clubs.Values.First(c => c.Id != s.HumanClub.Id && c.Id != parent.Id && s.World.FindLeague(c.LeagueId) is not null);
        var aiStar = s.World.SquadOf(aiClub).Where(x => x.Loan is null && !x.IsYouth && x.Age(s.World.Date) <= 30)
            .OrderByDescending(x => Ratings.Strength(x, s.Data.PositionProfiles)).First();
        aiStar.Contract!.Until = new DateOnly(s.World.SeasonYear + 1, 6, 30);
        aiStar.Contract.ExtensionOptionYears = 2;
        var aiUntil = aiStar.Contract.Until;

        // Leihspieler bleibt draußen: Pflichtspiele zählen, Einsätze kaum.
        var stillIn = true;
        TestData.PlayUntil(s, _ =>
        {
            if (p.ClubId == s.HumanClub.Id)
            {
                var slot = s.HumanClub.Tactic.Lineup.IndexOf(p.Id);
                if (slot >= 0) s.HumanClub.Tactic.Lineup[slot] = null;
                s.HumanClub.Tactic.Bench.Remove(p.Id);
            }
            stillIn = p.Loan is not null;
            return s.World.Date >= new DateOnly(s.World.SeasonYear, 11, 20) || !stillIn;
        });
        Assert.True(stillIn);
        Assert.True(loan.ClubMatches >= TransferTerms.GuaranteeMinMatches);
        Assert.True(TransferTerms.GuaranteeBreached(loan));

        Assert.True(s.CanExerciseExtensionOption(own));
        var until = own.Contract.Until;
        Assert.Null(s.ExerciseExtensionOption(own));
        Assert.Equal(until.AddYears(1), own.Contract.Until);
        Assert.Equal(0, own.Contract.ExtensionOptionYears);
        Assert.Equal("neg.error.noExtensionOption", s.ExerciseExtensionOption(own));
        Assert.True(HasMessage(s, "msg.optionExercised.subject"));

        // Bis ins Winterfenster: der KI-Stammverein holt den Spieler zurück, der KI-Verein hat seine Option gezogen.
        TestData.PlayUntil(s, _ => s.World.Date >= new DateOnly(s.World.SeasonYear + 1, 1, 10));
        Assert.Null(p.Loan);
        Assert.Equal(parent.Id, p.ClubId);
        Assert.True(HasMessage(s, "msg.guaranteeRecall.subject"));
        Assert.Equal(0, aiStar.Contract.ExtensionOptionYears);
        Assert.Equal(aiUntil.AddYears(2), aiStar.Contract.Until);
    }

    [Fact]
    public void ExtensionOptionCostsWageAndIsCapped()
    {
        var s = TestData.NewSession(25);
        var own = s.World.SquadOf(s.HumanClub).First(x => x.Loan is null && !x.IsYouth);
        own.Contract!.Until = new DateOnly(s.World.SeasonYear + 1, 6, 30);
        Assert.False(s.CanExerciseExtensionOption(own));
        var (n, error) = s.StartNegotiation(own, DealType.Renewal);
        Assert.Null(error);
        n!.Contract.WeeklyWage = 1;
        n.Contract.ExtensionOptionYears = 0;
        var plain = Negotiations.EvaluateContract(n, own, s.HumanClub, s.World, s.Data).Amount;
        n.Contract.ExtensionOptionYears = 2;
        var withOption = Negotiations.EvaluateContract(n, own, s.HumanClub, s.World, s.Data).Amount;
        Assert.True(withOption > plain, $"{withOption} > {plain}");
        var offer = n.Contract.Clone();
        offer.ExtensionOptionYears = 5;
        s.SubmitContract(n, offer);
        Assert.Equal(TransferTerms.MaxExtensionOptionYears, n.Contract.ExtensionOptionYears);
    }

    // ------------------------------------------------------------------ Spielstand

    [Fact]
    public void ConditionsAndLoanCountersSurviveSaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fmh-terms-" + Guid.NewGuid());
        try
        {
            var s = TestData.NewSession(26);
            var p = BuyTarget(s);
            s.World.TransferConditions.Add(new TransferCondition
            {
                PlayerId = p.Id, PayerId = s.HumanClub.Id, PayeeId = p.ClubId!.Value, Kind = TransferConditionKind.Goal,
                Amount = 30_000, Until = new DateOnly(s.World.SeasonYear + 2, 6, 30), Count = 3, Paid = 90_000,
            });
            p.Loan = new Loan { ParentClubId = p.ClubId!.Value, Start = s.World.Date, Until = s.World.Date.AddMonths(6), PlayingTimeGuarantee = true, ClubMatches = 7, Appearances = 2, GuaranteeBreachNotified = true };
            var path = Path.Combine(dir, "terms.fmh");
            Fmh.Core.Persistence.SqliteStore.Save(s.World, path);
            var w = Fmh.Core.Persistence.SqliteStore.Load(path);
            var c = Assert.Single(w.TransferConditions);
            Assert.Equal((p.Id, TransferConditionKind.Goal, 30_000L, 3, 90_000L), (c.PlayerId, c.Kind, c.Amount, c.Count, c.Paid));
            var l = w.Players[p.Id].Loan!;
            Assert.Equal((s.World.Date, 7, 2, true), (l.Start!.Value, l.ClubMatches, l.Appearances, l.GuaranteeBreachNotified));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
