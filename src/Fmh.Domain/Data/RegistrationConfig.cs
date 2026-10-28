namespace Fmh.Core.Data;

/// <summary>
/// Meldelisten je Spielklasse (Spielberechtigungsliste der DFL, Squad List der Premier League): Größe, Ausnahmen für Junge,
/// Mindestzahl ausgebildeter und eigener Spieler sowie U-Spieler im Spieltagskader.
/// </summary>
public sealed class RegistrationRules
{
    /// <summary>Höchstzahl gemeldeter Spieler ohne Ausnahme; 0 = Kadergrenze des Landes.</summary>
    public int MaxPlayers { get; set; }
    /// <summary>Spieler, die im Startjahr der Saison höchstens so alt werden, sind ohne Meldeplatz spielberechtigt (0 = keine Ausnahme).</summary>
    public int ExemptAge { get; set; }
    /// <summary>Leihspieler brauchen immer einen Meldeplatz, auch wenn sie jung genug sind (EFL).</summary>
    public bool LoansNeedSlot { get; set; }
    /// <summary>Mindestzahl Spieler, die zwischen 15 und 21 mindestens drei Spielzeiten bei Vereinen des Landes waren (Local Player, Homegrown).</summary>
    public int MinLocal { get; set; }
    /// <summary>Davon mindestens so viele beim eigenen Verein ausgebildet.</summary>
    public int MinClubTrained { get; set; }
    /// <summary>Mindestzahl Spieler mit der Staatsangehörigkeit des Landes.</summary>
    public int MinNationals { get; set; }
    /// <summary>Mindestzahl junger Spieler im Spieltagskader (Startelf und Bank), z. B. vier U23-Spieler in der 3. Liga.</summary>
    public int MatchdayMinYoung { get; set; }
    /// <summary>Jung im Sinne von <see cref="MatchdayMinYoung"/>: wird im Startjahr der Saison höchstens so alt.</summary>
    public int MatchdayYoungAge { get; set; } = 23;
}

/// <summary>Herkunft der Spieler, die zu Spielbeginn erzeugt werden: Anteil im eigenen Verein bzw. im Land ausgebildet.</summary>
public sealed class FormativeShares
{
    public double Club { get; set; } = 0.15;
    public double Country { get; set; } = 0.45;
}
