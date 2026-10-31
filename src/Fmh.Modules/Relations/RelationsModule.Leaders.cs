using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Führungsspieler, Mentoring, Kapitänseinfluss und Reaktionen unzufriedener Spieler.</summary>
public sealed partial class RelationsModule
{
    public void EnsureLeaders(Club club)
    {
        var cr = ClubOf(club);
        var squad = World.SquadOf(club).Where(p => !p.IsYouth && !p.Retired && p.Age(World.Date) >= 21).ToList();
        if (squad.Count == 0) { cr.LeaderIds.Clear(); cr.Mentoring.Clear(); return; }

        var ranked = squad
            .OrderByDescending(p => p.Attributes[Attr.Leadership] * 2 + p.Reputation + p.Hidden.Professionalism + p.Hidden.Loyalty)
            .ThenBy(p => p.Id)
            .ToList();
        var count = Math.Clamp(1 + (squad.Count >= 20 ? 1 : 0) + (squad.Count >= 26 ? 1 : 0), 1, 3);
        // Nur Spieler mit spürbarer Führungskraft; sonst die besten verfügbaren.
        var leaders = ranked.Where(p => p.Attributes[Attr.Leadership] >= 10).Take(count).ToList();
        if (leaders.Count == 0) leaders = ranked.Take(count).ToList();
        cr.LeaderIds = leaders.Select(p => p.Id).ToList();

        // Mentoring: Führungsspieler betreuen junge Profis (unter 23).
        var young = World.SquadOf(club).Where(p => !p.IsYouth && !p.Retired && p.Age(World.Date) < 23 && !cr.LeaderIds.Contains(p.Id))
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

    private void MentoringWeekly(Club club)
    {
        var cr = ClubOf(club);
        foreach (var group in cr.Mentoring)
        {
            if (!World.Players.TryGetValue(group.MentorId, out var mentor)) continue;
            foreach (var id in group.MenteeIds)
            {
                if (!World.Players.TryGetValue(id, out var mentee)) continue;
                // Professionalität junger Spieler steigt langsam mit dem Mentor.
                var chance = 0.12 + mentor.Hidden.Professionalism * 0.015 + mentor.Attributes[Attr.Leadership] * 0.008;
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

    private void UnhappyWeekly(Club club)
    {
        foreach (var p in World.SquadOf(club).Where(p => !p.IsYouth))
        {
            var rel = Of(p);
            if (rel.Satisfaction >= RelationsRules.UnhappyThreshold) continue;

            // Wechselwunsch: Ambition und Temperament erhöhen die Chance.
            var transferChance = 0.12 + (20 - rel.Satisfaction) * 0.01 + p.Hidden.Ambition * 0.008;
            if (p.Hidden.Loyalty >= 15) transferChance *= 0.45;
            if (Rng.Chance(Math.Clamp(transferChance, 0.05, 0.55)))
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
            if (club.Id == HumanClub.Id && Rng.Chance(0.18 + (RelationsRules.UnhappyThreshold - rel.Satisfaction) * 0.005))
            {
                Notify(MessageType.Press, "msg.sender.newspaper",
                    TR("sat.press.subject", p.Name),
                    TR("sat.press.body", p.Name, club.Name));
                rel.Satisfaction = Math.Clamp(rel.Satisfaction - 2, 0, 100);
            }
        }
    }
}
