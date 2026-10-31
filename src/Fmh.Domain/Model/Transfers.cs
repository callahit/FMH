using System.Text.Json.Serialization;

namespace Fmh.Core.Model;

/// <summary>Spielerstatus im Vertrag (Kapitel 7); bestimmt die Erwartung an Spielzeit.</summary>
public enum SquadStatus { Star, Regular, Rotation, Backup, Prospect }

/// <summary>Kaderplanung je Spieler (Kapitel 5): Stammspieler, Rotation, Ersatz, Perspektive.</summary>
public sealed class TransferStatus
{
    public bool Listed { get; set; }
    public bool LoanListed { get; set; }
    public long? AskingPrice { get; set; }
    /// <summary>Weiterverkaufsbeteiligung des früheren Vereins (ein Recht, Spielstände vor mehreren Rechten).</summary>
    public int? SellOnClubId { get; set; }
    public int SellOnPercent { get; set; }
    /// <summary>
    /// Mehrere Weiterverkaufsrechte. Null, solange nur das einzelne ältere Feld gilt; wird beim nächsten Wechsel übernommen.
    /// Leere Listen werden nicht geschrieben, damit ältere Spielstände unverändert bleiben.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SellOnRight>? SellOns { get; set; }
}

/// <summary>Bemessungsgrundlage einer Weiterverkaufsbeteiligung.</summary>
public enum SellOnBasis
{
    /// <summary>Anteil vom Bruttoerlös.</summary>
    Gross = 0,
    /// <summary>Anteil vom Gewinn (Erlös minus ursprüngliche Ablöse).</summary>
    Profit = 1,
}

/// <summary>Weiterverkaufsrecht eines früheren Vereins an späteren Wechseln. Es bleibt bestehen, bis der Anteil entfällt.</summary>
public sealed class SellOnRight
{
    public int ClubId { get; set; }
    /// <summary>Anteil, 1–50. Die Summe aller Rechte eines Spielers bleibt bei höchstens 50 %.</summary>
    public int Percent { get; set; }
    /// <summary>Deal, aus dem das Recht stammt; 0 bei Rechten aus älteren Spielständen.</summary>
    public int OriginDealId { get; set; }
    /// <summary>Brutto oder Gewinn; ältere Rechte ohne Feld gelten als Brutto.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SellOnBasis Basis { get; set; }
    /// <summary>Ursprüngliche Ablöse für die Gewinnbasis; 0 bei Bruttorechten oder älteren Spielständen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long CostBasis { get; set; }
}

/// <summary>Bedingung einer Kaufpflicht am Ende einer Leihe.</summary>
public enum LoanBuyCondition
{
    /// <summary>Immer kaufen (bisheriges Verhalten).</summary>
    Always = 0,
    /// <summary>Mindestanteil Einsätze an den Pflichtspielen des Leihvereins (<see cref="Loan.PurchaseConditionValue"/> %).</summary>
    AppearancePercent = 1,
    /// <summary>Leihverein hält die Spielklasse (kein Abstieg gegenüber dem Niveau zu Leihbeginn).</summary>
    StayInDivision = 2,
}

/// <summary>Leihe (Kapitel 7): Laufzeit 6–24 Monate, Gehaltsanteil, Kaufoption/-pflicht, Rückholklausel, Einsatzgarantie.</summary>
public sealed class Loan
{
    public int ParentClubId { get; set; }
    /// <summary>
    /// Beginn der Leihe; null bei Leihen aus älteren Spielständen (Einsatzgarantie zählt dann ab Saisonbeginn;
    /// Leihgrenzen-Ausnahmen rechnen mit dem aktuellen Datum).
    /// </summary>
    public DateOnly? Start { get; set; }
    public DateOnly Until { get; set; }
    /// <summary>Anteil des Gehalts, den der ausleihende Verein zahlt (0–100 %).</summary>
    public int WageSharePercent { get; set; } = 100;
    public long? PurchaseFee { get; set; }
    public bool PurchaseObligation { get; set; }
    /// <summary>Bedingung der Kaufpflicht; ohne Feld gilt <see cref="LoanBuyCondition.Always"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public LoanBuyCondition PurchaseCondition { get; set; }
    /// <summary>Parameter der Bedingung (z. B. Einsatzquote in %).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PurchaseConditionValue { get; set; }
    /// <summary>Spielklassenniveau des Leihvereins zu Beginn (für Klassenerhalt).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StartDivisionLevel { get; set; }
    public bool RecallInWinter { get; set; }
    /// <summary>
    /// Einsatzgarantie: Der Spieler muss in mindestens der Hälfte der Pflichtspiele des ausleihenden Vereins eingesetzt
    /// werden (Regel in <c>TransferTerms</c>). Wird sie verfehlt, darf der Stammverein ihn im Winter zurückholen.
    /// </summary>
    public bool PlayingTimeGuarantee { get; set; }
    /// <summary>Pflichtspiele des ausleihenden Vereins seit Beginn der Leihe (nur mit Einsatzgarantie gezählt).</summary>
    public int ClubMatches { get; set; }
    /// <summary>Davon Einsätze des Spielers.</summary>
    public int Appearances { get; set; }
    /// <summary>Der Stammverein wurde über die verfehlte Einsatzgarantie bereits informiert.</summary>
    public bool GuaranteeBreachNotified { get; set; }
    /// <summary>Einmalige Leihgebühr an den Stammverein. 0 wird nicht geschrieben.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Fee { get; set; }
}

