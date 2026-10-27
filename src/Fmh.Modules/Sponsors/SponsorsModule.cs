using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Sponsoren (Kapitel 8): Trikot, Ärmel, Ausrüster, 10 Banden, Stadionname. Angebote nach Erfolg und Medienpräsenz.</summary>
public sealed partial class SponsorsModule : GameModule, ISponsorsService
{
    public override string Id => "sponsors";

    public const int BoardSlots = 10;

    /// <summary>Anteil eines Werbeplatzes am Sponsorenwert des Vereins.</summary>
    public static double SlotShare(SponsorSlot slot) => slot switch
    {
        SponsorSlot.Shirt => 0.42,
        SponsorSlot.Sleeve => 0.09,
        SponsorSlot.Kit => 0.16,
        SponsorSlot.Board => 0.018,
        SponsorSlot.StadiumName => 0.15,
        _ => 0,
    };

    /// <summary>Jährlicher Sponsorenwert aller Plätze aus Vereinsgröße, Liga, Medien und Anlagen.</summary>
    public double SponsorValue(Club club)
    {
        var level = World.FindLeague(club.LeagueId)?.Level ?? 3;
        var leagueFactor = level switch { 1 => 1.0, 2 => 0.72, _ => 0.25 };
        var media = IsHuman(club) ? 0.8 + HumanManager.Career.Media / 250 : 1.0;
        return 3_000_000 * ClubRules.Scale(club) * leagueFactor * media * (1 + Facilities.Effect(club, "sponsor") + club.Stadium.Scoreboard * 0.02);
    }

    public IEnumerable<(SponsorSlot slot, int index)> AllSlots()
    {
        yield return (SponsorSlot.Shirt, 0);
        yield return (SponsorSlot.Sleeve, 0);
        yield return (SponsorSlot.Kit, 0);
        for (var i = 0; i < BoardSlots; i++) yield return (SponsorSlot.Board, i);
        yield return (SponsorSlot.StadiumName, 0);
    }

    public SponsorDeal? DealFor(Club club, SponsorSlot slot, int index = 0) =>
        club.Sponsors.FirstOrDefault(s => s.Slot == slot && s.Index == index && s.UntilSeason >= World.SeasonYear);

    public SponsorDeal MakeOffer(Club club, SponsorSlot slot, int index, bool forHuman)
    {
        var list = Data.Sponsors.TryGetValue(club.CountryCode, out var l) ? l.Companies : Data.Sponsors.Values.SelectMany(x => x.Companies).ToList();
        var taken = club.Sponsors.Select(s => s.Company).ToHashSet();
        var fitting = list.Where(c => c.Slots.Contains(slot) && !taken.Contains(c.Name) && Math.Abs(c.Size - club.Reputation) <= 6).ToList();
        if (fitting.Count == 0) fitting = list.Where(c => c.Slots.Contains(slot)).ToList();
        var company = fitting.Count == 0 ? new SponsorCompany { Name = "?" } : Rng.Pick(fitting);
        var years = Rng.Next(1, 4);
        // Langfristige Verträge zahlen etwas weniger pro Jahr, Bonus bei Erreichen des Saisonziels.
        var value = SponsorValue(club) * SlotShare(slot) * Rng.Range(0.8, 1.2) * (1 - (years - 1) * 0.04);
        return new SponsorDeal
        {
            Id = World.NextSponsorId++, Slot = slot, Index = index, Company = company.Name,
            PerSeason = MarketValue.Round(value), Bonus = MarketValue.Round(value * Rng.Range(0.1, 0.3)),
            UntilSeason = World.SeasonYear + years - 1, Expires = forHuman ? World.Date.AddDays(21) : null,
        };
    }

