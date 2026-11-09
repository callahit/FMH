using System.Globalization;
using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Calibrate;

/// <summary>
/// Kabine (AP 7): Anteil unzufriedener Spieler je Kader (Ziel aus rules/relations.json) und Wechselwünsche
/// unzufriedener gegenüber zufriedener Spieler. Gemessen am Monatsersten über alle Ligavereine.
/// </summary>
internal static class RelationsReport
{
    private static readonly List<double> Shares = new();
    private static int _unhappy, _unhappyWants, _content, _contentWants;
    private static int _lastMonth = -1;

    public static void Observe(World w, GameData data)
    {
        if (w.Date.Month == _lastMonth || w.Date.Day != 1) return;
        _lastMonth = w.Date.Month;
        var threshold = data.Relations.UnhappyThreshold;
        var shares = new List<double>();
        foreach (var club in w.Clubs.Values.Where(c => c.ReserveOf is null && w.FindLeague(c.LeagueId) is not null))
        {
            var squad = w.SquadOf(club).Where(p => !p.IsYouth && w.PlayerRelations.ContainsKey(p.Id)).Select(p => w.PlayerRelations[p.Id]).ToList();
            if (squad.Count == 0) continue;
            shares.Add(squad.Count(r => r.Satisfaction < threshold) / (double)squad.Count);
            foreach (var r in squad)
            {
                if (r.Satisfaction < threshold) { _unhappy++; if (r.WantsTransfer) _unhappyWants++; }
                else if (r.Satisfaction >= 50) { _content++; if (r.WantsTransfer) _contentWants++; }
            }
        }
        // Erst ab einem gefüllten Spieltag aussagekräftig (Monat nach Saisonstart)
        if (shares.Count > 0 && w.Leagues.Any(l => l.CurrentMatchday >= data.Relations.GraceMatchdays)) Shares.Add(shares.Average());
    }

    public static bool PrintSummary(GameData data)
    {
        if (Shares.Count == 0) return true;
        var range = data.Relations.UnhappyShare is [_, _] r ? r : new double[] { 5, 20 };
        var mean = Shares.Average() * 100;
        var unhappyRate = _unhappy == 0 ? 0 : _unhappyWants / (double)_unhappy;
        var contentRate = _content == 0 ? 0 : _contentWants / (double)_content;
        var ok = mean >= range[0] && mean <= range[1] && unhappyRate > contentRate;
        Console.WriteLine($"Kabine: Ø unzufriedene Spieler je Kader {mean.ToString("0.0", CultureInfo.InvariantCulture)} % (Ziel {range[0]:0}–{range[1]:0} %), " +
                          $"Wechselwunsch unzufrieden {unhappyRate:P0} gegenüber zufrieden {contentRate:P0}{(ok ? "" : " ABWEICHUNG")}");
        return ok;
    }
}
