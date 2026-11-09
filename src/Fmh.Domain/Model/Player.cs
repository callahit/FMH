using System.Text.Json.Serialization;

namespace Fmh.Core.Model;

/// <summary>Moral in sieben Stufen von "Sehr schlecht" bis "Super" (Kapitel 4).</summary>
public enum Morale { VeryBad, Bad, Poor, Okay, Good, VeryGood, Superb }

public enum PreferredFoot { Left, Right, Both }

public sealed class Player
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public List<string> Nationalities { get; set; } = new();
    public int Height { get; set; }
    public int Weight { get; set; }
    /// <summary>Stärke des linken bzw. rechten Fußes, je 1–20.</summary>
    public int LeftFoot { get; set; } = 8;
    public int RightFoot { get; set; } = 16;

    public Position MainPosition { get; set; }
    /// <summary>Eignung je Position. Fehlende Positionen gelten als ungeeignet.</summary>
    public Dictionary<Position, Suitability> Positions { get; set; } = new();

    public int? ClubId { get; set; }
    public Contract? Contract { get; set; }
    /// <summary>Reputation 1–20.</summary>
    public int Reputation { get; set; } = 5;

    public PlayerAttributes Attributes { get; set; } = new();
    public HiddenAttributes Hidden { get; set; } = new();
    public PlayerState State { get; set; } = new();
    public PlayerSeasonStats SeasonStats { get; set; } = new();
    public List<PlayerSeasonStats> History { get; set; } = new();
    public TransferStatus Transfer { get; set; } = new();
    public Loan? Loan { get; set; }
    public PlayerTraining Training { get; set; } = new();
    /// <summary>Jugendspieler (U19/U17) des Vereins, noch nicht im Profikader.</summary>
    public bool IsYouth { get; set; }
    public bool Retired { get; set; }
    /// <summary>Vorvertrag: Wechsel ablösefrei zum 1. Juli (Kapitel 7).</summary>
    public PreContract? PreContract { get; set; }
    /// <summary>Meldung für die Liga und Ausbildungsjahre (Local Player, Homegrown).</summary>
    public PlayerRegistration Registration { get; set; } = new();

    public string Name => $"{FirstName} {LastName}";
    public string ShortName => LastName;

    public int Age(DateOnly today)
    {
        var age = today.Year - BirthDate.Year;
        if (today < BirthDate.AddYears(age)) age--;
        return age;
    }

    public Suitability SuitabilityFor(Position p) =>
        Positions.TryGetValue(p, out var s) ? s : Suitability.Unsuitable;

    public PreferredFoot Foot =>
        Math.Abs(LeftFoot - RightFoot) <= 3 ? PreferredFoot.Both : LeftFoot > RightFoot ? PreferredFoot.Left : PreferredFoot.Right;

    /// <summary>Ohne Spielkontext: nicht verletzt, keine Ereignissperre und nicht für die Liga gesperrt.</summary>
    public bool IsAvailable => State.Injury is null && State.PersonalBan == 0 && State.Ban(PlayerState.LeagueScope) == 0;

    /// <summary>
    /// Einsatzfähig für einen Wettbewerb. <paramref name="scope"/> null prüft nur Verletzung und Ereignissperre,
    /// nicht die Wettbewerbssperren (die gelten nur in ihrem Bereich).
    /// </summary>
    public bool IsAvailableFor(string? scope) =>
        State.Injury is null && State.PersonalBan == 0 && (scope is null || State.Ban(scope) == 0);
}

public sealed class PlayerRegistration
{
    /// <summary>Verein, auf dessen Meldeliste der Spieler steht (gilt für Liga und nationale Pokale); null = nicht gemeldet.</summary>
    public int? ClubId { get; set; }
    /// <summary>Spielzeiten zwischen 15 und 21 je Verein (Vereins-Id; −1 = im Ausland).</summary>
    public Dictionary<int, int> Formative { get; set; } = new();
}

public sealed class PreContract
{
    public int ClubId { get; set; }
    public Contract Contract { get; set; } = new();
}

