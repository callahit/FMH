using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Calibrate;

/// <summary>
/// Turniere (AP 8b): Jede Endrunde, deren Jahr im Saisonlauf lag, muss vollständig gespielt sein und einen Sieger haben.
/// </summary>
internal static class TournamentReport
{
    public static bool PrintSummary(World w, GameData data, int firstSeason)
    {
        var ok = true;
        var lines = new List<string>();
        foreach (var rule in data.Internationals.Tournaments)
            for (var year = firstSeason + 1; year <= w.SeasonYear; year++)
            {
                if (!InternationalTournaments.IsTournamentYear(rule, year)) continue;
                var t = w.Tournaments.FirstOrDefault(x => x.Id == InternationalTournaments.Key(rule, year));
                if (t is not { Phase: TournamentPhase.Done } || t.Champion == "")
                {
                    ok = false;
                    lines.Add($"{InternationalTournaments.Key(rule, year)} nicht beendet");
                    continue;
                }
                var name = w.NationalTeams.FirstOrDefault(n => n.Code == t.Champion)?.Name ?? t.Champion;
                lines.Add($"{t.Id}: {name} ({(t.QualificationGroups.Count > 0 ? $"Qualifikation in {t.QualificationGroups.Count} Gruppen" : "ohne Qualifikation")}, {t.Qualified.Count} Teilnehmer)");
            }
        if (lines.Count > 0) Console.WriteLine("Turniere: " + string.Join("; ", lines));
        return ok;
    }
}
