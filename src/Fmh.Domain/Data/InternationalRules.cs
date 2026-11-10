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
