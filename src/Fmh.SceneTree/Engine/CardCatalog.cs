namespace Fmh.SceneTree;

/// <summary>
/// Kartengründe und Verletzungsarten. Die Originaltexte zu REASON und INJURY liegen nicht im Baum;
/// die Formulierungen sind die Zuordnung aus dem Katalog, die FMH-Schlüssel die bestehenden Verletzungen.
/// </summary>
public static class CardCatalog
{
    public static string Reason(byte code) => code switch
    {
        2 => "grobes Foul",
        3 => "Foul",
        4 or 5 => "Foul",
        6 or 10 => "Handspiel",
        7 => "Halten",
        9 => "taktisches Foul",
        25 => "Nachtreten",
        26 => "Stoßen",
        29 => "Foul am Torwart",
        30 or 33 => "Meckern",
        37 => "Schwalbe",
        _ => "Foulspiel",
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
