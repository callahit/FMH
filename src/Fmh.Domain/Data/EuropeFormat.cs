namespace Fmh.Core.Data;

/// <summary>Gemeinsames Format der europäischen Wettbewerbe (AP 5): Qualifikation, Ligaphase, K.-o., Koeffizienten.</summary>
public sealed class EuropeFormat
{
    public EuropeAccess Access { get; set; } = new();
    public List<EuropeCompetitionFormat> Competitions { get; set; } = new();

    public EuropeCompetitionFormat? Competition(string id) => Competitions.FirstOrDefault(c => c.Id == id);
}

public sealed class EuropeAccess
{
    public int WindowYears { get; set; } = 5;
    public double Win { get; set; } = 2;
    public double Draw { get; set; } = 1;
    public double BonusQualifyingWin { get; set; } = 0.5;
    public double BonusLeaguePhase { get; set; } = 4;
    public double BonusRoundOf16 { get; set; } = 1;
    public double BonusQuarter { get; set; } = 1;
    public double BonusSemi { get; set; } = 1;
    public double BonusFinal { get; set; } = 1.5;
    public double BonusTitle { get; set; } = 1.5;
    public double DefaultCountryScore { get; set; } = 12;
    public List<EuropeEntryRule> EntryByRank { get; set; } = new();
}

/// <summary>Einstiegsrunde nach Länderkoeffizient-Rang (1 = bestes Land unter den geladenen).</summary>
public sealed class EuropeEntryRule
{
    public int MaxRank { get; set; } = 99;
    public string UclChampion { get; set; } = "league";
    public string UclOther { get; set; } = "po";
    public string Uel { get; set; } = "league";
    public string Uecl { get; set; } = "po";
}

public sealed class EuropeCompetitionFormat
{
    public string Id { get; set; } = "";
    public long EntryPrize { get; set; }
    public double FieldStrength { get; set; } = 13;
    /// <summary>Zielgröße der Ligaphase; bei weniger Teilnehmern wird verkleinert (gerade Zahl).</summary>
    public int TargetLeagueSize { get; set; } = 8;
    /// <summary>Wie viele Teams aus der Ligaphase in die K.-o.-Runde einziehen (Zweierpotenz).</summary>
    public int KnockoutFrom { get; set; } = 4;
    /// <summary>Wettbewerb, in den Qualifikationsverlierer fallen (leer = ausgeschieden).</summary>
    public string DropTo { get; set; } = "";
    public List<EuropeStageFormat> Stages { get; set; } = new();
}

public sealed class EuropeStageFormat
{
    public string Id { get; set; } = "";
    /// <summary>knockout oder league.</summary>
    public string Type { get; set; } = "knockout";
    public string Key { get; set; } = "";
    public int Legs { get; set; } = 2;
    public List<string> Dates { get; set; } = new();
    public long Prize { get; set; }
    public string Weekday { get; set; } = "Wednesday";
}
