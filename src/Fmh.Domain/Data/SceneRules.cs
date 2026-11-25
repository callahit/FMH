using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>
/// Textmodus (Szenenkatalog): Zu jedem Ereignis der Matchengine erzählt eine Szene den Spielzug in mehreren Zeilen.
/// Der Ablauf steht hier (sprachunabhängig), die Texte stehen je Sprache in <c>lang/&lt;code&gt;.scenes.json</c>.
/// Das Ergebnis entscheidet immer die Engine; der Katalog ändert keine Statistik.
/// </summary>
public sealed class SceneRules
{
    /// <summary>Anteil der einfachen Angriffe, die eine eigene Szene bekommen (sonst wird der Text zu dicht).</summary>
    public double AttackShare { get; set; } = 0.55;
    public List<SceneDef> Scenes { get; set; } = new();
    /// <summary>Seltene Zwischenszenen ohne Wirkung auf das Spiel (Wetter, Flutlicht, Publikum).</summary>
    public List<RareSceneDef> Rare { get; set; } = new();
}

/// <summary>Eine Szene: passende Ereignisse, Bedingungen und die Zeilenfolge.</summary>
public sealed class SceneDef
{
    public string Id { get; set; } = "";
    /// <summary>Ereignisschlüssel wie im Kommentar, z. B. <c>goal.header</c> oder <c>shot.normal.saved</c>.</summary>
    public List<string> On { get; set; } = new();
    public SceneCondition? When { get; set; }
    /// <summary>Relatives Gewicht unter allen passenden Szenen.</summary>
    public double Weight { get; set; } = 1;
    public List<SceneStep> Steps { get; set; } = new();
}

/// <summary>Eine Zeile der Szene: Textschlüssel (Varianten je Sprache) und optional eine Bedingung oder Wahrscheinlichkeit.</summary>
public sealed class SceneStep
{
    public string Text { get; set; } = "";
    /// <summary>Wahrscheinlichkeit, dass die Zeile erscheint (1 = immer).</summary>
    public double Chance { get; set; } = 1;
    public SceneCondition? When { get; set; }
    /// <summary>
    /// Zeile aus Sicht der vorigen Aktion des Gegners (Spieler, Mannschaft, Seite): erzählt deren Ausgang, der erst jetzt
    /// feststeht, etwa „Die Flanke von {p} ist zu lang“ zu Beginn des Gegenangriffs. Braucht <c>afterOpponent</c>.
    /// </summary>
    public bool Previous { get; set; }
}

/// <summary>Bedingungen; nicht gesetzte Felder gelten immer.</summary>
public sealed class SceneCondition
{
    /// <summary>Vorheriges Ereignis derselben Mannschaft in derselben Minute, z. B. <c>cross</c> oder <c>counter</c>.</summary>
    public List<string>? After { get; set; }
    /// <summary>Vorige Aktion des Gegners in derselben oder der Minute davor, z. B. <c>cross</c>, <c>corner</c> oder <c>attack</c>.</summary>
    public List<string>? AfterOpponent { get; set; }
    public int? MinMinute { get; set; }
    public int? MaxMinute { get; set; }
    /// <summary>Spielstand aus Sicht der handelnden Mannschaft vor dem Ereignis.</summary>
    public SceneScore? Score { get; set; }
    /// <summary>Tordifferenz aus Sicht der handelnden Mannschaft vor dem Ereignis (z. B. −2 = zwei Tore zurück).</summary>
    public int? MinMargin { get; set; }
    public int? MaxMargin { get; set; }
    /// <summary>Wer gerade führt, unabhängig von der Seite des Ereignisses (Anpfiff, Halbzeit, Abpfiff).</summary>
    public SceneStanding? Standing { get; set; }
    public List<Weather>? Weather { get; set; }
    public bool? Floodlit { get; set; }
    public bool? Knockout { get; set; }
    public double? MinXg { get; set; }
    public double? MaxXg { get; set; }
    /// <summary>Spielfeldreihe aus Angriffssicht (0 = eigenes Tor, 6 = gegnerischer Strafraum).</summary>
    public int? MinRow { get; set; }
    public int? MaxRow { get; set; }
    /// <summary>Attributgrenzen des handelnden Spielers.</summary>
    public List<SceneAttr>? Attrs { get; set; }
    /// <summary>Attributgrenzen des gegnerischen Torwarts.</summary>
    public List<SceneAttr>? KeeperAttrs { get; set; }
}

public enum SceneScore { Level, Leading, Trailing }

public enum SceneStanding { Level, Home, Away }

public sealed class SceneAttr
{
    public Attr Attr { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
}

/// <summary>Seltene Zwischenszene: erscheint mit kleiner Wahrscheinlichkeit je Spielminute, wenn die Bedingung passt.</summary>
public sealed class RareSceneDef
{
    public string Id { get; set; } = "";
    /// <summary>Wahrscheinlichkeit je Spiel (verteilt auf die Spielzeit).</summary>
    public double Chance { get; set; } = 0.05;
    public SceneCondition? When { get; set; }
    public List<SceneStep> Steps { get; set; } = new();
}
