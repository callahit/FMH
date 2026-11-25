namespace Fmh.SceneTree;

/// <summary>
/// Kartengründe und Verletzungsarten. Die Originaltexte zu REASON und INJURY liegen nicht im Baum;
/// die Formulierungen sind die Zuordnung aus dem Katalog, die FMH-Schlüssel die bestehenden Verletzungen.
/// </summary>
public static class CardCatalog
{
    /// <summary>Passt in „Karte für OFFENDER REASON“, also mit „wegen …“.</summary>
    public static string Reason(byte code) => code switch
    {
        2 => "wegen eines groben Fouls",
        3 or 4 or 5 => "wegen eines Fouls",
        6 or 10 => "wegen Handspiels",
        7 => "wegen Haltens",
        9 => "wegen eines taktischen Fouls",
        25 => "wegen Nachtretens",
        26 => "wegen Stoßens",
        29 => "wegen eines Fouls am Torwart",
        30 or 33 => "wegen Meckerns",
        37 => "wegen einer Schwalbe",
        _ => "wegen Foulspiels",
    };

    public static bool Serious(byte code) => code is 2 or 25 or 26 or 29;

    public static bool Conduct(byte code) => code is 2 or 25 or 26 or 30 or 33 or 37;

    /// <summary>FMH-Schlüssel, typische Tage, Anzeigetext für INJURY.</summary>
    public static (string Key, int Days, string Label) Injury(byte code) => code switch
    {
        7 => ("bruise", 4, "Prellung"),
        8 => ("strain", 10, "Zerrung"),
        9 => ("concussion", 10, "Gehirnerschütterung"),
        13 => ("groin", 14, "Leistenprobleme"),
        14 => ("sprainedAnkle", 14, "Bänderdehnung"),
        16 => ("muscleTear", 28, "Muskelriss"),
        18 => ("hamstring", 21, "Muskelfaserriss"),
        22 => ("meniscus", 50, "Meniskusschaden"),
        23 => ("muscleTear", 24, "Muskelbündelriss"),
        30 => ("bruise", 5, "Prellung"),
        31 => ("strain", 8, "Zerrung"),
        32 => ("hamstring", 18, "Oberschenkelzerrung"),
        38 => ("ligamentTear", 60, "Bänderriss"),
        39 => ("acl", 200, "Kreuzbandriss"),
        _ => ("bruise", 6, "Verletzung"),
    };
}
