using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Simulation;

/// <summary>Kabine / Spielerbeziehungen (Must-now E3).</summary>
public interface IRelationsService
{
    PlayerRelations Of(Player p);
    ClubRelations ClubOf(Club club);
    void Recalculate(Player p);
    void RecalculateClub(Club club);
    TalkResult Talk(Player p, TalkTopic topic, PromiseKind? promiseKind = null, DateOnly? deadline = null, SquadStatus? status = null);
    void RecordNegotiatedStatus(Player p, SquadStatus status);
    void ApplyMatchMood(MatchTeamState team);
    string? MoodReportKey(Club club);
}
