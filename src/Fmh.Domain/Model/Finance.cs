namespace Fmh.Core.Model;

/// <summary>Schwierigkeit je Manager (Kapitel 2): wirkt auf Vorstandsgeduld und Finanzpuffer.</summary>
public enum Difficulty { Easy, Normal, Hard, Realistic }

/// <summary>Häufigkeit der Zufallsereignisse (Kapitel 10).</summary>
public enum EventFrequency { Off, Rare, Normal, Often }

/// <summary>Tonfall der Ereignisse, Schlagzeilen und Pressefragen (Kapitel 2): Anstoß-Satire bis FM-Ernst.</summary>
public enum MediaStyle { Satirical, Balanced, Serious }

/// <summary>Eintrittspreise je Bereich (Kapitel 8). Logen kosten pro Loge und Spiel.</summary>
public sealed class TicketPrices
{
    public int Standing { get; set; } = 10;
    public int Seating { get; set; } = 22;
    public int Box { get; set; } = 1_500;
    /// <summary>Rabatt für Dauerkarten in Prozent.</summary>
    public int SeasonTicketDiscount { get; set; } = 20;
    /// <summary>Aufschlag bei Topspielen und Derbys in Prozent.</summary>
    public int TopMatchSurcharge { get; set; } = 20;

    public TicketPrices Clone() => (TicketPrices)MemberwiseClone();
}

/// <summary>Kredit der Hausbank: Annuität, monatlich fällig.</summary>
public sealed class BankLoan
{
    public int Id { get; set; }
    public long Principal { get; set; }
    public long Remaining { get; set; }
    public double InterestPercent { get; set; }
    public int Years { get; set; }
    public long MonthlyPayment { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
}

/// <summary>Festgeld: Kapital ist bis zum Ablauf gebunden, Zinsen am Ende.</summary>
public sealed class FixedDeposit
{
    public long Amount { get; set; }
    public double InterestPercent { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly Until { get; set; }
}

public enum LicenseOutcome { Granted, Conditions, PointDeduction, Refused }

/// <summary>Monatsbilanz für Verlauf und Diagramm.</summary>
public sealed class MonthRecord
{
    public int Year { get; set; }
    public int Month { get; set; }
    public long Income { get; set; }
    public long Expenses { get; set; }
    public long Balance { get; set; }
}

/// <summary>Finanzlage eines Vereins (Kapitel 8).</summary>
public sealed class FinanceState
{
    public TicketPrices Prices { get; set; } = new();
    public List<BankLoan> Loans { get; set; } = new();
    public FixedDeposit? Deposit { get; set; }
    /// <summary>Tage in Folge unter dem Kreditrahmen; ab 30 Insolvenz.</summary>
    public int DaysBelowLimit { get; set; }
    public int SeasonTickets { get; set; }
    /// <summary>Einnahmen und Ausgaben der laufenden Saison je Buchungsart.</summary>
    public Dictionary<TransactionType, long> Season { get; set; } = new();
    public Dictionary<TransactionType, long> LastSeason { get; set; } = new();
    public List<MonthRecord> History { get; set; } = new();
    public LicenseOutcome LastLicense { get; set; } = LicenseOutcome.Granted;
    /// <summary>Fanstimmungs- und Zuschauerfaktor durch die Preispolitik (0,5–1,2), gleitend angepasst.</summary>
    public double PriceSatisfaction { get; set; } = 1.0;
    public bool Insolvent { get; set; }
    /// <summary>Punktabzug aus der Lizenzierung, gilt in der kommenden Saison.</summary>
    public int PendingDeduction { get; set; }
    /// <summary>Abgeschlossene Spielzeiten (für FFP und Lizenz), neueste zuletzt.</summary>
    public List<SeasonFinance> Seasons { get; set; } = new();
    public long MonthIncome { get; set; }
    public long MonthExpenses { get; set; }
}
