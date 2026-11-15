using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Privatleben des Managers (AP 13b): Wohnung, Auto und Hobbys wechseln.</summary>
public sealed partial class GameSession
{
    /// <summary>Privatleben, wenn das Element an ist und es schon angelegt wurde.</summary>
    public PrivateLife? PrivateLife =>
        Data.PrivateLife is not null && Anstoss.IsOn(World, HumanManager.Id, PrivateLives.Feature)
        && World.PrivateLives.TryGetValue(HumanManager.Id, out var life) ? life : null;

    /// <summary>Fehlertext-Schlüssel oder null.</summary>
    public string? ChangePrivate(PrivateLives.Kind kind, string id) =>
        PrivateLife is { } life ? PrivateLives.Change(Data.PrivateLife!, life, kind, id) : "private.error.off";
}
