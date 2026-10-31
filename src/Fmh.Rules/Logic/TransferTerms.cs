using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Regeln für Vertrags- und Transferbedingungen, die über den Abschluss hinaus wirken: erfolgsabhängige Zahlungen an den
/// abgebenden Verein, Einsatzgarantie bei Leihen und die Verlängerungsoption des Vereins.
/// </summary>
public static class TransferTerms
{
    /// <summary>Einsatzgarantie: Anteil der Pflichtspiele des ausleihenden Vereins, in denen der Spieler mindestens spielen muss.</summary>
    public const int GuaranteePercent = 50;

    /// <summary>Erst ab so vielen Pflichtspielen während der Leihe gilt eine Einsatzgarantie als verfehlt.</summary>
    public const int GuaranteeMinMatches = 6;

    /// <summary>Wie viele Prozentpunkte Gehaltsanteil der Stammverein für eine Einsatzgarantie nachlässt.</summary>
    public const int GuaranteeWageShareDiscount = 20;

    /// <summary>Höchstens so viele Jahre darf eine Verlängerungsoption des Vereins umfassen.</summary>
    public const int MaxExtensionOptionYears = 2;

    /// <summary>Summe aller Weiterverkaufsrechte eines Spielers, in Prozent.</summary>
    public const int MaxSellOnPercent = 50;

    /// <summary>Höchstens so viele spätere Ablöseraten.</summary>
    public const int MaxInstallments = 8;

    /// <summary>Höchstens so viele Monate bis zur letzten Rate (vier Saisons).</summary>
    public const int MaxInstallmentMonths = 48;

    /// <summary>Erlaubte Leihdauern in Monaten.</summary>
    public static readonly int[] LoanMonthOptions = { 6, 12, 18, 24 };

    /// <summary>Abschlag je Jahr mittlerer Zahlungsverzögerung (Verkäufer bevorzugt Sofortzahlung).</summary>
    public const double InstallmentYearDiscount = 0.04;

    /// <summary>Aufschlag auf die Gehaltsforderung je Optionsjahr (der Spieler gibt Freiheit auf).</summary>
    public const double ExtensionOptionWageFactor = 0.025;

    /// <summary>Abschlag auf erfolgsabhängige Zahlungen gegenüber fester Ablöse (Unsicherheit, spätere Zahlung).</summary>
    public const double AddOnDiscount = 0.5;

    /// <summary>Jahre, über die ein Verkäufer erfolgsabhängige Zahlungen schätzt (übliche Vertragslaufzeit).</summary>
    public const int AddOnYears = 3;

    // ------------------------------------------------------------------ Zusatzzahlungen an den Verkäufer

    /// <summary>Erwartete Pflichtspiel-Einsätze je Saison: Durchschnitt der bisherigen Saisons, sonst 20; höchstens 34.</summary>
    public static double ExpectedAppearances(Player p)
    {
        var seasons = p.History.Where(h => h.Appearances > 0).TakeLast(3).ToList();
        if (seasons.Count == 0) return 20;
        return Math.Clamp(seasons.Average(h => h.Appearances), 5, 34);
    }

    /// <summary>Erwartete Tore je Einsatz: aus der Laufbahn (ab 10 Einsätzen), sonst nach Position.</summary>
    public static double ExpectedGoalsPerAppearance(Player p)
    {
        var apps = p.History.Sum(h => h.Appearances) + p.SeasonStats.Appearances;
        var goals = p.History.Sum(h => h.Goals) + p.SeasonStats.Goals;
        if (apps >= 10) return Math.Clamp(goals / (double)apps, 0, 1.2);
        return p.MainPosition switch
        {
            Position.ST => 0.35,
            Position.AML or Position.AMR or Position.AMC => 0.2,
            Position.ML or Position.MR or Position.MC => 0.1,
            Position.GK => 0,
            _ => 0.04,
        };
    }

    /// <summary>
    /// Wert der erfolgsabhängigen Zahlungen eines Angebots aus Sicht des Verkäufers: erwartete Einsätze und Tore über
    /// <see cref="AddOnYears"/> Jahre, mit <see cref="AddOnDiscount"/> abgezinst.
    /// </summary>
    public static double AddOnValue(ClubOffer o, Player p)
    {
        if (o.AppearanceBonus <= 0 && o.GoalBonus <= 0) return 0;
        var apps = ExpectedAppearances(p) * AddOnYears;
        var goals = apps * ExpectedGoalsPerAppearance(p);
        return (Math.Max(0, o.AppearanceBonus) * apps + Math.Max(0, o.GoalBonus) * goals) * AddOnDiscount;
    }

