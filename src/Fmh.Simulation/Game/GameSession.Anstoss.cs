using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Anstoß-Schalter (AP 13): Hauptschalter und Einzelschalter des Managers, auch im laufenden Spielstand.</summary>
public sealed partial class GameSession
{
    public bool AnstossOn => Anstoss.MasterOn(World, HumanManager.Id);

    public bool AnstossElementOn(string element) => !Anstoss.Settings(World, HumanManager.Id).Disabled.Contains(element);

    public void SetAnstoss(bool on)
    {
        Anstoss.SetMaster(World, HumanManager.Id, on);
        DropSwitchedOff();
        _privateLife.Ensure();
    }

    public void SetAnstossElement(string element, bool on)
    {
        Anstoss.SetElement(World, HumanManager.Id, element, on);
        DropSwitchedOff();
        _privateLife.Ensure();
    }

    /// <summary>Offene Ereignisse ausgeschalteter Elemente verschwinden samt Nachricht, ohne Wirkung.</summary>
    private void DropSwitchedOff()
    {
        var features = Data.Events.Where(d => d.Feature is not null).ToDictionary(d => d.Id, d => d.Feature);
        var dropped = World.Events
            .Where(e => e.Chosen is null && features.TryGetValue(e.EventId, out var f) && !Anstoss.IsOn(World, HumanManager.Id, f))
            .Select(e => e.Id).ToHashSet();
        if (dropped.Count == 0) return;
        World.Events.RemoveAll(e => dropped.Contains(e.Id));
        World.Messages.RemoveAll(m => m.Action == MessageAction.Event && m.ActionRefId is { } id && dropped.Contains(id));
    }
}