    /// <summary>Saisonstart: KI besetzt freie Plätze, der Mensch bekommt Angebote in den Posteingang.</summary>
    public void SponsorSeasonStart()
    {
        foreach (var club in World.Clubs.Values.Where(Finance.HasFinances))
        {
            club.Sponsors.RemoveAll(s => s.UntilSeason < World.SeasonYear);
            foreach (var (slot, index) in AllSlots())
            {
                if (DealFor(club, slot, index) is not null) continue;
                if (slot == SponsorSlot.StadiumName && club.Reputation < 6) continue;
                if (IsHuman(club))
                {
                    if (slot != SponsorSlot.Board || index < 3) OfferSponsor(club, slot, index);
                }
                else club.Sponsors.Add(MakeOffer(club, slot, index, false));
            }
        }
    }

    public void OfferSponsor(Club club, SponsorSlot slot, int index)
    {
        var offer = MakeOffer(club, slot, index, true);
        club.SponsorOffers.Add(offer);
        Notify(MessageType.Finance, "msg.sender.marketing", TR("msg.sponsorOffer.subject", TR("slot." + slot), offer.Company),
            TR("msg.sponsorOffer.body", offer.Company, TR("slot." + slot), TextArg.Money(offer.PerSeason, Currency),
                offer.UntilSeason - World.SeasonYear + 1, TextArg.Money(offer.Bonus, Currency)),
            MessageAction.SponsorOffer, offer.Id);
    }

    /// <summary>Wöchentlich: freie Plätze bekommen gelegentlich neue Angebote, alte verfallen.</summary>
    public void SponsorsWeekly()
    {
        var club = HumanClub;
        foreach (var o in club.SponsorOffers.Where(o => o.Expires < World.Date).ToList())
        {
            club.SponsorOffers.Remove(o);
            ResolveSponsorMessage(o.Id);
        }
        foreach (var (slot, index) in AllSlots())
        {
            if (DealFor(club, slot, index) is not null || club.SponsorOffers.Any(o => o.Slot == slot && o.Index == index)) continue;
            if (slot == SponsorSlot.StadiumName && club.Reputation < 6) continue;
            if (Rng.Chance(slot == SponsorSlot.Board ? 0.12 : 0.25)) OfferSponsor(club, slot, index);
        }
    }

    private void ResolveSponsorMessage(int offerId)
    {
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.SponsorOffer && m.ActionRefId == offerId)) m.Resolved = true;
    }

    public string? AcceptSponsor(SponsorDeal offer)
    {
        var club = HumanClub;
        if (!club.SponsorOffers.Remove(offer)) return "sponsor.error.expired";
        if (DealFor(club, offer.Slot, offer.Index) is not null) return "sponsor.error.taken";
        offer.Expires = null;
        club.Sponsors.Add(offer);
        // Andere Angebote für denselben Platz verfallen.
        foreach (var o in club.SponsorOffers.Where(o => o.Slot == offer.Slot && o.Index == offer.Index).ToList())
        {
            club.SponsorOffers.Remove(o);
            ResolveSponsorMessage(o.Id);
        }
        ResolveSponsorMessage(offer.Id);
        return null;
    }

    public void RejectSponsor(SponsorDeal offer)
    {
        HumanClub.SponsorOffers.Remove(offer);
        ResolveSponsorMessage(offer.Id);
    }

    /// <summary>Saisonende: Erfolgsbonus bei erreichtem Saisonziel.</summary>
    public void SponsorBonuses(Club club, bool goalReached)
    {
        if (!goalReached) return;
        var bonus = club.Sponsors.Where(s => s.UntilSeason >= World.SeasonYear).Sum(s => s.Bonus);
        if (bonus > 0) Finance.Book(club, TransactionType.Sponsorship, bonus, TR("tx.sponsorBonus"));
    }

    /// <summary>Stadionname: Sponsor, sonst der traditionelle Name.</summary>
    public string StadiumDisplayName(Club club) =>
        DealFor(club, SponsorSlot.StadiumName) is { } d ? $"{d.Company} Arena" : club.StadiumName;
}