    // ------------------------------------------------------------------ Einsatzgarantie (Leihe)

    /// <summary>Einsatzgarantie verfehlt: genug Pflichtspiele, aber weniger als <see cref="GuaranteePercent"/> % Einsätze.</summary>
    public static bool GuaranteeBreached(Loan loan) =>
        loan.PlayingTimeGuarantee && loan.ClubMatches >= GuaranteeMinMatches && loan.Appearances * 100 < loan.ClubMatches * GuaranteePercent;

    // ------------------------------------------------------------------ Verlängerungsoption

    /// <summary>Stichtag, bis zu dem der Verein die Option ziehen kann: der Tag vor dem Vorvertrags-Stichtag seines Landes.</summary>
    public static DateOnly OptionDeadline(World world, Club club)
    {
        var country = world.Country(club.CountryCode);
        return ScheduleGenerator.ParseDate(country.TransferWindows.PreContractFrom, world.SeasonYear, country.Season.StartMonth).AddDays(-1);
    }

    /// <summary>
    /// Verein, dem der Vertrag gehört: bei einer Leihe der Stammverein, sonst der aktuelle Verein.
    /// Die Verlängerungsoption steht diesem Verein zu, auch solange der Spieler verliehen ist.
    /// </summary>
    public static int? ContractingClubId(Player p) => p.Loan is { } loan ? loan.ParentClubId : p.ClubId;

    /// <summary>Option ziehbar: Vertrag mit Option endet in dieser Saison, Stichtag nicht überschritten, kein Vorvertrag.</summary>
    public static bool CanExerciseOption(Player p, World world)
    {
        if (p.Contract is not { ExtensionOptionYears: > 0, Youth: false } c || ContractingClubId(p) is not { } cid || p.PreContract is not null) return false;
        if (!world.Clubs.ContainsKey(cid)) return false;
        if (c.Until > new DateOnly(world.SeasonYear + 1, 6, 30)) return false;
        return world.Date <= OptionDeadline(world, world.Clubs[cid]);
    }

    // ------------------------------------------------------------------ Weiterverkauf und Leihgebühr

    /// <summary>Wirksame Rechte: die Liste, sonst das einzelne Feld älterer Spielstände.</summary>
    public static IReadOnlyList<SellOnRight> ActiveSellOns(TransferStatus status)
    {
        if (status.SellOns is { Count: > 0 }) return status.SellOns;
        if (status.SellOnClubId is { } id && status.SellOnPercent > 0)
            return new[] { new SellOnRight { ClubId = id, Percent = Math.Clamp(status.SellOnPercent, 0, MaxSellOnPercent) } };
        return Array.Empty<SellOnRight>();
    }

    /// <summary>Übernimmt ein einzelnes Alt-Recht in die Liste, damit weitere Rechte daneben stehen können.</summary>
    public static void MaterializeSellOns(TransferStatus status)
    {
        if (status.SellOns is { Count: > 0 }) return;
        status.SellOns = new List<SellOnRight>();
        if (status.SellOnClubId is { } id && status.SellOnPercent > 0)
            status.SellOns.Add(new SellOnRight { ClubId = id, Percent = Math.Clamp(status.SellOnPercent, 0, MaxSellOnPercent) });
    }

    /// <summary>
    /// Freier Anteil bis <see cref="MaxSellOnPercent"/>, wenn das Recht von <paramref name="clubId"/> neu gesetzt wird.
    /// Ein bestehendes Recht desselben Vereins wird dabei ersetzt.
    /// </summary>
    public static int SellOnRoom(TransferStatus status, int clubId)
    {
        MaterializeSellOns(status);
        var used = status.SellOns!.Where(r => r.ClubId != clubId).Sum(r => r.Percent);
        return Math.Max(0, MaxSellOnPercent - used);
    }

    /// <summary>10 % des Marktwerts ersetzen 10 Prozentpunkte Gehaltsanteil, den der Stammverein bei einer Leihe verlangt.</summary>
    public static int WageShareFromLoanFee(long loanFee, double marketValue)
    {
        if (loanFee <= 0 || marketValue <= 0) return 0;
        return (int)Math.Min(100, loanFee * 100 / marketValue);
    }

    // ------------------------------------------------------------------ Zahlungsplan

