using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Stadionplanung (Kapitel 8): Prognose der Mehreinnahmen eines Ausbaus über das Zuschauermodell und der
/// Stadionneubau an anderer Stelle, der nur nach einem Grundstücksangebot der Stadt (Ereignis) möglich ist.
/// </summary>
public sealed partial class StadiumPlansModule : GameModule, IStadiumPlansService
{
    public override string Id => "stadiumPlans";

    private static Stadium CloneStadium(Stadium s) => new()
    {
        Stands = s.Stands.Select(x => new Stand { Side = x.Side, Standing = x.Standing, Seating = x.Seating, Roof = x.Roof, Boxes = x.Boxes, Closed = x.Closed }).ToList(),
        Floodlight = s.Floodlight, UndersoilHeating = s.UndersoilHeating, Scoreboard = s.Scoreboard, Pitch = s.Pitch,
    };

    /// <summary>Spieltagseinnahmen einer Saison (alle Liga-Heimspiele, trockenes Wetter) mit einem gegebenen Stadion.</summary>
    private (double spectators, long income) SeasonGate(Club club, Stadium stadium)
    {
        var league = World.FindLeague(club.LeagueId);
        if (league is null) return (0, 0);
        var homes = league.Fixtures.Where(f => f.HomeId == club.Id).ToList();
        if (homes.Count == 0) return (0, 0);
        var original = club.Stadium;
        var prices = club.Finance.Prices;
        club.Stadium = stadium;
        try
        {
            double spectators = 0;
            long income = 0;
            foreach (var f in homes)
            {
                var away = World.Clubs[f.AwayId];
                var (standing, seating, boxes) = Finance.Attendance(club, away, f, Weather.Sunny);
                var attendance = standing + seating;
                spectators += attendance;
                var surcharge = Finance.IsTopMatch(club, away, f) ? 1 + prices.TopMatchSurcharge / 100.0 : 1;
                // Dauerkarten sind rabattiert; als Mittel über alle Zuschauer gerechnet.
                var seasonShare = attendance == 0 ? 0 : Math.Min(club.Finance.SeasonTickets, attendance) / (double)attendance;
                var discount = 1 - seasonShare * prices.SeasonTicketDiscount / 100.0;
                income += (long)((standing * prices.Standing + seating * prices.Seating) * surcharge * discount + boxes * prices.Box * surcharge);
                income += (long)(attendance * 1.2 * (1 + Facilities.Effect(club, "income.catering") * 0.2) + boxes * 600 * (1 + Facilities.Effect(club, "income.box")));
                income += (long)(attendance * 0.8 * (1 + Facilities.Effect(club, "income.merch") * 0.2) * Math.Clamp(club.Fans.Mood / 60, 0.5, 1.4));
            }
            return (spectators / homes.Count, income);
        }
        finally
        {
            club.Stadium = original;
        }
    }

    /// <summary>Prognose: zusätzliche Zuschauer je Heimspiel, Einnahmen und Unterhalt pro Saison, Amortisation.</summary>
    public StadiumImpact ForecastStadium(Club club, Stadium planned, long cost)
    {
        var (before, incomeBefore) = SeasonGate(club, club.Stadium);
        var (after, incomeAfter) = SeasonGate(club, planned);
        var upkeep = (ClubRules.StadiumUpkeep(planned) - ClubRules.StadiumUpkeep(club.Stadium)) * 12;
        return new StadiumImpact((int)Math.Round(after - before), incomeAfter - incomeBefore, upkeep, cost);
    }

    /// <summary>Prognose für einen Tribünen- oder Technikausbau (Standardbaufirma).</summary>
    public StadiumImpact? ForecastProject(Club club, ProjectKind kind, StandSide? side, Contractor contractor = Contractor.Standard)
    {
        if (Facilities.StadiumProjectQuote(club, kind, side) is not { } quote) return null;
        var planned = CloneStadium(club.Stadium);
        var stand = side is { } s ? planned.Stand(s) : null;
        switch (kind)
        {
            case ProjectKind.Standing: stand!.Standing += 1000; break;
            case ProjectKind.Seating: stand!.Seating += 1000; break;
            case ProjectKind.Roof: stand!.Roof = true; break;
            case ProjectKind.Boxes: stand!.Boxes = Math.Min(40, stand.Boxes + 5); break;
            case ProjectKind.Floodlight: planned.Floodlight++; break;
            case ProjectKind.Heating: planned.UndersoilHeating = true; break;
            case ProjectKind.Scoreboard: planned.Scoreboard++; break;
        }
        var impact = ForecastStadium(club, planned, MarketValue.Round(quote.cost * ClubRules.ContractorFactors(contractor).cost));
        // Anzeigetafel: Werbeeinnahmen je Heimspiel (siehe Spieltagseinnahmen).
        if (kind == ProjectKind.Scoreboard)
            impact = impact with { ExtraIncome = impact.ExtraIncome + 6_000L * (World.FindLeague(club.LeagueId)?.Fixtures.Count(f => f.HomeId == club.Id) ?? 17) };
        return impact;
    }

