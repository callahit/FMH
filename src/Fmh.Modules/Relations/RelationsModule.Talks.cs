using Fmh.Core.Data;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Einzelgespräche und Versprechen.</summary>
public sealed partial class RelationsModule
{
    public TalkResult Talk(Player p, TalkTopic topic, PromiseKind? promiseKind = null, DateOnly? deadline = null, SquadStatus? status = null)
    {
        if (p.ClubId != HumanClub.Id) return new TalkResult { Allowed = false, ReactionKey = "talk.error.notOwn" };
        var rel = Of(p);
        if (rel.LastTalk is { } last && World.Date < last.AddDays(RelationsRules.TalkCooldownDays))
            return new TalkResult { Allowed = false, ReactionKey = "talk.error.cooldown" };

        var temp = p.Hidden.Temperament;
        var ambition = p.Hidden.Ambition;
        var loyalty = p.Hidden.Loyalty;
        var pro = p.Hidden.Professionalism;
        var delta = 0;
        var reaction = "talk.reaction.neutral";
        PlayerPromise? promise = null;

        switch (topic)
        {
            case TalkTopic.Praise:
                delta = temp >= 8 ? 6 + (pro >= 12 ? 2 : 0) : 2;
                reaction = temp >= 12 ? "talk.reaction.praise.warm" : "talk.reaction.praise.cool";
                break;
            case TalkTopic.Criticism:
                if (pro >= 14) { delta = 1; reaction = "talk.reaction.criticism.accept"; }
                else if (temp <= 8) { delta = -8; reaction = "talk.reaction.criticism.angry"; }
                else { delta = -3; reaction = "talk.reaction.criticism.sullen"; }
                break;
            case TalkTopic.PlayingTime:
                if (ambition >= 14) { delta = -2; reaction = "talk.reaction.playingTime.demand"; }
                else if (loyalty >= 14) { delta = 3; reaction = "talk.reaction.playingTime.patient"; }
                else { delta = 1; reaction = "talk.reaction.playingTime.ok"; }
                break;
            case TalkTopic.Performance:
                delta = pro >= 12 ? 3 : temp <= 9 ? -2 : 1;
                reaction = pro >= 12 ? "talk.reaction.performance.focus" : "talk.reaction.performance.defensive";
                break;
            case TalkTopic.Contract:
                delta = ambition >= 13 ? -1 : loyalty >= 13 ? 4 : 1;
                reaction = ambition >= 13 ? "talk.reaction.contract.ambitious" : "talk.reaction.contract.loyal";
                break;
            case TalkTopic.Promise:
                var kind = promiseKind ?? PromiseKind.PlayingTime;
                var until = deadline ?? World.Date.AddDays(60);
                promise = new PlayerPromise
                {
                    Id = World.NextPromiseId++,
                    PlayerId = p.Id,
                    ClubId = HumanClub.Id,
                    Kind = kind,
                    MadeOn = World.Date,
                    Deadline = until,
                    Outcome = PromiseOutcome.Pending,
                    TargetStatus = status ?? p.Contract?.Status,
                    TargetValue = kind switch
                    {
                        PromiseKind.PlayingTime => 50,
                        PromiseKind.Contract => p.Contract?.WeeklyWage ?? 0,
                        _ => 0,
                    },
                };
                World.Promises.Add(promise);
                delta = 8 + loyalty / 5;
                reaction = "talk.reaction.promise.made";
                break;
        }

        rel.Satisfaction = Math.Clamp(rel.Satisfaction + delta, 0, 100);
        rel.LastTalk = World.Date;
        if (delta != 0)
            p.State.Morale = (Morale)Math.Clamp((int)p.State.Morale + Math.Sign(delta), 0, 6);

        Notify(MessageType.Info, "msg.sender.assistant",
            TR("talk.notify.subject", p.Name),
            TR("talk.notify.body", p.Name, TR("talk.topic." + topic), TR(reaction)));

        return new TalkResult { Allowed = true, SatisfactionDelta = delta, ReactionKey = reaction, Promise = promise };
    }

