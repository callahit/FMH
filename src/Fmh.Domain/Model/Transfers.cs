namespace Fmh.Core.Model;

/// <summary>Spielerstatus im Vertrag (Kapitel 7); bestimmt die Erwartung an Spielzeit.</summary>
public enum SquadStatus { Star, Regular, Rotation, Backup, Prospect }

/// <summary>Kaderplanung je Spieler (Kapitel 5): Stammspieler, Rotation, Ersatz, Perspektive.</summary>
public sealed class TransferStatus
{
    public bool Listed { get; set; }
    public bool LoanListed { get; set; }
    public long? AskingPrice { get; set; }
    /// <summary>Weiterverkaufsbeteiligung des früheren Vereins.</summary>
    public int? SellOnClubId { get; set; }
    public int SellOnPercent { get; set; }
}

/// <summary>Leihe (Kapitel 7): Laufzeit 6–12 Monate, Gehaltsanteil, Kaufoption/-pflicht, Rückholklausel, Einsatzgarantie.</summary>
public sealed class Loan
{
    public int ParentClubId { get; set; }
    /// <summary>Beginn der Leihe (für die Ausnahme von den Leihgrenzen); fehlt in älteren Spielständen.</summary>
    public DateOnly? Since { get; set; }
    public DateOnly Until { get; set; }
    /// <summary>Anteil des Gehalts, den der ausleihende Verein zahlt (0–100 %).</summary>
    public int WageSharePercent { get; set; } = 100;
    public long? PurchaseFee { get; set; }
    public bool PurchaseObligation { get; set; }
    public bool RecallInWinter { get; set; }
    public bool PlayingTimeGuarantee { get; set; }
}

public sealed class ClubBudget
{
    /// <summary>Kontostand. Bis zum Finanzmodul (Phase 3) bewegen nur Transfers und Trainingslager das Konto.</summary>
    public long Balance { get; set; }
    public long TransferBudget { get; set; }
    /// <summary>Gehaltsbudget pro Woche.</summary>
    public long WageBudget { get; set; }
}

public enum TransactionType
{
    TransferIn, TransferOut, SigningFee, AgentFee, Installment, TrainingCamp, LoanFee, Other,
    // Phase 3
    Tickets, SeasonTickets, Tv, Sponsorship, Merchandise, Catering, PrizeMoney, BuildingIncome,
    Wages, StaffWages, Bonuses, Upkeep, Construction, Interest, LoanPayout, LoanRepayment, Deposit, Travel, Fines, Event,
    Youth, European,
    // Wirtschaft: Verwaltung, Spielbetrieb und sonstiges Personal als Anteil der laufenden Einnahmen.
    Operations,
}

/// <summary>Finanzbuchung (Datenmodell Kapitel 12).</summary>
public sealed class Transaction
{
    public DateOnly Date { get; set; }
    public int ClubId { get; set; }
    public TransactionType Type { get; set; }
    public long Amount { get; set; }
    /// <summary>Wörtlicher Text aus Spielständen vor der Mehrsprachigkeit.</summary>
    public string Text { get; set; } = "";
    public Data.TextRef? TextRef { get; set; }

    public string Display(Data.Texts t) => TextRef is { } r ? t.Render(r) : Text;
}

/// <summary>Ratenzahlung einer Ablöse: wird jeweils am Stichtag fällig.</summary>
public sealed class Installment
{
    public int FromClubId { get; set; }
    public int ToClubId { get; set; }
    public DateOnly Due { get; set; }
    public long Amount { get; set; }
    /// <summary>Wörtlicher Text aus Spielständen vor der Mehrsprachigkeit.</summary>
    public string Text { get; set; } = "";
    public Data.TextRef? TextRef { get; set; }

    public string Display(Data.Texts t) => TextRef is { } r ? t.Render(r) : Text;
}

public sealed class TransferRecord
{
    public DateOnly Date { get; set; }
    public int PlayerId { get; set; }
    public int? FromClubId { get; set; }
    public int? ToClubId { get; set; }
    public long Fee { get; set; }
    public bool Loan { get; set; }
    public int SeasonYear { get; set; }
}

public enum DealType { Transfer, Loan, FreeAgent, PreContract, Renewal }
public enum NegotiationStage { ClubTalks, PlayerTalks, Completed, Failed, Withdrawn, Pending }

/// <summary>Angebot an den abgebenden Verein (Kapitel 7).</summary>
public sealed class ClubOffer
{
    public long Fee { get; set; }
    /// <summary>Ratenzahlung über 0–3 Jahre (0 = sofort).</summary>
    public int InstallmentYears { get; set; }
    public long AppearanceBonus { get; set; }
    public long GoalBonus { get; set; }
    /// <summary>Weiterverkaufsbeteiligung 0–50 %.</summary>
    public int SellOnPercent { get; set; }
    public int? SwapPlayerId { get; set; }

    // Leihe
    public int LoanMonths { get; set; } = 12;
    public int WageSharePercent { get; set; } = 100;
    public long? PurchaseFee { get; set; }
    public bool PurchaseObligation { get; set; }
    public bool RecallInWinter { get; set; }
    public bool PlayingTimeGuarantee { get; set; }

    public ClubOffer Clone() => (ClubOffer)MemberwiseClone();
}

/// <summary>Vertragsangebot an den Spieler (Kapitel 7).</summary>
public sealed class ContractOffer
{
    public int Years { get; set; } = 3;
    public int WeeklyWage { get; set; }
    public long SigningFee { get; set; }
    public int AppearanceBonus { get; set; }
    public int GoalBonus { get; set; }
    public long? ReleaseClause { get; set; }
    public int YearlyRaisePercent { get; set; }
    /// <summary>Gehalt bei Abstieg −% bzw. Aufstieg +%; null = übliche Klausel der Spielklasse.</summary>
    public int? RelegationPercent { get; set; }
    public int? PromotionPercent { get; set; }
    public int ExtensionOptionYears { get; set; }
    public SquadStatus Status { get; set; } = SquadStatus.Rotation;

    public ContractOffer Clone() => (ContractOffer)MemberwiseClone();
}

/// <summary>Laufende Verhandlung: erst mit dem Verein, dann mit dem Spieler (und Berater).</summary>
public sealed class Negotiation
{
    public int Id { get; set; }
    public DealType Type { get; set; }
    public NegotiationStage Stage { get; set; }
    public int PlayerId { get; set; }
    public int BuyerId { get; set; }
    public int? SellerId { get; set; }
    public DateOnly Started { get; set; }
    public int ClubRounds { get; set; }
    public int PlayerRounds { get; set; }
    public ClubOffer Offer { get; set; } = new();
    public ContractOffer Contract { get; set; } = new();
    /// <summary>Gegenforderung des Vereins (Ablöse).</summary>
    public long? CounterFee { get; set; }
    /// <summary>Forderung des Spielers (Wochengehalt).</summary>
    public int? WageDemand { get; set; }
    /// <summary>Letzte Antwort (wörtlich, aus Spielständen vor der Mehrsprachigkeit).</summary>
    public string LastResponse { get; set; } = "";
    public Data.TextRef? Response { get; set; }

    public string ResponseText(Data.Texts t) => Response is { } r ? t.Render(r) : LastResponse;
    /// <summary>Angebot eines KI-Vereins an den menschlichen Manager (wartet auf Entscheidung).</summary>
    public bool Incoming { get; set; }
}
