using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Saison“.</summary>
public sealed partial class GameSession
{
    public Position NeededPosition(Club club) => _season.NeededPosition(club);
    public bool IsTransferWindowOpen(Club? club = null) => _season.IsTransferWindowOpen(club);
}