    private void CheckPromises()
    {
        foreach (var promise in World.Promises.Where(p => p.Outcome == PromiseOutcome.Pending).OrderBy(p => p.Id).ToList())
        {
            if (!World.Players.TryGetValue(promise.PlayerId, out var player)) { promise.Outcome = PromiseOutcome.Broken; continue; }
            if (player.ClubId != promise.ClubId)
            {
                // Spieler hat den Verein verlassen: Transferversprechen gehalten, sonst erledigt.
                promise.Outcome = promise.Kind == PromiseKind.Transfer ? PromiseOutcome.Held : PromiseOutcome.Held;
                continue;
            }

            var heldEarly = promise.Kind switch
            {
                PromiseKind.Transfer when player.ClubId != promise.ClubId => true,
                PromiseKind.Contract when player.Contract is { } c && c.WeeklyWage > promise.TargetValue && c.Start >= promise.MadeOn => true,
                PromiseKind.SquadRole when promise.TargetStatus is { } ts && player.Contract?.Status == ts
                    && MinutesShare(player, promise.ClubId) >= ExpectedMinutesShare(ts, player.Hidden.Ambition) * 0.85 => true,
                PromiseKind.PlayingTime when MinutesShare(player, promise.ClubId) >= promise.TargetValue / 100.0 => true,
                _ => false,
            };
            if (heldEarly && World.Date < promise.Deadline)
            {
                // Früh erfüllt nur bei klarer Spielzeit/Status-Erfüllung.
                if (promise.Kind is PromiseKind.PlayingTime or PromiseKind.SquadRole)
                    ResolvePromise(promise, player, held: true);
                continue;
            }

            if (World.Date < promise.Deadline) continue;

            var held = promise.Kind switch
            {
                PromiseKind.PlayingTime => MinutesShare(player, promise.ClubId) >= Math.Max(0.25, promise.TargetValue / 100.0),
                PromiseKind.Transfer => Of(player).WantsTransfer || player.Transfer.Listed || player.ClubId != promise.ClubId,
                PromiseKind.Contract => WageImprovedSince(player, promise),
                PromiseKind.SquadRole => SquadRoleHeld(player, promise),
                _ => false,
            };
            ResolvePromise(promise, player, held);
        }
    }

    private void ResolvePromise(PlayerPromise promise, Player player, bool held)
    {
        promise.Outcome = held ? PromiseOutcome.Held : PromiseOutcome.Broken;
        var rel = Of(player);
        if (held)
        {
            rel.Satisfaction = Math.Clamp(rel.Satisfaction + (promise.Implicit ? 4 : 12), 0, 100);
            if (player.ClubId == HumanClub.Id)
                Notify(MessageType.Info, "msg.sender.assistant", TR("promise.held.subject", player.Name),
                    TR("promise.held.body", player.Name, TR("promise.kind." + promise.Kind)));
        }
        else
        {
            rel.Satisfaction = Math.Clamp(rel.Satisfaction - (promise.Implicit ? 14 : 25), 0, 100);
            player.State.Morale = (Morale)Math.Max(0, (int)player.State.Morale - 2);
            if (player.ClubId == HumanClub.Id)
                Notify(MessageType.Info, "msg.sender.assistant", TR("promise.broken.subject", player.Name),
                    TR("promise.broken.body", player.Name, TR("promise.kind." + promise.Kind)));
        }
    }

    private double MinutesShare(Player p, int clubId)
    {
        if (!World.Clubs.TryGetValue(clubId, out var club)) return 0;
        var league = World.FindLeague(club.LeagueId);
        var matchdays = Math.Max(1, league?.CurrentMatchday ?? 1);
        return p.SeasonStats.Minutes / (matchdays * 90.0);
    }

    private static bool WageImprovedSince(Player p, PlayerPromise promise) =>
        p.Contract is { } c && c.WeeklyWage > promise.TargetValue;

    private bool SquadRoleHeld(Player p, PlayerPromise promise)
    {
        if (promise.TargetStatus is not { } ts) return false;
        if (p.Contract?.Status != ts && Of(p).ExpectedStatus != ts) return false;
        // Implizit: Spielzeit muss zum Status passen.
        return MinutesShare(p, promise.ClubId) >= ExpectedMinutesShare(ts, p.Hidden.Ambition) * 0.7;
    }
}