    // ================================================================== Neubau

    /// <summary>Grundstücksangebot der Stadt (aus dem Ereignis "citySite").</summary>
    public void OfferStadiumSite(Club club)
    {
        club.Grounds.Site = new StadiumSite
        {
            Offered = World.Date,
            Expires = World.Date.AddYears(2),
            MaxCapacity = Math.Clamp(20_000 + club.Reputation * 2_800, 20_000, 80_000) / 1000 * 1000,
            LandPrice = MarketValue.Round(1_500_000 * ClubRules.Scale(club)),
        };
    }

    /// <summary>Gültiges Grundstück für den Neubau (angeboten, nicht abgelaufen, nicht bebaut).</summary>
    public StadiumSite? AvailableSite(Club club) =>
        club.Grounds.Site is { Used: false } s && s.Expires >= World.Date ? s : null;

    public long NewStadiumCost(Club club, StadiumDesign d)
    {
        var fin = World.Country(club.CountryCode).Finance;
        var places = d.Capacity * fin.StadiumCostPerPlace * (d.Roof ? 1.0 : 0.85);
        // Sitzplätze sind teurer als Stehplätze.
        places *= 1 + (100 - d.StandingPercent) / 100.0 * 0.25;
        return MarketValue.Round(fin.StadiumBaseCost + places + d.Boxes * fin.StadiumCostPerBox + (club.Grounds.Site?.LandPrice ?? 0));
    }

    /// <summary>Stadionkredite: eigener Rahmen bis zum Dreifachen der Jahreseinnahmen.</summary>
    public long StadiumLoanCapacity(Club club) =>
        Math.Max(0, MarketValue.Round(Finance.ExpectedIncome(club) * 3.0) - club.Finance.Loans.Sum(l => l.Remaining));

    /// <summary>Prognose für den Neubau gegenüber dem heutigen Stadion.</summary>
    public StadiumImpact ForecastNewStadium(Club club, StadiumDesign d, Contractor contractor = Contractor.Standard) =>
        ForecastStadium(club, StadiumBuilder.BuildNewStadium(club, d), MarketValue.Round(NewStadiumCost(club, d) * ClubRules.ContractorFactors(contractor).cost));

    /// <summary>Neubau beauftragen. Fehlertext-Schlüssel oder null.</summary>
    public string? OrderNewStadium(StadiumDesign d, Contractor contractor, bool financed)
    {
        var club = HumanClub;
        if (AvailableSite(club) is not { } site) return "build.error.noSite";
        if (d.Capacity < 10_000 || d.Capacity > site.MaxCapacity) return "build.error.capacity";
        if (club.Grounds.Projects.Any(p => p.Kind == ProjectKind.NewStadium && p.State is not (ProjectState.Done or ProjectState.Rejected))) return "build.error.running";
        var design = new StadiumDesign
        {
            Capacity = d.Capacity / 1000 * 1000, StandingPercent = Math.Clamp(d.StandingPercent, 0, 60), Boxes = Math.Clamp(d.Boxes, 0, 80), Roof = d.Roof,
            Name = string.IsNullOrWhiteSpace(d.Name) ? $"{club.City}-Arena" : d.Name.Trim(),
        };
        var fin = World.Country(club.CountryCode).Finance;
        var error = Facilities.Order(club, new ConstructionProject { Kind = ProjectKind.NewStadium, Amount = design.Capacity, Design = design },
            NewStadiumCost(club, design), fin.StadiumBuildDays, contractor, financed);
        if (error is null) site.Used = true;
        return error;
    }

    /// <summary>Abgelaufene Grundstücksangebote verfallen (monatlich geprüft).</summary>
    public void SiteMonthly(Club club)
    {
        if (club.Grounds.Site is { Used: false } s && s.Expires < World.Date)
        {
            club.Grounds.Site = null;
            if (IsHuman(club)) Notify(MessageType.Stadium, "msg.sender.city", TR("msg.siteExpired.subject"), TR("msg.siteExpired.body"));
        }
    }
}
