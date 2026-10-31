using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public enum ResponseKind { Accept, Counter, Reject }

/// <summary>Antwort in einer Verhandlung, mit Text-Schlüssel und Werten für die Begründung.</summary>
public sealed record NegotiationResponse(ResponseKind Kind, string TextKey, long Amount = 0, params object[] Args);

/// <summary>
/// Verhandlungslogik (Kapitel 7): Angebot an den Verein (Ablöse, Raten, Boni, Weiterverkauf, Tausch),
/// danach Vertrag mit dem Spieler (Laufzeit, Gehalt, Handgeld, Prämien, Klauseln, Status).
/// Höchstens drei Runden, danach 14 Tage Sperre.
/// </summary>
public static class Negotiations
{
    public const int MaxRounds = 3;
    public const int LockDays = 14;

    public static string LockKey(int playerId, int clubId) => $"{playerId}:{clubId}";

    public static bool IsLocked(World world, int playerId, int clubId) =>
        world.NegotiationLocks.TryGetValue(LockKey(playerId, clubId), out var until) && until > world.Date;

    // ------------------------------------------------------------------ Verein

    /// <summary>Was der abgebende Verein mindestens verlangt.</summary>
    public static long AskingPrice(Player p, Club seller, World world, GameData data)
    {
        if (p.Transfer.AskingPrice is { } ask && p.Transfer.Listed) return ask;
        var value = MarketValue.Of(p, world, data.PositionProfiles);
        var factor = 1.15;
        if (p.Transfer.Listed) factor = 0.85;
        var squad = world.SquadOf(seller).OrderByDescending(x => Ratings.Strength(x, data.PositionProfiles)).ToList();
        var rank = squad.IndexOf(p);
        if (rank is >= 0 and < 4 && seller.Tactic.Lineup.Contains(p.Id)) factor = 1.6;
        if (p.Age(world.Date) <= 21 && p.Hidden.PotentialAbility - p.Hidden.CurrentAbility >= 25) factor = Math.Max(factor, 1.35);
        if (p.Contract is { } c && (c.Until.DayNumber - world.Date.DayNumber) < 365) factor = Math.Min(factor, 0.75);
        return MarketValue.Round(value * factor);
    }

    /// <summary>Wert eines Angebots aus Sicht des Verkäufers.</summary>
    public static double EffectiveValue(ClubOffer o, Player p, World world, GameData data)
    {
        var value = MarketValue.Of(p, world, data.PositionProfiles);
        TransferTerms.NormalizePaymentPlan(o, o.Fee);
        var fee = o.Fee * (1 - TransferTerms.InstallmentYearDiscount * TransferTerms.AveragePaymentDelayYears(o, o.Fee));
        // Zusatzzahlungen je Einsatz/Tor werden tatsächlich gebucht (TransferCondition) und nach erwarteter Wirkung bewertet.
        var bonus = TransferTerms.AddOnValue(o, p);
        // Gewinnbasis ist für den Verkäufer weniger wert als Brutto, weil der Gewinn kleiner oder null sein kann.
        var sellOnFactor = o.SellOnBasis == SellOnBasis.Profit ? 0.25 : 0.5;
        var sellOn = Math.Clamp(o.SellOnPercent, 0, 50) / 100.0 * value * sellOnFactor;
        var swap = o.SwapPlayerId is { } sid && world.Players.TryGetValue(sid, out var sp)
            ? MarketValue.Of(sp, world, data.PositionProfiles) * 0.8 : 0;
        return fee + bonus + sellOn + swap;
    }

    public static NegotiationResponse EvaluateClubOffer(Negotiation n, Player p, Club seller, World world, GameData data)
    {
        var o = n.Offer;
        if (n.Type == DealType.Loan) return EvaluateLoan(n, p, seller, world, data);
        TransferTerms.NormalizePaymentPlan(o, o.Fee);
        if (p.Contract?.ReleaseClause is { } clause && o.Fee >= clause && TransferTerms.IsImmediatePayment(o))
            return new NegotiationResponse(ResponseKind.Accept, "neg.club.releaseClause");

        var ask = AskingPrice(p, seller, world, data);
        var eff = EffectiveValue(o, p, world, data);
        if (eff >= ask) return new NegotiationResponse(ResponseKind.Accept, "neg.club.accept");
        var delay = Math.Max(0.01, 1 - TransferTerms.InstallmentYearDiscount * TransferTerms.AveragePaymentDelayYears(o, o.Fee));
        var counter = MarketValue.Round(ask / delay);
        if (n.ClubRounds >= MaxRounds) return new NegotiationResponse(ResponseKind.Reject, "neg.club.final");
        if (eff >= ask * 0.7) return new NegotiationResponse(ResponseKind.Counter, "neg.club.counter", counter, counter);
        return new NegotiationResponse(ResponseKind.Counter, "neg.club.farOff", counter, counter);
    }

