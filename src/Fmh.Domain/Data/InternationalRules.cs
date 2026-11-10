using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Nationalmannschaften (Datei <c>rules/internationals.json</c>, AP 8): Fenster, Nominierung, Belastung.</summary>
public sealed class InternationalRules
{
    /// <summary>Länderspielfenster der Saison; in diesen Wochen ruht der Ligabetrieb.</summary>
    public List<InternationalWindow> Windows { get; set; } = new();
    /// <summary>Kadergröße einer Nominierung.</summary>
    [Range(11, 40)]
    public int SquadSize { get; set; } = 23;
    /// <summary>Mindestalter für eine Nominierung.</summary>
    [Range(14, 30)]
    public int MinAge { get; set; } = 17;
    /// <summary>Gewicht der Form: Aufschlag auf die Spielstärke je Formpunkt über 5 (Form 1–10 aus den letzten Noten).</summary>
    [Range(0, 3)]
    public double FormWeight { get; set; } = 0.4;
    /// <summary>Konditionsverlust durch die Reise, wenn ein Spieler zum Verein zurückkehrt (zusätzlich zur Belastung der Spiele).</summary>
    [Range(0, 50)]
    public double TravelFatigue { get; set; } = 8;
    /// <summary>Spielsystem der Nationalteams.</summary>
    public string Formation { get; set; } = "442";
    /// <summary>Wechsel je Länderspiel.</summary>
    [Range(0, 11)]
    public int Substitutions { get; set; } = 5;
    /// <summary>Fiktive Länder ohne spielbare Ligen; sie stellen Gegner mit erzeugten Kadern.</summary>
    public List<BackgroundNation> BackgroundNations { get; set; } = new();
    /// <summary>Turniere (EM, WM) mit Qualifikation in den Fenstern der Saison davor (AP 8b).</summary>
    public List<TournamentRule> Tournaments { get; set; } = new();
}

/// <summary>
/// Turnier einer Nationalmannschafts-Endrunde: Qualifikation in den Länderspielfenstern der Saison vor dem Turnierjahr
/// (Gruppen mit Hin- und Rückspiel), Endrunde im Sommer mit Gruppen und K.-o.-Runden auf neutralem Platz.
/// </summary>
public sealed class TournamentRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Abstand in Jahren zwischen zwei Turnieren.</summary>
    [Range(1, 8)]
    public int Every { get; set; } = 4;
    /// <summary>Jahr der ersten Endrunde; weitere folgen im Abstand <see cref="Every"/>.</summary>
    [Range(2000, 2200)]
    public int FirstYear { get; set; } = 2028;
    /// <summary>Erster Spieltag der Endrunde (MM-TT, im Turnierjahr).</summary>
    public string Start { get; set; } = "06-14";
    /// <summary>Teilnehmer der Endrunde; bei weniger Ländern spielen alle.</summary>
    [Range(2, 64)]
    public int Teams { get; set; } = 8;
    /// <summary>Teams je Endrundengruppe (einfache Runde).</summary>
    [Range(2, 8)]
    public int GroupSize { get; set; } = 4;
    /// <summary>Weiterkommende je Gruppe; Gruppen mal Weiterkommende muss eine Zweierpotenz sein.</summary>
    [Range(1, 4)]
    public int Advance { get; set; } = 2;
    /// <summary>Tage zwischen zwei Spieltagen der Endrunde.</summary>
    [Range(1, 10)]
    public int DaysBetween { get; set; } = 4;
    /// <summary>Teams je Qualifikationsgruppe (Hin- und Rückspiel in den Fenstern).</summary>
    [Range(2, 8)]
    public int QualificationGroupSize { get; set; } = 4;
}

/// <summary>Ein Länderspielfenster: beginnt am ersten Montag ab <see cref="Start"/> und dauert <see cref="Days"/> Tage.</summary>
public sealed class InternationalWindow
{
    public string Id { get; set; } = "";
    /// <summary>Frühester Beginn (MM-TT); das Fenster beginnt am ersten Montag ab diesem Tag.</summary>
    public string Start { get; set; } = "09-01";
    /// <summary>Dauer in Tagen ab dem Montag; der letzte Tag ist der Rückreisetag.</summary>
    [Range(3, 21)]
    public int Days { get; set; } = 8;
    /// <summary>Spieltage als Abstand in Tagen vom Montag (3 = Donnerstag, 6 = Sonntag).</summary>
    public List<int> MatchOffsets { get; set; } = new() { 3, 6 };
}

/// <summary>Fiktives Land ohne spielbare Ligen; Spielstärke 1–20 wie bei Vereinen.</summary>
public sealed class BackgroundNation
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    [Range(1, 20)]
    public double Strength { get; set; } = 12;
}