public sealed class Contract
{
    /// <summary>Grundgehalt intern pro Woche gespeichert (Kapitel 7); Anzeige je Managerprofil.</summary>
    public int WeeklyWage { get; set; }
    public DateOnly Until { get; set; }
    public DateOnly Start { get; set; }
    public long SigningFee { get; set; }
    public int AppearanceBonus { get; set; }
    public int GoalBonus { get; set; }
    public long? ReleaseClause { get; set; }
    public int YearlyRaisePercent { get; set; }
    /// <summary>Abstiegsklausel älterer Spielstände: Gehalt −30 % nach Abstieg (neu: <see cref="RelegationPercent"/>).</summary>
    public bool RelegationClause { get; set; }
    /// <summary>
    /// Gehaltsänderung bei Abstieg (−%) bzw. Aufstieg (+%) des Vereins, je Spielklasse. null = Vertrag aus älterem
    /// Spielstand: Gehalt folgt dem Gehaltsniveau der neuen Spielklasse.
    /// </summary>
    public int? RelegationPercent { get; set; }
    public int? PromotionPercent { get; set; }
    /// <summary>
    /// Einseitige Verlängerungsoption des Vereins (Jahre, 0 = keine). Der Verein kann sie im letzten Vertragsjahr bis zum
    /// Stichtag für Vorverträge ziehen; der Vertrag verlängert sich dann zu gleichen Bedingungen, die Option ist verbraucht.
    /// </summary>
    public int ExtensionOptionYears { get; set; }
    public SquadStatus Status { get; set; } = SquadStatus.Rotation;
    /// <summary>Jugendvertrag (vor dem ersten Profivertrag).</summary>
    public bool Youth { get; set; }
}

public sealed class PlayerState
{
    /// <summary>Gemeinsamer Bereich aller Ligaspiele und der Relegation.</summary>
    public const string LeagueScope = "league";
    /// <summary>Vorlage für Pokalschwellen; der Zähler selbst hängt an der Pokal-Id.</summary>
    public const string CupScope = "cup";
    /// <summary>Vorlage, wenn ein Europapokal keinen eigenen Bereich hat.</summary>
    public const string InternationalScope = "international";

    /// <summary>Kondition 0–100 %.</summary>
    public double Condition { get; set; } = 100;
    /// <summary>Spielpraxis 0–100 %.</summary>
    public double MatchPractice { get; set; } = 70;
    public Morale Morale { get; set; } = Morale.Okay;
    /// <summary>Noten der letzten Spiele (1,0–6,0), neueste zuletzt, höchstens 5.</summary>
    public List<double> RecentGrades { get; set; } = new();
    public Injury? Injury { get; set; }

    /// <summary>Ligasperre (Gelb- und Rotsperre zusammen). Setzen ersetzt sie durch eine Rotsperre dieser Länge (Altdaten).</summary>
    public int SuspendedMatches
    {
        get => Ban(LeagueScope);
        set => SetDiscipline(LeagueScope, Yellows(LeagueScope), 0, value);
    }

    /// <summary>Gelbe Karten der Liga in dieser Saison.</summary>
    public int YellowCardsAccumulated
    {
        get => Yellows(LeagueScope);
        set => SetDiscipline(LeagueScope, value, YellowBan(LeagueScope), RedBan(LeagueScope));
    }

    /// <summary>Sperre aus Ereignissen: gilt in jedem Wettbewerb und wird im nächsten Pflichtspiel abgesessen, das keine Wettbewerbssperre abbaut.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PersonalBan
    {
        get => _personalBan;
        set => _personalBan = Math.Max(0, value);
    }

    /// <summary>Zähler außerhalb der Liga (Pokal-Id, Europapokal-Id). Leer bleibt unsichtbar, damit Spieler ohne solche Karten ihr bisheriges Format behalten.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ScopeDiscipline>? Competitions
    {
        get
        {
            Dictionary<string, ScopeDiscipline>? copy = null;
            foreach (var (key, row) in _byScope.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (key == LeagueScope || row.IsEmpty) continue;
                copy ??= new Dictionary<string, ScopeDiscipline>();
                copy[key] = row;
            }
            return copy;
        }
        set
        {
            if (value is null) return;
            foreach (var (key, row) in value)
            {
                if (key == LeagueScope || row is null) continue;
                _byScope[key] = row;
            }
        }
    }

