using System.Text.Json.Serialization;

namespace Fmh.Core.Model;

/// <summary>Art einer Vertrags- oder Deal-Klausel (Must-now E1-S3/S4).</summary>
public enum ClauseKind
{
    Release,
    RelegationWage,
    PromotionWage,
    YearlyRaise,
    ExtensionOption,
    BuyBack,
    Preemption,
}

/// <summary>
/// Klausel als eigener Datensatz: Art und Parameter. Neue Arten brauchen keine Schemaänderung am Spieler-JSON.
/// Der laufende Vertrag spiegelt Release/Relegation/Promotion weiter am <see cref="Contract"/>; Kaufrechte liegen nur hier.
/// </summary>
public sealed class Clause
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    /// <summary>Vertrag, zu dem die Klausel gehört; null bei reinen Deal-Rechten.</summary>
    public int? ContractId { get; set; }
    public int? DealId { get; set; }
    /// <summary>Verein, dem das Recht zusteht (Rückkauf, Vorkauf) bzw. 0 bei Vertragsgehältern.</summary>
    public int BeneficiaryClubId { get; set; }
    public ClauseKind Kind { get; set; }
    /// <summary>Geldbetrag (Ausstieg, Rückkauf) oder 0.</summary>
    public long Amount { get; set; }
    /// <summary>Prozentparameter (Gehaltsklausel).</summary>
    public int Percent { get; set; }
    /// <summary>Gültig bis; null = unbegrenzt bzw. Vertragsende.</summary>
    public DateOnly? Until { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Offenes Vorkaufsrecht: Drittangebot wartet auf Entscheidung des Begünstigten.</summary>
public sealed class PreemptionOffer
{
    public int ClauseId { get; set; }
    public int NegotiationId { get; set; }
    public int PlayerId { get; set; }
    public int BeneficiaryClubId { get; set; }
    public long Fee { get; set; }
    public DateOnly Expires { get; set; }
}
