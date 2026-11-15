namespace Fmh.Core.Model;

/// <summary>
/// Privatleben eines Managers (AP 13b, Anstoß-Element <c>privateLife</c>): Privatkonto, Stress, Wohnung, Auto und
/// Hobbys. Entsteht erst, wenn das Element an ist.
/// </summary>
public sealed class PrivateLife
{
    /// <summary>Privatvermögen in Euro (getrennt von der Vereinskasse).</summary>
    public long Savings { get; set; }
    /// <summary>Stress 0–100.</summary>
    public double Stress { get; set; }
    /// <summary>Wohnung (Kennung aus rules/privateLife.json).</summary>
    public string Home { get; set; } = "";
    public string Car { get; set; } = "";
    public List<string> Hobbies { get; set; } = new();
    /// <summary>Warnung über hohen Stress ist verschickt (bis der Stress wieder sinkt).</summary>
    public bool StressWarned { get; set; }
}
