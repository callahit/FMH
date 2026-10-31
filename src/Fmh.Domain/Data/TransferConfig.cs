namespace Fmh.Core.Data;

/// <summary>Stichtag für die Altersgrenze der Ausnahme von den Leihgrenzen.</summary>
public enum LoanAgeCutoff
{
    /// <summary>Die Leihe beginnt vor dem Ende der Saison, in der der Spieler die Altersgrenze erreicht (FIFA, DFL, RFEF).</summary>
    SeasonEnd,
    /// <summary>Der Spieler ist am 31. Dezember des Startjahres der Saison jünger als die Altersgrenze (FIGC: unter 23).</summary>
    December31,
}

/// <summary>
/// Leihgrenzen (FIFA-Transferstatut Art. 10 bzw. die nationalen Umsetzungen): Höchstzahl gleichzeitig verliehener und
/// ausgeliehener Spieler, Höchstzahl mit demselben Verein, Ausnahme für junge bzw. selbst ausgebildete Spieler.
/// 0 bei einer Grenze bedeutet: keine Grenze.
/// </summary>
public sealed class LoanRules
{
    /// <summary>Höchstzahl gleichzeitig an andere Vereine verliehener Spieler.</summary>
    public int MaxOut { get; set; } = 6;
    /// <summary>Höchstzahl gleichzeitig von anderen Vereinen ausgeliehener Spieler.</summary>
    public int MaxIn { get; set; } = 6;
    /// <summary>Höchstzahl der in einer Saison ausgeliehenen Spieler (Premier League: vier); 0 = keine Grenze.</summary>
    public int MaxInPerSeason { get; set; }
    /// <summary>Höchstzahl gleichzeitiger Leihen mit demselben Verein, je Richtung.</summary>
    public int MaxPerClub { get; set; } = 3;
    /// <summary>Spieler bis zu diesem Alter sind ausgenommen (0 = keine Ausnahme), Stichtag siehe <see cref="AgeCutoff"/>.</summary>
    public int ExemptAge { get; set; } = 21;
    public LoanAgeCutoff AgeCutoff { get; set; } = LoanAgeCutoff.SeasonEnd;
    /// <summary>Die Ausnahme gilt nur für Spieler, die der verleihende Verein selbst ausgebildet hat.</summary>
    public bool ExemptNeedsClubTrained { get; set; } = true;
    /// <summary>Die Grenze je Partnerverein gilt auch für ausgenommene Spieler (FIGC).</summary>
    public bool PerClubCountsExempt { get; set; }
    /// <summary>Längste Leihdauer in Monaten.</summary>
    public int MaxMonths { get; set; } = 24;
    /// <summary>Abweichende Höchstzahlen ab einer Saison (stufenweise Einführung, z. B. FIGC 10 → 9 → 8).</summary>
    public List<LoanStep> Steps { get; set; } = new();
    /// <summary>Herkunft der Zahlen.</summary>
    public string? Source { get; set; }

    /// <summary>Höchstzahlen (verliehen, ausgeliehen) in der Saison mit dem Startjahr <paramref name="seasonYear"/>.</summary>
    public (int maxOut, int maxIn) Limits(int seasonYear)
    {
        var step = Steps.Where(s => s.FromSeason <= seasonYear).OrderByDescending(s => s.FromSeason).FirstOrDefault();
        return step is null ? (MaxOut, MaxIn) : (step.MaxOut, step.MaxIn);
    }
}

public sealed class LoanStep
{
    public int FromSeason { get; set; }
    public int MaxOut { get; set; }
    public int MaxIn { get; set; }
}

/// <summary>
/// Nationale Leihregeln eines Landes: gelten für Leihen zwischen zwei Vereinen dieses Landes. Einzelne Spielklassen können
/// abweichen (Premier League: höchstens zwei Leihspieler von Vereinen der eigenen Liga). Leihen über Landesgrenzen regelt
/// <see cref="TransferRules.InternationalLoans"/>.
/// </summary>
public sealed class CountryLoanRules
{
    public LoanRules Default { get; set; } = new();
    /// <summary>Abweichende Regeln je Division (Id), gelten für Leihen zwischen zwei Vereinen dieser Division.</summary>
    public Dictionary<string, LoanRules> Divisions { get; set; } = new();
}

