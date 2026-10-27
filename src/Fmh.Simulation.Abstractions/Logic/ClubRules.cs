using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Reine Regelfunktionen rund um den Verein (Größenfaktor, Baukosten, Unterhalt, Gehälter), die mehrere Module brauchen.</summary>
public static class ClubRules
{
    /// <summary>Größe des Vereins für Geldbeträge: 1,0 bei Reputation 10, ca. ×1,19 je Punkt.</summary>
    public static double Scale(Club club) => Math.Max(0.15, Math.Pow(1.19, club.Reputation - 10));

    /// <summary>Finanzrahmen der Spielklasse eines Vereins: Liga bzw. Staffel, sonst ihre Division, sonst Standardwerte.</summary>
    public static DivisionFinance DivisionFinance(World world, Club club) =>
        world.Country(club.CountryCode).Finance.Divisions is var all && (all.TryGetValue(club.LeagueId, out var d)
        || (world.FindLeague(club.LeagueId) is { } l && all.TryGetValue(l.Format.Id, out d))) ? d : new DivisionFinance();

    public static long LevelCost(BuildingDef def, int level) => (long)(def.Cost * Math.Pow(def.CostGrowth, level - 1));

    public static long LandPrice(Club club) => MarketValue.Round(120_000 * Scale(club));

    public static (double cost, double days) ContractorFactors(Contractor c) => c switch
    {
        Contractor.Cheap => (0.85, 1.4),
        Contractor.Premium => (1.3, 0.7),
        _ => (1.0, 1.0),
    };

    public static long StadiumUpkeep(Stadium st) =>
        (long)(st.Capacity * 0.6 + st.Boxes * 400 + st.Floodlight * 3_000 + (st.UndersoilHeating ? 6_000 : 0) + st.Scoreboard * 1_500);

    /// <summary>Wochengehalt eines Scouts aus seiner Urteilskraft.</summary>
    public static int ScoutWage(Scout s) => (int)(Math.Round(250 * Math.Pow(1.2, (s.JudgingAbility + s.JudgingPotential) / 2.0) / 100) * 100);

    public static int Outcome(Club club, Fixture f)
    {
        var r = f.Result!;
        var home = f.HomeId == club.Id;
        var diff = home ? r.HomeGoals - r.AwayGoals : r.AwayGoals - r.HomeGoals;
        if (diff == 0 && r.PenaltiesHome is { } ph && r.PenaltiesAway is { } pa) diff = home ? ph - pa : pa - ph;
        return Math.Sign(diff);
    }

    /// <summary>Die alten Kennzahlen (Anzeige, Training) folgen den Kriterien des Leistungszentrums.</summary>
    public static void SyncYouthValues(Club club)
    {
        club.YouthFacilities = (int)Math.Round(Math.Clamp((club.Academy.Facilities + club.Academy.Coaching) / 2, 1, 20));
        club.YouthRecruitment = (int)Math.Round(Math.Clamp(club.Academy.Recruitment, 1, 20));
    }
}
