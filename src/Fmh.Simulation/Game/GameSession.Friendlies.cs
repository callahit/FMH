using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Fassade: Testspiele (AP 9).</summary>
public sealed partial class GameSession
{
    public IReadOnlyList<Club> FriendlyCandidates() => _friendlies.Candidates();
    public IReadOnlyList<DateOnly> FriendlyDates(Club opponent, int days = 60) => _friendlies.FreeDates(opponent, days);
    public string? ArrangeFriendly(Club opponent, DateOnly date, bool home, int homeSharePercent) =>
        _friendlies.Arrange(opponent, date, home, homeSharePercent);
    public void CancelFriendly(Friendly friendly) => _friendlies.Cancel(friendly);
    public IEnumerable<Friendly> OwnFriendlies => World.Friendlies.Where(f => f.Fixture.Involves(HumanClub.Id)).OrderBy(f => f.Fixture.Date);
}
