namespace Fmh.Core.Model;

/// <summary>Nationalmannschaft (AP 8): ein Land mit spielbaren Ligen oder ein fiktives Hintergrundland.</summary>
public sealed class NationalTeam
{
    /// <summary>Ländercode (Nationalität der Spieler).</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Hintergrundland: Kader werden je Spiel erzeugt, keine Spieler der Welt.</summary>
    public bool Background { get; set; }
    /// <summary>Spielstärke 1–20 der Hintergrundländer.</summary>
    public double Strength { get; set; }
    /// <summary>Aktuell nominierte Spieler (leer außerhalb eines Länderspielfensters).</summary>
    public List<int> Squad { get; set; } = new();
    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
}

/// <summary>Länderspiel; Hintergrundländer haben keine Spieler der Welt, ihre Torschützen fehlen im Ergebnis.</summary>
public sealed class InternationalFixture
{
    public int Id { get; set; }
    public string WindowId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string HomeCode { get; set; } = "";
    public string AwayCode { get; set; } = "";
    public MatchResult? Result { get; set; }

    public bool Involves(string code) => HomeCode == code || AwayCode == code;
}

/// <summary>Termine eines Länderspielfensters der laufenden Saison.</summary>
public sealed class InternationalWindowDates
{
    public string Id { get; set; } = "";
    /// <summary>Anreise (Montag): ab hier sind die Nominierten beim Nationalteam.</summary>
    public DateOnly Start { get; set; }
    /// <summary>Letzter Tag beim Nationalteam; am Folgetag sind die Spieler zurück im Verein.</summary>
    public DateOnly End { get; set; }
    public List<DateOnly> MatchDates { get; set; } = new();

    public bool Contains(DateOnly d) => d >= Start && d <= End;
}

/// <summary>Länderspielbilanz eines Spielers.</summary>
public sealed class InternationalCareer
{
    public int Caps { get; set; }
    public int Goals { get; set; }
    /// <summary>Land, für das der Spieler spielt (ab dem ersten Einsatz festgelegt).</summary>
    public string Nation { get; set; } = "";
}
