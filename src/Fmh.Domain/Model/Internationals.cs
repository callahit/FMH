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
    /// <summary>Nationaltrainer (Manager-Id; auch der menschliche Manager, neben seinem Verein), leer = unbesetzt.</summary>
    public int? CoachId { get; set; }
    /// <summary>Ziel des Verbands für das nächste Turnier: Q (Endrunde erreichen), KO4 (Halbfinale), KO2 (Finale).</summary>
    public string CoachTarget { get; set; } = "";
    /// <summary>Vertrag bis zum Turnier dieses Jahres; danach wird abgerechnet.</summary>
    public int CoachUntil { get; set; }
    /// <summary>Spielsystem; leer = Vorgabe aus den Daten.</summary>
    public string Formation { get; set; } = "";
    /// <summary>Vom menschlichen Nationaltrainer vorgemerkte Spieler; fehlende Plätze füllt die KI auf.</summary>
    public List<int> PlannedSquad { get; set; } = new();
    /// <summary>Offenes Angebot an den menschlichen Manager (gültig bis), sonst leer.</summary>
    public DateOnly? OfferExpires { get; set; }
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
    /// <summary>Turnier-Id (z. B. EM-2028); leer bei Freundschaftsspielen.</summary>
    public string? TournamentId { get; set; }
    /// <summary>Runde: Q-A (Qualifikation Gruppe A), G-A (Endrunde Gruppe A), KO4 (Halbfinale), KO2 (Finale) usw.</summary>
    public string Stage { get; set; } = "";
    /// <summary>Sieger eines K.-o.-Spiels (nach Verlängerung oder Elfmeterschießen).</summary>
    public string? WinnerCode { get; set; }

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

public enum TournamentPhase { Qualification, Finals, Done }

/// <summary>Turnier (AP 8b): Qualifikation, Endrunde und Sieger.</summary>
public sealed class InternationalTournament
{
    /// <summary>Regel-Id und Jahr, z. B. EM-2028.</summary>
    public string Id { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Year { get; set; }
    public TournamentPhase Phase { get; set; }
    public DateOnly FinalsStart { get; set; }
    public List<TournamentGroup> QualificationGroups { get; set; } = new();
    public List<string> Qualified { get; set; } = new();
    public List<TournamentGroup> Groups { get; set; } = new();
    public string Champion { get; set; } = "";
}

public sealed class TournamentGroup
{
    public string Name { get; set; } = "";
    public List<string> Codes { get; set; } = new();
}
