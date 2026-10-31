using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Lizenz“.</summary>
public sealed partial class GameSession
{
    public IReadOnlyList<EuropeanEntry> EuropeanEntriesOf(int seasonYear) => _license.EuropeanEntriesOf(seasonYear);
    public EuropeCompetition? EuropeCompetitionOf(string id) => _license.EuropeCompetitionOf(id);
    public static EuropeCompetition? EuropeCompetitionOf(CountryConfig country, string id) => LicenseModule.EuropeCompetitionOf(country, id);
}