/// <summary>
/// Verhalten der KI-Vereine eines Landes auf dem Transfermarkt. In Italien sind rund 20 % der Spieler ausgeliehen, in der
/// Bundesliga deutlich weniger (CIES Football Observatory).
/// </summary>
public sealed class MarketTendency
{
    /// <summary>Wie gern Vereine Spieler ausleihen statt kaufen (1 = Durchschnitt).</summary>
    public double LoanAffinity { get; set; } = 1.0;
    /// <summary>Anteil der Leihen mit Kaufpflicht (Italien: obbligo di riscatto).</summary>
    public double PurchaseObligationShare { get; set; } = 0.1;
    /// <summary>Kalibrierungsziel: Anteil der Leihspieler in den Kadern je Ebene (Ebene → [min, max], Stichtag 1. März).</summary>
    public Dictionary<int, double[]> LoanShare { get; set; } = new();
    public string? Source { get; set; }
}

/// <summary>Kalibrierungsziele des Transfermarkts für die höchste Spielklasse (Mittel über alle Saisons).</summary>
public sealed class MarketReference
{
    /// <summary>Neuzugänge je Verein und Saison (Ablöse, ablösefrei, Leihe): [min, max].</summary>
    public double[] NewSigningsPerClub { get; set; } = { 0, double.MaxValue };
    /// <summary>Ablösetransfers je Verein und Saison: [min, max].</summary>
    public double[] FeeSigningsPerClub { get; set; } = { 0, double.MaxValue };
    /// <summary>Ablösetransfers der ersten Saison mindestens dieser Anteil vom Mittel der Folgesaisons (Review F-07).</summary>
    public double SeasonOneMinRatio { get; set; } = 0.5;
    public string? Source { get; set; }
}

/// <summary>
/// Transferstrategie eines KI-Vereins (nach CIES Football Observatory und der Typologie von Vereinen nach Ressourcen und
/// Marktverhalten): Spitzenvereine kaufen Qualität und verleihen überzählige Talente, Ausbildungsvereine kaufen junge
/// Spieler mit Potenzial und verkaufen sie weiter, Leihnehmer füllen den Kader mit Leihspielern stärkerer Vereine,
/// Vereine der unteren Spielklassen holen vor allem ablösefreie Spieler.
/// </summary>
public enum TransferStrategy { Elite, Developer, Borrower, Bargain }

/// <summary>Verhalten einer Strategie im Transferfenster (je Woche bzw. je Fenster).</summary>
public sealed class StrategyProfile
{
    /// <summary>Wahrscheinlichkeit je Woche im Fenster, einen Kauf zu versuchen.</summary>
    public double SigningChance { get; set; }
    public int MaxSummerSignings { get; set; }
    public int MaxWinterSignings { get; set; }
    /// <summary>Wahrscheinlichkeit je Woche im Fenster, einen Leihspieler zu suchen (vor der Leihneigung des Landes).</summary>
    public double BorrowChance { get; set; }
    /// <summary>Kauft bevorzugt junge Spieler mit Potenzial (bis zu diesem Alter); 0 = nach aktueller Stärke.</summary>
    public int YouthFocusAge { get; set; }
    /// <summary>
    /// Kauft Spieler höchstens so viel stärker als das Niveau des Vereins (Ansehen). Größere Sprünge machen Vereine selten;
    /// ohne Grenze wandern die Besten Jahr für Jahr nach oben und die Ligen werden stetig stärker (Review F-02).
    /// </summary>
    public double MaxAboveLevel { get; set; } = 1.5;
    /// <summary>Verleiht Spieler außerhalb der besten so vielen des Kaders, wenn sie jung sind; 0 = verleiht nicht aktiv.</summary>
    public int LendBeyondRank { get; set; }
}

/// <summary>Weltweite Transferregeln (rules/transfers.json).</summary>
public sealed class TransferRules
{
    /// <summary>Leihen zwischen Vereinen verschiedener Länder (FIFA-Transferstatut Art. 10, ab 2024/25).</summary>
    public LoanRules InternationalLoans { get; set; } = new();
    /// <summary>Tiefste Spielklasse, in der Vereine Ablösen zahlen, Leihspieler aufnehmen und Strategien verfolgen.</summary>
    public int ProfessionalLevels { get; set; } = 3;
    public Dictionary<TransferStrategy, StrategyProfile> Strategies { get; set; } = new();
    public MarketReference Reference { get; set; } = new();

    public StrategyProfile Profile(TransferStrategy s) => Strategies.TryGetValue(s, out var p) ? p : new StrategyProfile();
}
