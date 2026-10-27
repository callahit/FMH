namespace Fmh.Core.Model;

/// <summary>Altersstufen des Nachwuchsleistungszentrums (Alter zum Saisonstart): U15 = 14–15, U17 = 16–17, U19 = 18.</summary>
public enum YouthAgeGroup { U15, U17, U19 }

/// <summary>
/// Nachwuchsleistungszentrum eines Vereins (Kapitel 7): vier Qualitätskriterien 1–20, ein Jahresbudget mit
/// Aufteilung, Zertifizierung (0–3 Sterne) und Absolventen. Die Kriterien folgen dem Budget und den Gebäuden
/// nur langsam (monatlich), Investitionen wirken also erst über ein bis zwei Jahre.
/// </summary>
public sealed class YouthAcademy
{
    /// <summary>Trainerarbeit: Budgetanteil Trainer plus Jugendtrainer im Personal.</summary>
    public double Coaching { get; set; } = 8;
    /// <summary>Infrastruktur: Leistungszentrum, Jugendplätze, Internat, Kabinen.</summary>
    public double Facilities { get; set; } = 8;
    /// <summary>Sichtung und Einzugsgebiet: Anzahl und Talent der Neuzugänge.</summary>
    public double Recruitment { get; set; } = 8;
    /// <summary>Schule, Internat und Betreuung: Professionalität, Abbrecherquote.</summary>
    public double Education { get; set; } = 8;

    /// <summary>Jahresbudget der Saison (wird monatlich gebucht).</summary>
    public long Budget { get; set; }
    /// <summary>Aufteilung des Budgets in Prozent: Trainer, Sichtung, Schule; der Rest ist 0.</summary>
    public int ShareCoaching { get; set; } = 50;
    public int ShareRecruitment { get; set; } = 30;
    public int ShareEducation { get; set; } = 20;
    /// <summary>Der Mensch hat das Budget für die laufende Saison festgelegt.</summary>
    public bool BudgetSet { get; set; }

    /// <summary>Spieler, die aus der eigenen Jugend einen Profivertrag bekommen haben.</summary>
    public List<int> Graduates { get; set; } = new();
    /// <summary>Ruf der Akademie 0–100 (Absolventen in der Profimannschaft, Titel der Jugend).</summary>
    public double Reputation { get; set; } = 30;
    /// <summary>Startwerte gesetzt (ältere Spielstände werden beim Laden nachgerüstet).</summary>
    public bool Initialized { get; set; }

    public double Rating => Coaching * 0.35 + Facilities * 0.25 + Recruitment * 0.25 + Education * 0.15;

    /// <summary>Zertifizierung nach Gesamtwert: 0 bis 3 Sterne (Lizenzvoraussetzung).</summary>
    public int Stars => Rating switch { >= 14.5 => 3, >= 10.5 => 2, >= 6.5 => 1, _ => 0 };
}
