using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Kabine / Spielerbeziehungen“.</summary>
public sealed partial class GameSession
{
    public PlayerRelations PlayerRelationsOf(Player p) => _relations.Of(p);
    public ClubRelations ClubRelationsOf(Club club) => _relations.ClubOf(club);
    public void RecalculateSatisfaction(Player p) => _relations.Recalculate(p);
    public void RecalculateClubSatisfaction(Club club) => _relations.RecalculateClub(club);
    public TalkResult TalkToPlayer(Player p, TalkTopic topic, PromiseKind? promiseKind = null, DateOnly? deadline = null, SquadStatus? status = null) =>
        _relations.Talk(p, topic, promiseKind, deadline, status);
    public double SquadMood(Club club) => _relations.ClubOf(club).SquadMood;
    public IReadOnlyList<int> SquadLeaders(Club club) => _relations.ClubOf(club).LeaderIds;
    public void RecordNegotiatedStatus(Player p, SquadStatus status) => _relations.RecordNegotiatedStatus(p, status);
}
