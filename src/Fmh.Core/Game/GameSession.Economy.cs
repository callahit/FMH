using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Phase 3: Startausstattung der Vereine und tägliche Abläufe des Umfelds.</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Stadion, Gelände, Personal, Vorstand, Fans, Preise, Rivalen und Sponsoren für alle Vereine anlegen.
    /// Idempotent: ältere Spielstände werden beim Laden nachgerüstet.
    /// </summary>
    private void InitEconomy()
    {
        var fresh = new List<Club>();
        foreach (var club in World.Clubs.Values.OrderBy(c => c.Id))
        {
            if (club.Stadium.Stands.Count > 0) continue;
            fresh.Add(club);
            club.Stadium = CreateStadium(club, _rng);
            CreateGrounds(club);
            CreateStaff(club);
            CreateBoard(club);
            CreateFans(club);
            var div = DivisionFinance(club);
            club.Finance.Prices = new TicketPrices { Standing = div.Standing, Seating = div.Seating, Box = div.Box };
        }
        if (fresh.Count == 0) return;
        AssignRivals();
        if (!World.Staff.Any(s => s.ClubId is null)) RefreshStaffMarket();
        foreach (var club in fresh)
        {
            RefreshStaffEffects(club);
            if (!HasFinances(club)) continue;
            SetSeasonBudget(club);
            UpdateFavourites(club);
            if (IsHuman(club)) continue;
            foreach (var (slot, index) in AllSlots())
                if (!(slot == SponsorSlot.StadiumName && club.Reputation < 6)) club.Sponsors.Add(MakeOffer(club, slot, index, false));
        }
        var human = HumanClub;
        if (fresh.Contains(human))
        {
            var m = HumanManager;
            m.Career.License = m.Reputation switch { <= 3 => CoachingLicense.C, <= 6 => CoachingLicense.B, <= 11 => CoachingLicense.A, _ => CoachingLicense.Pro };
            if (m.Career.Stations.Count == 0) StartStation(human);
            foreach (var (slot, index) in AllSlots())
            {
                if (slot == SponsorSlot.StadiumName && human.Reputation < 6) continue;
                // Bestehende Verträge laufen zum Start weiter (Trikot, Ausrüster, einige Banden), der Rest ist offen.
                if (slot is SponsorSlot.Shirt or SponsorSlot.Kit || slot == SponsorSlot.Board && index < 6) human.Sponsors.Add(MakeOffer(human, slot, index, false));
            }
            OfferSponsorsForFreeSlots(human);
            if (World.Date.Month == World.Countries[0].Season.StartMonth && World.Date.Day <= 7) OfferSeasonGoal();
        }
    }

    private void OfferSponsorsForFreeSlots(Club club)
    {
        foreach (var (slot, index) in AllSlots())
        {
            if (DealFor(club, slot, index) is not null || club.SponsorOffers.Any(o => o.Slot == slot && o.Index == index)) continue;
            if (slot == SponsorSlot.StadiumName && club.Reputation < 6) continue;
            if (slot != SponsorSlot.Board || index < 8) OfferSponsor(club, slot, index);
        }
    }

    /// <summary>Rivalen: der reputationsnächste Verein derselben Liga, dazu ein zufälliger Lokalrivale.</summary>
    private void AssignRivals()
    {
        foreach (var league in World.Leagues)
        {
            var clubs = league.ClubIds.Select(id => World.Clubs[id]).ToList();
            foreach (var club in clubs.Where(c => c.Rivals.Count == 0))
            {
                var nearest = clubs.Where(c => c.Id != club.Id).OrderBy(c => Math.Abs(c.Reputation - club.Reputation)).ThenBy(c => c.Id).First();
                club.Rivals.Add(nearest.Id);
                if (!nearest.Rivals.Contains(club.Id)) nearest.Rivals.Add(club.Id);
            }
        }
    }

    /// <summary>Täglich (nur der menschliche Verein): Vorstand, Fans, Sponsoren, Presse, Ereignisse, Pressekonferenz vor dem Spiel.</summary>
    private void EnvironmentDaily()
    {
        if (AwaitingNewJob) return;
        var club = HumanClub;
        if (World.Date.DayOfWeek == DayOfWeek.Monday)
        {
            BoardWeekly();
            if (AwaitingNewJob) return;
            SponsorsWeekly();
            PressWeekly();
            MoraleWeekly();
            // Das Medienansehen fällt ohne neue Schlagzeilen langsam auf den Mittelwert zurück.
            var career = HumanManager.Career;
            career.Media += (50 - career.Media) * 0.02;
            if (World.Date.Month == 11 && World.Date.Day <= 7) AnnualMeeting();
        }
        EventsDaily();
        var tomorrow = World.Date.AddDays(1);
        if (World.PendingPress is null && AllFixtures.FirstOrDefault(f => f.Date == tomorrow && f.Result is null && f.Involves(club.Id)) is { } next)
        {
            ChoreoBeforeMatch(next);
            SchedulePress(next, after: false);
        }
    }

    /// <summary>Sportpsychologe und Anlagen heben niedrige Moral schneller wieder an.</summary>
    private void MoraleWeekly()
    {
        foreach (var club in World.Clubs.Values)
        {
            var chance = Effect(club, "morale") * 0.08 + Effect(club, "psychology") * 0.1 + StaffAbility(club, StaffRole.Psychologist) * 0.01;
            if (chance <= 0) continue;
            foreach (var p in World.SquadOf(club).Where(p => p.State.Morale < Morale.Okay))
                if (_rng.Chance(chance)) p.State.Morale++;
        }
    }
}
