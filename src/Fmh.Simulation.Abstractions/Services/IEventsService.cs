using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Game;

namespace Fmh.Core.Simulation;

/// <summary>Dienst des Moduls „Ereignisse“ für andere Module.</summary>
public interface IEventsService
{
    void EventsDaily();
    /// <summary>Entscheidung umsetzen (Option per Id), Wirkung als Nachricht zusammenfassen.</summary>
    string? ResolveEvent(PendingEvent pending, string optionId, bool announce = false);
    PendingEvent? EventOf(Message m);
}
