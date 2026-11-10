using System.ComponentModel.DataAnnotations;

namespace Fmh.Core.Data;

/// <summary>Co-Trainer (Datei <c>rules/assistant.json</c>, AP 11): Schwellen für den Trainingsbericht, Urlaubsdauer.</summary>
public sealed class AssistantRules
{
    /// <summary>Ø Kondition der Stammelf, unter der der Co-Trainer zu leichterem Training rät.</summary>
    [Range(0, 100)]
    public double LowCondition { get; set; } = 82;
    /// <summary>Vertrautheit mit dem Spielsystem, unter der mehr Taktiktraining empfohlen wird.</summary>
    [Range(0, 100)]
    public double LowFamiliarity { get; set; } = 60;
    /// <summary>Ø Spielpraxis der Stammelf, unter der ein Trainingsspiel empfohlen wird.</summary>
    [Range(0, 100)]
    public double LowMatchPractice { get; set; } = 50;
    /// <summary>Ab so vielen Verletzten im Kader rät der Co-Trainer zu geringerer Belastung.</summary>
    [Range(1, 30)]
    public int ManyInjured { get; set; } = 4;
    /// <summary>Höchstdauer eines Urlaubs in Tagen.</summary>
    [Range(1, 400)]
    public int MaxHolidayDays { get; set; } = 120;
}
