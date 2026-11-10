using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Was der Co-Trainer während des Urlaubs übernimmt.</summary>
public sealed class HolidayOptions
{
    /// <summary>Der Co-Trainer stellt die Mannschaft auf (sonst bleibt die eigene Taktik, Ausfälle werden ersetzt).</summary>
    public bool Lineup { get; set; } = true;
    /// <summary>Der Co-Trainer plant das Training jeden Montag nach seinem Bericht.</summary>
    public bool Training { get; set; } = true;
    /// <summary>Der Sportdirektor verhandelt Transfers (sonst werden Angebote abgelehnt).</summary>
    public bool Transfers { get; set; }
}

/// <summary>Ergebnis eines Urlaubs: gespielte Partien und ob er vorzeitig endete.</summary>
public sealed class HolidayReport
{
    public DateOnly From { get; init; }
    public DateOnly To { get; set; }
    public List<Fixture> Matches { get; } = new();
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int Decisions { get; set; }
    /// <summary>Grund für ein vorzeitiges Ende (Textschlüssel), sonst null.</summary>
    public string? StoppedKey { get; set; }
}

/// <summary>Fassade: Urlaub mit Delegation an den Co-Trainer und dessen Trainingsbericht (AP 11).</summary>
public sealed partial class GameSession
{
    /// <summary>Bericht des Co-Trainers zum aktuellen Wochenplan.</summary>
    public TrainingReport TrainingReport() =>
        TrainingAdvisor.Advise(World, Data, HumanClub, Familiarity(HumanClub, HumanClub.Tactic.FormationId));

    /// <summary>Den Vorschlag des Co-Trainers als Wochenplan übernehmen.</summary>
    public void ApplyTrainingSuggestion() => HumanClub.Training = TrainingReport().Suggested.Clone();

    /// <summary>
    /// Urlaub bis <paramref name="until"/>: Spiele laufen sofort, offene Entscheidungen übernimmt der Assistent. Endet
    /// vorzeitig, wenn der Manager entlassen wird. Eigene Taktik und eigener Trainingsplan gelten danach wieder.
    /// </summary>
    public HolidayReport TakeHoliday(DateOnly until, HolidayOptions options)
    {
        var limit = World.Date.AddDays(Data.Assistant.MaxHolidayDays);
        if (until > limit) until = limit;
        var report = new HolidayReport { From = World.Date };
        var club = HumanClub;
        var tactic = club.Tactic.Clone();
        var plan = club.Training.Clone();
        var delegateTransfers = HumanManager.Profile.DelegateTransfers;
        HumanManager.Profile.DelegateTransfers = options.Transfers || delegateTransfers;
        var lastPlanned = DateOnly.MinValue;
        try
        {
            for (var guard = 0; guard < 2_000 && World.Date < until; guard++)
            {
                if (AwaitingNewJob) { report.StoppedKey = "holiday.stopped.sacked"; break; }
                if (options.Training && World.Date.DayOfWeek == DayOfWeek.Monday && lastPlanned != World.Date)
                {
                    ApplyTrainingSuggestion();
                    lastPlanned = World.Date;
                }
                var r = Advance(1);
                if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f)
                {
                    _matchday.AiLineupForHuman = options.Lineup;
                    try { PlayInstant(f); }
                    finally { _matchday.AiLineupForHuman = false; }
                    Record(report, f, club.Id);
                }
                if (r.Reason == StopReason.Decision)
                {
                    report.Decisions += PendingDecisions.Count;
                    DelegateDecisions();
                }
            }
        }
        finally
        {
            HumanManager.Profile.DelegateTransfers = delegateTransfers;
            // Nur zurücksetzen, wenn der Manager noch denselben Verein führt.
            if (HumanManager.ClubId == club.Id)
            {
                club.Tactic = tactic;
                club.Training = plan;
                LineupSelector.Repair(club, World, Data, BenchSize(club));
            }
        }
        report.To = World.Date;
        return report;
    }

    private static void Record(HolidayReport report, Fixture f, int clubId)
    {
        report.Matches.Add(f);
        if (f.Result is not { } r || f.Kind == CompetitionKind.Friendly) return;
        var own = f.HomeId == clubId ? r.HomeGoals : r.AwayGoals;
        var other = f.HomeId == clubId ? r.AwayGoals : r.HomeGoals;
        var winner = own == other && r.PenaltiesHome is { } ph ? (f.HomeId == clubId ? ph > r.PenaltiesAway : r.PenaltiesAway > ph) : (bool?)null;
        if (own > other || winner == true) report.Won++;
        else if (own < other || winner == false) report.Lost++;
        else report.Drawn++;
    }
}
