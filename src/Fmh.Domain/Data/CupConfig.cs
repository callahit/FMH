namespace Fmh.Core.Data;

/// <summary>
/// Pokalformat (Kapitel 3): Teilnehmer, Runden, Heimrecht, Termine. Ein Land kann beliebig viele Pokale haben
/// (Landespokal, nationaler Pokal, Ligapokal, Supercup); Qualifikation und späterer Einstieg stehen in <see cref="Entries"/>.
/// </summary>
public sealed class CupConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// Ligen und Pools, deren Vereine teilnehmen. Mit <see cref="Entries"/> füllen sie nur die freien Plätze bis
    /// <see cref="Teams"/> auf (höhere Ebenen und höherer Ruf zuerst), etwa in der ersten Saison ohne Vorjahresergebnisse.
    /// </summary>
    public List<string> Participants { get; set; } = new();
    /// <summary>Nur Vereine dieser Region (Landespokal); leer = alle.</summary>
    public string Region { get; set; } = "";
    /// <summary>Teilnehmerzahl. Ohne späteren Einstieg wird auf eine Zweierpotenz mit Freilosen aufgefüllt.</summary>
    public int Teams { get; set; } = 64;
    /// <summary>Feste Startplätze in der angegebenen Reihenfolge; Vereine werden nicht doppelt aufgenommen.</summary>
    public List<CupEntry> Entries { get; set; } = new();
    /// <summary>Unterklassige Vereine haben Heimrecht (wie im DFB-Pokal).</summary>
    public bool LowerTierHome { get; set; } = true;
    public int Legs { get; set; } = 1;
    /// <summary>Spiele im Finale, abweichend von <see cref="Legs"/>.</summary>
    public int? FinalLegs { get; set; }
    /// <summary>Spiele je Runde (Index 0 = Runde 1), vorrangig vor <see cref="Legs"/> und <see cref="FinalLegs"/>.</summary>
    public List<int> RoundLegs { get; set; } = new();
    public bool AwayGoals { get; set; }
    /// <summary>Termine je Runde im Format MM-TT (bei zwei Spielen zwei Termine je Runde, durch Komma getrennt).</summary>
    public List<string> Dates { get; set; } = new();
    public bool NeutralFinal { get; set; } = true;
    /// <summary>
    /// Supercup und andere Einzelspiele: Teilnehmer in der Reihenfolge von <see cref="Entries"/> ohne Auslosung,
    /// Heimrecht beim Verein aus dem Eintrag mit diesem Index (null = erster Eintrag).
    /// </summary>
    public bool FixedPairing { get; set; }
    public int? HomeEntry { get; set; }
    /// <summary>Prämien je erreichter Runde und für den Sieger; null = Werte des Landes (<see cref="FinanceConfig.CupPrizes"/>).</summary>
    public List<long>? Prizes { get; set; }
    public long? WinnerPrize { get; set; }
}

/// <summary>
/// Startplatz im Pokal. Entweder Vereine aus Ligen (<see cref="Participants"/>, optional nach dem Abschluss der Vorsaison
/// und nur die ersten <see cref="Count"/>) oder die Bestplatzierten eines anderen Pokals der Vorsaison
/// (<see cref="QualifiersOf"/>, z. B. Landespokalsieger für den DFB-Pokal).
/// </summary>
public sealed class CupEntry
{
    public List<string> Participants { get; set; } = new();
    /// <summary>Abschlusstabelle der Vorsaison statt der aktuellen Ligazugehörigkeit (ohne Vorsaison: aktuelle Liga nach Ruf).</summary>
    public bool PreviousSeason { get; set; }
    /// <summary>Pokal der Vorsaison, dessen Bestplatzierte (Sieger, Finalist, Halbfinalisten …) einziehen.</summary>
    public string QualifiersOf { get; set; } = "";
    /// <summary>Höchstzahl der Vereine aus diesem Eintrag; null = alle.</summary>
    public int? Count { get; set; }
    /// <summary>Nur Vereine, die in dieser Saison europäisch spielen (true) bzw. nicht (false); null = egal.</summary>
    public bool? Europe { get; set; }
    /// <summary>Runde, in der die Vereine einsteigen (1 = von Beginn an).</summary>
    public int Round { get; set; } = 1;
}