    /// <summary>
    /// Übernimmt ältere <see cref="ClubOffer.InstallmentYears"/> und begrenzt Anzahlung und Raten.
    /// Bei Sofortzahlung ist die Anzahlung die volle Ablöse.
    /// </summary>
    public static void NormalizePaymentPlan(ClubOffer o, long fee)
    {
        fee = Math.Max(0, fee);
        if (o.InstallmentCount <= 0 && o.DownPayment <= 0 && o.InstallmentYears > 0)
        {
            o.InstallmentCount = Math.Clamp(o.InstallmentYears, 0, MaxInstallments);
            o.DownPayment = fee / (o.InstallmentYears + 1);
        }
        o.InstallmentYears = 0;
        o.InstallmentCount = Math.Clamp(o.InstallmentCount, 0, MaxInstallments);
        if (o.InstallmentCount == 0)
        {
            o.DownPayment = fee;
            return;
        }
        if (o.DownPayment <= 0) o.DownPayment = fee / (o.InstallmentCount + 1);
        o.DownPayment = Math.Clamp(o.DownPayment, 0, fee);
        if (o.DownPayment == fee) o.InstallmentCount = 0;
    }

    /// <summary>Sofortzahlung ohne offene Raten (z. B. für Ausstiegsklauseln).</summary>
    public static bool IsImmediatePayment(ClubOffer o) => o.InstallmentCount <= 0;

    /// <summary>
    /// Baut die späteren Raten: Restbetrag gleichmäßig aufgeteilt, Termine über höchstens
    /// <see cref="MaxInstallmentMonths"/> Monate.
    /// </summary>
    public static IReadOnlyList<(DateOnly Due, long Amount)> BuildInstallmentSchedule(ClubOffer o, DateOnly today, long fee)
    {
        NormalizePaymentPlan(o, fee);
        if (o.InstallmentCount <= 0) return Array.Empty<(DateOnly, long)>();
        var remaining = fee - o.DownPayment;
        if (remaining <= 0) return Array.Empty<(DateOnly, long)>();
        var count = o.InstallmentCount;
        var spanMonths = Math.Min(MaxInstallmentMonths, Math.Max(count, count * (MaxInstallmentMonths / MaxInstallments)));
        var list = new List<(DateOnly, long)>(count);
        long paid = 0;
        for (var k = 1; k <= count; k++)
        {
            var amount = k == count ? remaining - paid : remaining / count;
            paid += amount;
            var months = (int)Math.Round(spanMonths * (k / (double)count));
            list.Add((today.AddMonths(Math.Max(1, months)), amount));
        }
        return list;
    }

    /// <summary>Mittlere Zahlungsverzögerung in Jahren (Anzahlung sofort, Raten später) für die Verkäuferbewertung.</summary>
    public static double AveragePaymentDelayYears(ClubOffer o, long fee)
    {
        NormalizePaymentPlan(o, fee);
        if (fee <= 0 || o.InstallmentCount <= 0) return 0;
        var schedule = BuildInstallmentSchedule(o, DateOnly.FromDayNumber(0), fee);
        double weighted = 0;
        foreach (var (due, amount) in schedule)
            weighted += amount * (due.DayNumber / 365.25);
        return weighted / fee;
    }

    /// <summary>Erlaubte Leihdauer: nächstliegende Option aus <see cref="LoanMonthOptions"/>.</summary>
    public static int ClampLoanMonths(int months)
    {
        var best = LoanMonthOptions[0];
        foreach (var opt in LoanMonthOptions)
            if (Math.Abs(opt - months) < Math.Abs(best - months)) best = opt;
        return best;
    }

    /// <summary>Kaufpflicht greift am Leihende, wenn die Bedingung erfüllt ist.</summary>
    public static bool PurchaseObligationTriggered(Loan loan, World world, Club borrower)
    {
        if (!loan.PurchaseObligation || loan.PurchaseFee is null) return false;
        return loan.PurchaseCondition switch
        {
            LoanBuyCondition.AppearancePercent =>
                loan.ClubMatches > 0 && loan.Appearances * 100 >= loan.ClubMatches * Math.Clamp(loan.PurchaseConditionValue <= 0 ? 50 : loan.PurchaseConditionValue, 1, 100),
            LoanBuyCondition.StayInDivision =>
                DivisionLevel(world, borrower) <= (loan.StartDivisionLevel <= 0 ? DivisionLevel(world, borrower) : loan.StartDivisionLevel),
            _ => true,
        };
    }

    /// <summary>Spielklassenniveau 1 = höchste Liga; ohne Liga 99.</summary>
    public static int DivisionLevel(World world, Club club)
    {
        var league = world.FindLeague(club.LeagueId);
        return league?.Level ?? 99;
    }

    /// <summary>Bemessungsgrundlage für ein Weiterverkaufsrecht.</summary>
    public static long SellOnBase(SellOnRight right, long fee) =>
        right.Basis == SellOnBasis.Profit ? Math.Max(0, fee - right.CostBasis) : fee;
}
