using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Sponsoren“.</summary>
public sealed partial class GameSession
{
    public const int BoardSlots = SponsorsModule.BoardSlots;
    public static double SlotShare(SponsorSlot slot) => SponsorsModule.SlotShare(slot);
    public double SponsorValue(Club club) => _sponsors.SponsorValue(club);
    public IEnumerable<(SponsorSlot slot, int index)> AllSlots() => _sponsors.AllSlots();
    public SponsorDeal? DealFor(Club club, SponsorSlot slot, int index = 0) => _sponsors.DealFor(club, slot, index);
    public string? AcceptSponsor(SponsorDeal offer) => _sponsors.AcceptSponsor(offer);
    public void RejectSponsor(SponsorDeal offer) => _sponsors.RejectSponsor(offer);
    public string StadiumDisplayName(Club club) => _sponsors.StadiumDisplayName(club);
}
