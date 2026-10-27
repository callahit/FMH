using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Ereignisse“.</summary>
public sealed partial class GameSession
{
    public const int EventDecisionDays = EventsModule.EventDecisionDays;
    public PendingEvent? TriggerRandomEvent(string? forceId = null) => _events.TriggerRandomEvent(forceId);
    public string? ResolveEvent(PendingEvent pending, string optionId, bool announce = false) => _events.ResolveEvent(pending, optionId, announce);
    public List<TextRef> ApplyEffects(Club club, Player? player, Dictionary<string, double> effects) => _events.ApplyEffects(club, player, effects);
    public PendingEvent? EventOf(Message m) => _events.EventOf(m);
}
