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

    /// <summary>Summe aller Weiterverkaufsrechte eines Spielers, in Prozent vom Bruttoerlös.</summary>
    public const int MaxSellOnPercent = 50;

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
}