    private readonly Dictionary<string, ScopeDiscipline> _byScope = new(StringComparer.Ordinal);
    private int _personalBan;

    public int Ban(string scope) => YellowBan(scope) + RedBan(scope);
    public int Yellows(string scope) => Row(scope)?.YellowCards ?? 0;
    public int YellowBan(string scope) => Row(scope)?.YellowBanMatches ?? 0;
    public int RedBan(string scope) => Row(scope)?.RedBanMatches ?? 0;

    public bool AnySuspension => PersonalBan > 0 || _byScope.Values.Any(r => r.YellowBanMatches + r.RedBanMatches > 0);

    public List<string> ScopeKeys() => _byScope.Keys.ToList();

    public int AddYellow(string scope)
    {
        var row = Edit(scope);
        row.YellowCards++;
        return row.YellowCards;
    }

    public void SetYellows(string scope, int value) => SetDiscipline(scope, value, YellowBan(scope), RedBan(scope));

    public void AddYellowBan(string scope, int delta) => SetDiscipline(scope, Yellows(scope), YellowBan(scope) + delta, RedBan(scope));

    public void AddRedBan(string scope, int delta) => SetDiscipline(scope, Yellows(scope), YellowBan(scope), RedBan(scope) + delta);

    public void SetDiscipline(string scope, int yellows, int yellowBan, int redBan)
    {
        yellows = Math.Max(0, yellows);
        yellowBan = Math.Max(0, yellowBan);
        redBan = Math.Max(0, redBan);
        if (yellows == 0 && yellowBan == 0 && redBan == 0)
        {
            _byScope.Remove(scope);
            return;
        }
        var row = Edit(scope);
        row.YellowCards = yellows;
        row.YellowBanMatches = yellowBan;
        row.RedBanMatches = redBan;
    }

    private ScopeDiscipline? Row(string scope) => _byScope.TryGetValue(scope, out var row) ? row : null;

    private ScopeDiscipline Edit(string scope)
    {
        if (!_byScope.TryGetValue(scope, out var row)) _byScope[scope] = row = new ScopeDiscipline();
        return row;
    }

    /// <summary>Form 1–10 aus den letzten 5 Noten; ohne Noten neutral 5.</summary>
    public int Form
    {
        get
        {
            if (RecentGrades.Count == 0) return 5;
            var avg = RecentGrades.Average();
            // Note 1,0 => 10, Note 3,5 => 5, Note 6,0 => 1
            return (int)Math.Clamp(Math.Round(10 - (avg - 1.0) * 1.8), 1, 10);
        }
    }

    public void AddGrade(double grade)
    {
        RecentGrades.Add(grade);
        while (RecentGrades.Count > 5) RecentGrades.RemoveAt(0);
    }
}

/// <summary>Gelbe Karten und daraus folgende sowie rote Sperren in einem Wettbewerb.</summary>
public sealed class ScopeDiscipline
{
    public int YellowCards { get; set; }
    public int YellowBanMatches { get; set; }
    public int RedBanMatches { get; set; }
    [JsonIgnore]
    public bool IsEmpty => YellowCards == 0 && YellowBanMatches == 0 && RedBanMatches == 0;
}

public sealed class Injury
{
    /// <summary>Schlüssel der Verletzungsart (rules/injuries.json).</summary>
    public string Type { get; set; } = "";
    public int DaysRemaining { get; set; }
    public int TotalDays { get; set; }
}

public sealed class PlayerSeasonStats
{
    public int SeasonYear { get; set; }
    public int? ClubId { get; set; }
    public int Appearances { get; set; }
    public int Minutes { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int GradedMatches { get; set; }
    public double GradeSum { get; set; }

    public double? AverageGrade => GradedMatches == 0 ? null : GradeSum / GradedMatches;
}
