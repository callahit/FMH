namespace Fmh.Core.Model;

/// <summary>Stand des nationalen Lizenzverfahrens der laufenden Saison (Kapitel 8).</summary>
public enum LicenseStage { None, Applied, Decided, Final }

/// <summary>Ergebnis der Financial-Fair-Play-Prüfung (Break-even über drei Spielzeiten, Kaderkostenquote).</summary>
public enum FfpStatus { Compliant, Warning, Fined, Excluded }

/// <summary>Abgeschlossene Saison für Lizenz und FFP: relevante Einnahmen und Ausgaben.</summary>
public sealed class SeasonFinance
{
    public int SeasonYear { get; set; }
    public long Revenue { get; set; }
    public long Expenses { get; set; }
    /// <summary>Break-even-Ergebnis ohne Infrastruktur, Nachwuchs, Kredite und Festgeld.</summary>
    public long BreakEven { get; set; }
    /// <summary>Spielergehälter plus Ablösen (über drei Jahre verteilt) im Verhältnis zum Umsatz.</summary>
    public double SquadCostRatio { get; set; }
    public long EquityAtEnd { get; set; }
}

/// <summary>Lizenzakte eines Vereins: national (Liga) und UEFA, FFP.</summary>
public sealed class LicenseFile
{
    public LicenseStage Stage { get; set; }
    /// <summary>Für die kommende Saison geltende Entscheidung.</summary>
    public LicenseOutcome Outcome { get; set; } = LicenseOutcome.Granted;
    /// <summary>Auflagen, die bis zum Nachweisstichtag zu erfüllen sind (Textschlüssel).</summary>
    public List<string> Conditions { get; set; } = new();
    /// <summary>Infrastruktur-Auflagen nur für die höhere Liga (bei Aufstieg; Verstoß kostet eine Geldstrafe).</summary>
    public List<string> UpperConditions { get; set; } = new();
    public bool UefaLicense { get; set; }
    /// <summary>Gründe für eine verweigerte UEFA-Lizenz (Textschlüssel).</summary>
    public List<string> UefaReasons { get; set; } = new();
    public FfpStatus Ffp { get; set; }
    public long FfpBreakEven { get; set; }
    public double FfpSquadCost { get; set; }
    /// <summary>Wegen FFP von europäischen Wettbewerben ausgeschlossen bis einschließlich dieser Saison.</summary>
    public int ExcludedUntilSeason { get; set; }
    /// <summary>Zwangsabstieg zum Saisonende (Lizenz verweigert).</summary>
    public bool ForcedRelegation { get; set; }
    /// <summary>Lizenz auch für die höhere Liga (sonst kein Aufstieg).</summary>
    public bool PromotionAllowed { get; set; } = true;
}

/// <summary>Europäischer Wettbewerb der kommenden bzw. laufenden Saison (vereinfacht ohne eigene Spiele).</summary>
public sealed class EuropeanEntry
{
    public int ClubId { get; set; }
    public string CompetitionId { get; set; } = "";
    public int SeasonYear { get; set; }
    /// <summary>Erreichte Runde (Index in der Rundenliste des Wettbewerbs), -1 vor dem Start.</summary>
    public int Stage { get; set; } = -1;
    public bool Eliminated { get; set; }
    /// <summary>Nachgerückt, weil ein besser platzierter Verein keine Zulassung hatte.</summary>
    public bool Replacement { get; set; }
}

/// <summary>Grundstück der Stadt für einen Stadionneubau an anderer Stelle (nur nach dem Ereignis).</summary>
public sealed class StadiumSite
{
    public DateOnly Offered { get; set; }
    public DateOnly Expires { get; set; }
    /// <summary>Höchstkapazität, die das Grundstück zulässt.</summary>
    public int MaxCapacity { get; set; } = 60_000;
    public long LandPrice { get; set; }
    public bool Used { get; set; }
}

/// <summary>Planung eines neuen Stadions.</summary>
public sealed class StadiumDesign
{
    public int Capacity { get; set; } = 30_000;
    /// <summary>Anteil Stehplätze in Prozent.</summary>
    public int StandingPercent { get; set; } = 25;
    public int Boxes { get; set; } = 40;
    public bool Roof { get; set; } = true;
    public string Name { get; set; } = "";
}
