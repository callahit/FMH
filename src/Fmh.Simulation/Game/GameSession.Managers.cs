using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Trainerwechsel: Verein, bisheriger Trainer, Nachfolger (falls schon bestimmt), Datum und Grund („sacked“, „moved“).</summary>
public sealed record CoachChange(DateOnly Date, Club Club, Manager Previous, Manager? Successor, string Exit);

/// <summary>Fassade: KI-Manager und Trainerkarussell.</summary>
public sealed partial class GameSession
{
    /// <summary>Trainer eines Vereins (KI oder Mensch); null, wenn die Stelle gerade frei ist.</summary>
    public Manager? ManagerOf(Club club) => club.ManagerId is { } id ? World.Managers.FirstOrDefault(m => m.Id == id) : null;

    public IEnumerable<Manager> FreeManagers => ManagerGenerator.FreeManagers(World);

    /// <summary>Freie Trainerstellen sofort besetzen (sonst am nächsten Tag).</summary>
    public void FillManagerVacancies() => _managers.FillVacancies(World.Date);

    /// <summary>Trainerwechsel ab Saisonbeginn (1. Juli des Startjahres), neueste zuerst.</summary>
    public IReadOnlyList<CoachChange> CoachChanges(int seasonYear)
    {
        var start = new DateOnly(seasonYear, 7, 1);
        var end = start.AddYears(1);
        var changes = new List<CoachChange>();
        foreach (var m in World.Managers.Where(m => !m.Human))
            foreach (var st in m.Career.Stations.Where(s => s.To is { } to && to >= start && to < end && s.Exit is "sacked" or "moved"))
            {
                if (!World.Clubs.TryGetValue(st.ClubId, out var club)) continue;
                var successor = World.Managers.FirstOrDefault(x => x != m && x.Career.Stations.Any(s => s.ClubId == st.ClubId && s.From == st.To));
                changes.Add(new CoachChange(st.To!.Value, club, m, successor, st.Exit));
            }
        return changes.OrderByDescending(c => c.Date).ThenBy(c => c.Club.Id).ToList();
    }
}
