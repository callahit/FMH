using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Hierarchie (Führungsspieler, Etablierte, Mitläufer), Mannschaftsrat, Mentoring, Kapitänseinfluss, Vertrauen und
/// Reaktionen unzufriedener Spieler. Werte aus <c>rules/relations.json</c>.
/// </summary>
public sealed partial class RelationsModule
{
    private RelationsConfig Rules => Data.Relations;

    public void EnsureLeaders(Club club)
    {
        var cr = ClubOf(club);
        var hierarchy = Rules.Hierarchy;
        var all = World.SquadOf(club).Where(p => !p.IsYouth && !p.Retired).ToList();
        var squad = all.Where(p => p.Age(World.Date) >= 21).ToList();
        if (squad.Count == 0) { cr.LeaderIds.Clear(); cr.Mentoring.Clear(); cr.CouncilIds.Clear(); return; }

        var ranked = squad
            .OrderByDescending(p => p.Attributes[Attr.Leadership] * 2 + p.Reputation + p.Hidden.Professionalism + p.Hidden.Loyalty)
            .ThenBy(p => p.Id)
            .ToList();
        var count = Math.Clamp(1 + (squad.Count >= 20 ? 1 : 0) + (squad.Count >= 26 ? 1 : 0), 1, 3);
        // Nur Spieler mit spürbarer Führungskraft; sonst die besten verfügbaren.
        var leaders = ranked.Where(p => p.Attributes[Attr.Leadership] >= hierarchy.LeaderMinLeadership).Take(count).ToList();
        if (leaders.Count == 0) leaders = ranked.Take(count).ToList();
        cr.LeaderIds = leaders.Select(p => p.Id).ToList();

        // Hierarchie: Etablierte nach Ruf, Alter und Zeit im Verein; der Rest folgt
        var established = all.Where(p => !cr.LeaderIds.Contains(p.Id))
            .OrderByDescending(p => p.Reputation * 2 + Math.Min(p.Age(World.Date), 30) / 3.0 + YearsAtClub(p))
            .ThenBy(p => p.Id)
            .Take((int)Math.Round(all.Count * hierarchy.EstablishedShare))
            .Select(p => p.Id).ToHashSet();
        foreach (var p in all)
            Of(p).Tier = cr.LeaderIds.Contains(p.Id) ? SquadTier.Leader : established.Contains(p.Id) ? SquadTier.Established : SquadTier.Follower;

        // Mannschaftsrat: Führungsspieler, aufgefüllt mit den Etablierten mit der meisten Führungsqualität
        cr.CouncilIds = cr.LeaderIds.Concat(all.Where(p => established.Contains(p.Id))
                .OrderByDescending(p => p.Attributes[Attr.Leadership]).ThenBy(p => p.Id).Select(p => p.Id))
            .Take(Math.Max(hierarchy.CouncilSize, cr.LeaderIds.Count)).ToList();

        // Mentoring: Führungsspieler betreuen junge Profis (unter 23).
        var young = all.Where(p => p.Age(World.Date) < 23 && !cr.LeaderIds.Contains(p.Id))
            .OrderBy(p => p.Age(World.Date)).ThenBy(p => p.Id).ToList();
        cr.Mentoring.Clear();
        var menteeIdx = 0;
        foreach (var leaderId in cr.LeaderIds)
        {
            var group = new MentoringGroup { MentorId = leaderId };
            for (var n = 0; n < 2 && menteeIdx < young.Count; n++, menteeIdx++)
                group.MenteeIds.Add(young[menteeIdx].Id);
            if (group.MenteeIds.Count > 0) cr.Mentoring.Add(group);
        }

        // Kapitän: wenn keiner gesetzt, stärkster Leader.
        if (club.Tactic.CaptainId is null || !cr.LeaderIds.Contains(club.Tactic.CaptainId.Value))
            club.Tactic.CaptainId ??= cr.LeaderIds.FirstOrDefault();
    }

    /// <summary>Jahre im Verein seit Vertragsbeginn (Spielstände ohne Datum: 0).</summary>
    private double YearsAtClub(Player p) =>
        p.Contract is { Start.Year: > 1 } c ? Math.Max(0, (World.Date.DayNumber - c.Start.DayNumber) / 365.0) : 0;

    private void MentoringWeekly(Club club)
    {
        var cr = ClubOf(club);
        var h = Rules.Hierarchy;
        foreach (var group in cr.Mentoring)
        {
            if (!World.Players.TryGetValue(group.MentorId, out var mentor)) continue;
            foreach (var id in group.MenteeIds)
            {
                if (!World.Players.TryGetValue(id, out var mentee)) continue;
                // Professionalität junger Spieler steigt langsam mit dem Mentor.
                var chance = h.MentorBase + mentor.Hidden.Professionalism * h.MentorPerProfessionalism + mentor.Attributes[Attr.Leadership] * h.MentorPerLeadership;
                if (mentee.Hidden.Professionalism >= mentor.Hidden.Professionalism) chance *= 0.35;
                if (Rng.Chance(Math.Clamp(chance, 0.05, 0.55)) && mentee.Hidden.Professionalism < 20)
                    mentee.Hidden.Professionalism++;
            }
        }
    }