/// <summary>Art einer erfolgsabhängigen Zahlung an den abgebenden Verein.</summary>
public enum TransferConditionKind { Appearance, Goal }

/// <summary>
/// Erfolgsabhängige Zahlung an den abgebenden Verein (Zusatzzahlung aus <see cref="ClubOffer.AppearanceBonus"/> bzw.
/// <see cref="ClubOffer.GoalBonus"/>): je Einsatz bzw. Tor für den Käufer bis zum Ende des ersten Vertrags.
/// </summary>
public sealed class TransferCondition
{
    public int PlayerId { get; set; }
    /// <summary>Käufer, der zahlt.</summary>
    public int PayerId { get; set; }
    /// <summary>Abgebender Verein, der die Zahlung erhält.</summary>
    public int PayeeId { get; set; }
    public TransferConditionKind Kind { get; set; }
    /// <summary>Betrag je Einsatz bzw. Tor.</summary>
    public long Amount { get; set; }
    /// <summary>Letzter Tag, an dem die Bedingung zählt (Ende des ersten Vertrags beim Käufer).</summary>
    public DateOnly Until { get; set; }
    /// <summary>Bisher erfüllte Einsätze bzw. Tore.</summary>
    public int Count { get; set; }
    /// <summary>Bisher gezahlte Summe.</summary>
    public long Paid { get; set; }
    /// <summary>Deal, zu dem die Bedingung gehört; 0 bei älteren Spielständen.</summary>
    public int DealId { get; set; }
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
    /// <summary>Deal, zu dem die Rate gehört; 0 bei älteren Spielständen.</summary>
    public int DealId { get; set; }

    public string Display(Data.Texts t) => TextRef is { } r ? t.Render(r) : Text;
}

/// <summary>
/// Ein Vertrag als eigener Datensatz: die laufende Fassung steht zusätzlich am Spieler, frühere Fassungen bleiben mit
/// <see cref="SupersededBy"/> in der Historie. Verträge aus der Welterzeugung bekommen eine Id erst, wenn sie abgelöst werden.
/// </summary>
public sealed class ContractRecord
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public int ClubId { get; set; }
    /// <summary>Deal des Wechsels, 0 bei Verlängerung oder älteren Verträgen.</summary>
    public int DealId { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public int WeeklyWage { get; set; }
    public bool Current { get; set; }
    public int? SupersededBy { get; set; }
    /// <summary>Vollständige Vertragsbedingungen (Must-now E6-S2); null bei älteren Historienzeilen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Contract? Terms { get; set; }
}

public sealed class TransferRecord
{
    /// <summary>Deal dieses Wechsels; 0 bei Einträgen aus älteren Spielständen. Tausch und Hauptwechsel teilen sich die Id.</summary>
    public int DealId { get; set; }
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
    /// <summary>
    /// Ältere Ratenzahlung über 0–3 gleiche Jahresraten. Wird beim Einreichen auf
    /// <see cref="DownPayment"/> / <see cref="InstallmentCount"/> abgebildet, wenn diese noch 0 sind.
    /// </summary>
    public int InstallmentYears { get; set; }
    /// <summary>Anzahlung; bei Sofortzahlung gleich der Ablöse. Belastet das Transferbudget sofort.</summary>
    public long DownPayment { get; set; }
    /// <summary>Anzahl späterer Raten (0–8), verteilt über höchstens vier Saisons.</summary>
    public int InstallmentCount { get; set; }
    /// <summary>Zusatzzahlung an den abgebenden Verein je Pflichtspiel-Einsatz für den Käufer (bis Vertragsende).</summary>
    public long AppearanceBonus { get; set; }
    /// <summary>Zusatzzahlung an den abgebenden Verein je Pflichtspiel-Tor für den Käufer (bis Vertragsende).</summary>
    public long GoalBonus { get; set; }
    /// <summary>Weiterverkaufsbeteiligung 0–50 %.</summary>
    public int SellOnPercent { get; set; }
    /// <summary>Bruttoerlös oder Gewinn als Bemessungsgrundlage der Weiterverkaufsbeteiligung.</summary>
    public SellOnBasis SellOnBasis { get; set; }
    public int? SwapPlayerId { get; set; }

    // Leihe
    /// <summary>Leihdauer in Monaten: 6, 12, 18 oder 24.</summary>
    public int LoanMonths { get; set; } = 12;
    public int WageSharePercent { get; set; } = 100;
    public long? PurchaseFee { get; set; }
    public bool PurchaseObligation { get; set; }
    public LoanBuyCondition PurchaseCondition { get; set; }
    /// <summary>Parameter der Kaufpflicht (z. B. 50 für 50 % Einsätze).</summary>
    public int PurchaseConditionValue { get; set; } = 50;
    public bool RecallInWinter { get; set; }
    public bool PlayingTimeGuarantee { get; set; }
    /// <summary>Einmalige Leihgebühr an den Stammverein.</summary>
    public long LoanFee { get; set; }
    /// <summary>Rückkaufklausel zugunsten des Verkäufers; null = keine.</summary>
    public long? BuyBackFee { get; set; }
    /// <summary>Jahre, in denen der Rückkauf möglich ist (1–3).</summary>
    public int BuyBackYears { get; set; } = 2;
    /// <summary>Vorkaufsrecht des Verkäufers bei späteren Drittangeboten.</summary>
    public bool PreemptionRight { get; set; }

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
    /// <summary>Einseitige Verlängerungsoption des Vereins um 0–2 Jahre (siehe <see cref="Contract.ExtensionOptionYears"/>).</summary>
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
