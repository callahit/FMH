using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Anstoß-Schalter (AP 13): Hauptschalter und Einzelschalter je Manager.</summary>
public static class Anstoss
{
    public const string Prefix = "anstoss.";

    /// <summary>Element aus einem Kennzeichen (<c>anstoss.satire</c> → <c>satire</c>).</summary>
    public static string Element(string feature) => feature.StartsWith(Prefix, StringComparison.Ordinal) ? feature[Prefix.Length..] : feature;

    public static AnstossSettings Settings(World w, int managerId) => w.Anstoss.TryGetValue(managerId, out var s) ? s : new AnstossSettings();

    public static bool MasterOn(World w, int managerId) => Settings(w, managerId).Enabled;

    /// <summary>Inhalt mit diesem Kennzeichen erlaubt? Ohne Kennzeichen immer.</summary>
    public static bool IsOn(World w, int managerId, string? feature)
    {
        if (string.IsNullOrEmpty(feature)) return true;
        var s = Settings(w, managerId);
        return s.Enabled && !s.Disabled.Contains(Element(feature));
    }

    /// <summary>Hauptschalter setzen; ein Eintrag entsteht erst bei einer Abweichung von der Voreinstellung.</summary>
    public static void SetMaster(World w, int managerId, bool on) => Update(w, managerId, s => s.Enabled = on);

    public static void SetElement(World w, int managerId, string element, bool on) => Update(w, managerId, s =>
    {
        s.Disabled.Remove(element);
        if (!on) s.Disabled.Add(element);
        s.Disabled.Sort(StringComparer.Ordinal);
    });

    private static void Update(World w, int managerId, Action<AnstossSettings> change)
    {
        var s = Settings(w, managerId);
        change(s);
        if (s.Enabled && s.Disabled.Count == 0) w.Anstoss.Remove(managerId);
        else w.Anstoss[managerId] = s;
    }
}
