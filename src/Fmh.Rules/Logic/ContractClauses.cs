using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Auf- und Abstiegsklauseln in Verträgen: Spieler- und Personalgehälter sowie Sponsorenzahlungen ändern sich, wenn der Verein
/// die Spielklasse wechselt. Übliche Werte stehen je Spielklasse bzw. je Land in den Daten.
/// </summary>
public static class ContractClauses
{
    /// <summary>Übliche Gehaltsklausel für Spieler und Personal in der aktuellen Spielklasse des Vereins (−% Abstieg, +% Aufstieg).</summary>
    public static (int relegation, int promotion) Defaults(World world, Club club)
    {
        var div = ClubRules.DivisionFinance(world, club);
        return (div.RelegationWagePercent, div.PromotionWagePercent);
    }

    /// <summary>Übliche Sponsorenklausel des Landes.</summary>
    public static (int relegation, int promotion) SponsorDefaults(World world, Club club)
    {
        var fin = world.Country(club.CountryCode).Finance;
        return (fin.SponsorRelegationPercent, fin.SponsorPromotionPercent);
    }

    /// <summary>
    /// Faktor für eine Bewegung um <paramref name="steps"/> Ebenen (positiv = Abstieg, negativ = Aufstieg); jede Ebene wirkt
    /// einzeln, z. B. zwei Abstiege mit −30 % ergeben ×0,49.
    /// </summary>
    public static double Factor(int steps, int relegation, int promotion) => steps switch
    {
        > 0 => Math.Pow(1 - Math.Clamp(relegation, 0, 90) / 100.0, steps),
        < 0 => Math.Pow(1 + Math.Max(0, promotion) / 100.0, -steps),
        _ => 1.0,
    };

    /// <summary>Setzt fehlende Klauseln eines neuen Vertrags auf die üblichen Werte der Spielklasse des Vereins.</summary>
    public static void FillDefaults(Contract c, World world, Club club)
    {
        if (c.RelegationPercent is not null && c.PromotionPercent is not null) return;
        var (rel, pro) = Defaults(world, club);
        c.RelegationPercent ??= rel;
        c.PromotionPercent ??= pro;
    }
}