    private static NegotiationResponse EvaluateLoan(Negotiation n, Player p, Club seller, World world, GameData data)
    {
        var o = n.Offer;
        var squad = world.SquadOf(seller).OrderByDescending(x => Ratings.Strength(x, data.PositionProfiles)).ToList();
        var key = squad.IndexOf(p) is >= 0 and < 11;
        if (key && !p.Transfer.LoanListed) return new NegotiationResponse(ResponseKind.Reject, "neg.loan.keyPlayer");
        var minShare = p.Transfer.LoanListed ? 50 : p.Age(world.Date) <= 21 ? 60 : 80;
        // Eine Einsatzgarantie sichert dem Stammverein Spielpraxis zu; dafür trägt er mehr vom Gehalt.
        if (o.PlayingTimeGuarantee) minShare = Math.Max(0, minShare - TransferTerms.GuaranteeWageShareDiscount);
        var covered = o.WageSharePercent + TransferTerms.WageShareFromLoanFee(o.LoanFee, MarketValue.Of(p, world, data.PositionProfiles));
        if (o.PurchaseFee is { } fee && fee < MarketValue.Of(p, world, data.PositionProfiles) * 0.9)
            return new NegotiationResponse(ResponseKind.Counter, "neg.loan.optionLow", MarketValue.Of(p, world, data.PositionProfiles));
        if (covered >= minShare) return new NegotiationResponse(ResponseKind.Accept, "neg.loan.accept");
        if (n.ClubRounds >= MaxRounds) return new NegotiationResponse(ResponseKind.Reject, "neg.club.final");
        return new NegotiationResponse(ResponseKind.Counter, "neg.loan.wageShare", minShare, minShare);
    }

    // ------------------------------------------------------------------ Spieler

    /// <summary>
    /// Gehaltsforderung pro Woche: Marktniveau der Stärke, angepasst an Reputation, Status, Ehrgeiz und das Gehaltsniveau der
    /// Spielklasse des Käufers. Das bisherige Gehalt hebt die Forderung höchstens auf 120 % (Verlängerung) bzw. 140 %
    /// (Wechsel) dieses Niveaus, damit sich Gehälter nicht mit jeder Verlängerung hochschaukeln.
    /// </summary>
    public static int WageDemand(Player p, Club buyer, SquadStatus status, World world, GameData data)
    {
        var fair = MarketValue.FairWage(p, world, data.PositionProfiles, buyer);
        var moving = p.ClubId != buyer.Id;
        double target = fair;
        var gap = p.Reputation - buyer.Reputation;
        target *= gap > 0 ? 1 + gap * 0.06 : 0.97;
        target *= status switch
        {
            SquadStatus.Star => 1.25,
            SquadStatus.Regular => 1.1,
            SquadStatus.Backup => 0.9,
            SquadStatus.Prospect => 0.8,
            _ => 1.0,
        };
        if (p.Hidden.Ambition >= 15) target *= 1.05;
        var div = ClubRules.DivisionFinance(world, buyer);
        target *= div.WageLevel;
        var current = p.Contract?.WeeklyWage ?? 0;
        // Mehr als die Spitzengehälter der Spielklasse kann der Verein nicht zahlen.
        var demand = Math.Min(Math.Max(target, Math.Min(current * (moving ? 1.1 : 1.0), target * (moving ? 1.4 : 1.2))), div.MaxWeeklyWage);
        if (p.IsYouth || p.Age(world.Date) < 18) demand = Math.Min(demand, fair * 0.6);
        return (int)(Math.Round(demand / 100) * 100);
    }

    /// <summary>Gründe, aus denen ein Spieler gar nicht verhandelt (Kapitel 7). null = gesprächsbereit.</summary>
    public static string? RefusalReason(Player p, Club buyer, World world, GameData data)
    {
        if (p.ClubId == buyer.Id) return null;
        if (p.Reputation >= buyer.Reputation + 6) return "refuse.reputation";
        var currentLevel = p.ClubId is { } cid ? CupLogic.Level(world, cid) : 99;
        var buyerLevel = CupLogic.Level(world, buyer.Id);
        if (buyerLevel > currentLevel && p.Reputation >= 9) return "refuse.leagueLevel";
        var strength = Ratings.Strength(p, data.PositionProfiles);
        var better = world.SquadOf(buyer).Count(x => x.MainPosition == p.MainPosition && Ratings.Strength(x, data.PositionProfiles) >= strength + 0.5);
        if (better >= 2 && p.Hidden.Ambition >= 13) return "refuse.competition";
        if (p.Hidden.Loyalty >= 18 && p.ClubId is { } own && world.Clubs[own].Reputation >= buyer.Reputation) return "refuse.loyalty";
        return null;
    }

