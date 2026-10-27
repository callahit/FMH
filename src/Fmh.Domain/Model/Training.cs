namespace Fmh.Core.Model;

/// <summary>Trainingsbausteine (Kapitel 5).</summary>
public enum TrainingBlock { Fitness, Technique, Tactics, Finishing, SetPieces, Goalkeeping, Recovery, PracticeMatch, Rest }

public enum TrainingIntensity { Light, Normal, Hard }

/// <summary>Wochenplan: 7 Tage mit Vormittag und Nachmittag (Index = Tag * 2 + Einheit, Montag = 0).</summary>
public sealed class TrainingPlan
{
    public TrainingIntensity Intensity { get; set; } = TrainingIntensity.Normal;
    public List<TrainingBlock> Sessions { get; set; } = Default();

    public static List<TrainingBlock> Default() => new()
    {
        TrainingBlock.Recovery, TrainingBlock.Fitness,       // Mo
        TrainingBlock.Technique, TrainingBlock.Tactics,      // Di
        TrainingBlock.Finishing, TrainingBlock.Fitness,      // Mi
        TrainingBlock.Tactics, TrainingBlock.SetPieces,      // Do
        TrainingBlock.PracticeMatch, TrainingBlock.Rest,     // Fr
        TrainingBlock.Rest, TrainingBlock.Rest,              // Sa (Spieltag)
        TrainingBlock.Recovery, TrainingBlock.Rest,          // So
    };

    public TrainingBlock At(DayOfWeek day, int session)
    {
        var d = ((int)day + 6) % 7;
        return Sessions[d * 2 + session];
    }

    public TrainingPlan Clone() => new() { Intensity = Intensity, Sessions = new List<TrainingBlock>(Sessions) };
}

/// <summary>Individualtraining und Trainingsfortschritt eines Spielers.</summary>
public sealed class PlayerTraining
{
    /// <summary>Schwerpunkt-Attribut (verdoppelt den Fortschritt dort).</summary>
    public Attr? Focus { get; set; }
    /// <summary>Positionsumschulung (dauert 3–12 Monate).</summary>
    public Position? Retrain { get; set; }
    /// <summary>Fortschritt der Umschulung 0–1 je Eignungsstufe.</summary>
    public double RetrainProgress { get; set; }
    /// <summary>Angesparter Fortschritt je Attributgruppe; ab 1,0 steigt ein Attribut.</summary>
    public double Technical { get; set; }
    public double Mental { get; set; }
    public double Physical { get; set; }
    public double Goalkeeping { get; set; }
    public double FocusProgress { get; set; }
}

public enum CampGoal { Recovery, TeamBuilding, Fitness }

public sealed class TrainingCamp
{
    public CampGoal Goal { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public long Cost { get; set; }
    public string Location { get; set; } = "";
}
