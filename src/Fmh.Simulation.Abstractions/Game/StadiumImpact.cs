namespace Fmh.Core.Game;

/// <summary>Wirkung eines Ausbaus auf Zuschauer und Einnahmen (pro Saison, Ligaspiele).</summary>
public sealed record StadiumImpact(int ExtraSpectators, long ExtraIncome, long ExtraUpkeep, long Cost)
{
    public long NetPerSeason => ExtraIncome - ExtraUpkeep;
    /// <summary>Jahre bis zur Amortisation, null wenn sich der Ausbau nicht rechnet.</summary>
    public double? PaybackYears => NetPerSeason > 0 ? Cost / (double)NetPerSeason : null;
}