    /// <summary>Erwarteter Status nach Stärke im neuen Kader.</summary>
    public static SquadStatus ExpectedStatus(Player p, Club buyer, World world, GameData data)
    {
        var strength = Ratings.Strength(p, data.PositionProfiles);
        var others = world.SquadOf(buyer).Where(x => x.Id != p.Id).Select(x => Ratings.Strength(x, data.PositionProfiles)).OrderByDescending(x => x).ToList();
        var rank = others.Count(x => x > strength);
        if (p.Age(world.Date) <= 20 && rank >= 14) return SquadStatus.Prospect;
        return rank switch { < 3 => SquadStatus.Star, < 11 => SquadStatus.Regular, < 16 => SquadStatus.Rotation, _ => SquadStatus.Backup };
    }

    public static (int min, int max) PreferredYears(Player p, DateOnly today)
    {
        var age = p.Age(today);
        if (age < 18) return (1, 3);
        return age switch { <= 23 => (3, 5), <= 29 => (2, 4), _ => (2, 3) };
    }

    public static NegotiationResponse EvaluateContract(Negotiation n, Player p, Club buyer, World world, GameData data)
    {
        var o = n.Contract;
        var age = p.Age(world.Date);
        if (age < 18 && o.Years > 3) return new NegotiationResponse(ResponseKind.Counter, "neg.player.minorYears", 3);
        if (o.Years is < 1 or > 5) return new NegotiationResponse(ResponseKind.Counter, "neg.player.years", 0);
        // Mehr als das Spitzengehalt der Spielklasse lässt das Gehaltsgefüge nicht zu.
        var max = ClubRules.DivisionFinance(world, buyer).MaxWeeklyWage;
        if (o.WeeklyWage > max) return new NegotiationResponse(ResponseKind.Counter, "neg.player.wageCap", max, max);

        var demand = (double)WageDemand(p, buyer, o.Status, world, data);
        var (minY, maxY) = PreferredYears(p, world.Date);
        if (o.Years < minY || o.Years > maxY) demand *= 1.06;
        var expected = ExpectedStatus(p, buyer, world, data);
        if ((int)o.Status > (int)expected + 1) demand *= 1.1;
        demand *= ClauseFactor(o, buyer, world);
        // Eine Verlängerungsoption des Vereins kostet den Spieler Freiheit und wird mit Gehalt aufgewogen.
        demand *= 1 + TransferTerms.ExtensionOptionWageFactor * Math.Clamp(o.ExtensionOptionYears, 0, TransferTerms.MaxExtensionOptionYears);
        if (o.ReleaseClause is not null) demand *= 0.97;

        var effective = o.WeeklyWage
                        + o.SigningFee / (52.0 * Math.Max(1, o.Years)) * 0.8
                        + (o.AppearanceBonus + o.GoalBonus * 0.4) * 0.5;
        effective *= 1 + o.YearlyRaisePercent / 100.0 * Math.Max(0, o.Years - 1) / 2 * 0.6;

        var wageDemand = (int)(Math.Round(demand / 100) * 100);
        if (effective >= demand * 0.97) return new NegotiationResponse(ResponseKind.Accept, "neg.player.accept");
        if (n.PlayerRounds >= MaxRounds) return new NegotiationResponse(ResponseKind.Reject, "neg.player.final");
        return new NegotiationResponse(ResponseKind.Counter, "neg.player.counter", wageDemand, wageDemand);
    }

    /// <summary>Beraterprovision: 5–15 % der Ablöse, je nach Reputation des Spielers.</summary>
    public static long AgentFee(Player p, long fee) => MarketValue.Round(fee * (0.05 + Math.Clamp(p.Reputation, 1, 20) / 20.0 * 0.10));

    /// <summary>Sinnvolles Startangebot für den Vertrag (Forderung des Spielers, erwarteter Status, bevorzugte Laufzeit).</summary>
    public static ContractOffer SuggestContract(Player p, Club buyer, World world, GameData data)
    {
        var status = ExpectedStatus(p, buyer, world, data);
        var (minY, maxY) = PreferredYears(p, world.Date);
        return new ContractOffer
        {
            Years = Math.Clamp((minY + maxY) / 2, 1, p.Age(world.Date) < 18 ? 3 : 5),
            Status = status,
            WeeklyWage = WageDemand(p, buyer, status, world, data),
            SigningFee = 0,
            RelegationPercent = ContractClauses.Defaults(world, buyer).relegation,
            PromotionPercent = ContractClauses.Defaults(world, buyer).promotion,
        };
    }

    /// <summary>
    /// Wirkung der Auf-/Abstiegsklausel auf die Gehaltsforderung: Ein höherer Abschlag als in der Spielklasse üblich kostet je
    /// Prozentpunkt 0,3 % mehr Grundgehalt, ein geringerer Aufstiegszuschlag je Punkt 0,1 %.
    /// </summary>
    public static double ClauseFactor(ContractOffer o, Club buyer, World world)
    {
        var (rel, pro) = ContractClauses.Defaults(world, buyer);
        var factor = 1 + 0.003 * ((o.RelegationPercent ?? rel) - rel) - 0.001 * ((o.PromotionPercent ?? pro) - pro);
        return Math.Clamp(factor, 0.85, 1.3);
    }
}
