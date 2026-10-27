using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Sponsoren“ für andere Module.</summary>
public interface ISponsorsService
{
    /// <summary>Jährlicher Sponsorenwert aller Plätze aus Vereinsgröße, Liga, Medien und Anlagen.</summary>
    double SponsorValue(Club club);
    IEnumerable<(SponsorSlot slot, int index)> AllSlots();
    SponsorDeal? DealFor(Club club, SponsorSlot slot, int index = 0);
    SponsorDeal MakeOffer(Club club, SponsorSlot slot, int index, bool forHuman);
    /// <summary>Saisonstart: KI besetzt freie Plätze, der Mensch bekommt Angebote in den Posteingang.</summary>
    void SponsorSeasonStart();
    void OfferSponsor(Club club, SponsorSlot slot, int index);
    /// <summary>Wöchentlich: freie Plätze bekommen gelegentlich neue Angebote, alte verfallen.</summary>
    void SponsorsWeekly();
    string? AcceptSponsor(SponsorDeal offer);
    /// <summary>Saisonende: Erfolgsbonus bei erreichtem Saisonziel.</summary>
    void SponsorBonuses(Club club, bool goalReached);
    /// <summary>Stadionname: Sponsor, sonst der traditionelle Name.</summary>
    string StadiumDisplayName(Club club);
}