    private void CaptainMoodWeekly(Club club)
    {
        if (club.Tactic.CaptainId is not { } capId || !World.Players.TryGetValue(capId, out var captain)) return;
        var capSat = Of(captain).Satisfaction;
        var delta = capSat >= 70 ? 2 : capSat <= 35 ? -3 : 0;
        if (delta == 0) return;
        foreach (var p in World.SquadOf(club).Where(p => !p.IsYouth && p.Id != capId))
        {
            var rel = Of(p);
            rel.Satisfaction = Math.Clamp(rel.Satisfaction + delta, 0, 100);
        }
    }

    /// <summary>
    /// Mannschaftsrat: Seine Stimmung färbt auf die Mitläufer ab. Ist er unzufrieden, meldet er sich beim menschlichen
    /// Trainer (höchstens alle 30 Tage) mit dem häufigsten Grund seiner Mitglieder.
    /// </summary>
    private void CouncilWeekly(Club club)
    {
        var cr = ClubOf(club);
        var council = cr.CouncilIds.Where(World.Players.ContainsKey).Select(id => World.Players[id]).Where(p => p.ClubId == club.Id).ToList();
        if (council.Count == 0) return;
        var mood = council.Average(p => Of(p).Satisfaction);
        var pull = Rules.Hierarchy.CouncilPull;
        var delta = mood >= 70 ? pull : mood <= 35 ? -pull : 0;
        if (delta != 0)
            foreach (var p in World.SquadOf(club).Where(p => !p.IsYouth && Of(p).Tier == SquadTier.Follower))
                Of(p).Satisfaction = Math.Clamp(Of(p).Satisfaction + delta, 0, 100);

        if (club.Id != HumanClub.Id || mood >= Rules.Hierarchy.CouncilConcernBelow) return;
        if (cr.LastCouncilConcern is { } last && World.Date.DayNumber - last.DayNumber < 30) return;
        cr.LastCouncilConcern = World.Date;
        var reason = council.SelectMany(p => Of(p).ReasonKeys.Take(1)).GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key).FirstOrDefault() ?? "sat.reason.goals.bad";
        Notify(MessageType.Info, "msg.sender.assistant", TR("council.concern.subject"),
            TR("council.concern.body", string.Join(", ", council.Select(p => p.Name)), (int)Math.Round(mood), TR(reason)));
    }

    /// <summary>Vertrauen erholt sich jede Woche ein Stück Richtung Startwert.</summary>
    private void TrustWeekly(Club club)
    {
        var cr = ClubOf(club);
        var t = Rules.Trust;
        cr.Trust = cr.Trust < t.Start ? Math.Min(t.Start, cr.Trust + t.WeeklyRecovery) : Math.Max(t.Start, cr.Trust - t.WeeklyRecovery);
    }

    private void UnhappyWeekly(Club club)
    {
        var rules = Rules;
        var request = rules.TransferRequest;
        foreach (var p in World.SquadOf(club).Where(p => !p.IsYouth))
        {
            var rel = Of(p);
            // Zufrieden genug: ein früherer Wechselwunsch erlischt
            if (rel.WantsTransfer && rel.Satisfaction >= rules.WantsTransferResetAt) rel.WantsTransfer = false;
            if (rel.Satisfaction >= rules.UnhappyThreshold) continue;

            // Wechselwunsch: Ambition und Temperament erhöhen die Chance.
            var transferChance = request.Base + (20 - rel.Satisfaction) * request.PerPointBelow20 + p.Hidden.Ambition * request.PerAmbition;
            if (p.Hidden.Loyalty >= 15) transferChance *= request.LoyalFactor;
            if (!rel.WantsTransfer && Rng.Chance(Math.Clamp(transferChance, request.Min, request.Max)))
            {
                rel.WantsTransfer = true;
                if (!p.Transfer.Listed && Rng.Chance(0.4))
                    p.Transfer.Listed = true;
                if (club.Id == HumanClub.Id)
                    Notify(MessageType.Transfer, "msg.sender.assistant",
                        TR("sat.transferRequest.subject", p.Name),
                        TR("sat.transferRequest.body", p.Name));
            }

            // Presse: unzufriedene Spieler reden häufiger.
            if (club.Id == HumanClub.Id && Rng.Chance(0.18 + (rules.UnhappyThreshold - rel.Satisfaction) * 0.005))
            {
                Notify(MessageType.Press, "msg.sender.newspaper",
                    TR("sat.press.subject", p.Name),
                    TR("sat.press.body", p.Name, club.Name));
                rel.Satisfaction = Math.Clamp(rel.Satisfaction - 2, 0, 100);
            }
        }
    }
}
