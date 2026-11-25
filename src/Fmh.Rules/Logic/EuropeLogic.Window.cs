using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public static partial class EuropeLogic
{
    /// <summary>
    /// Fünfjahresfenster (Review I-1): schließt die vorige Saison ab und lässt Zugewinne verfallen, die älter als
    /// <see cref="EuropeAccess.WindowYears"/> sind. Danach gilt: Koeffizient = Grundwert + Zugewinne der letzten
    /// Fensterjahre minus eins plus die laufende Saison. Mehrfacher Aufruf in derselben Saison ändert nichts.
    /// </summary>
    public static void RollCoefficients(World world, EuropeFormat format)
    {
        var window = world.Coefficients;
        var closing = world.SeasonYear - 1;
        if (window.Seasons.Count > 0 && window.Seasons[^1].SeasonYear >= closing) return;
        // Ältere Spielstände ohne Grundwert: alles Bisherige zählt als Grundwert, das Fenster beginnt jetzt.
        if (window.CountryBase.Count == 0 && window.ClubBase.Count == 0)
        {
            window.CountryBase = new Dictionary<string, double>(world.CountryCoefficients);
            window.ClubBase = new Dictionary<int, double>(world.ClubCoefficients);
        }

        var season = new CoefficientSeason { SeasonYear = closing };
        foreach (var (code, total) in world.CountryCoefficients)
        {
            var gain = total - window.CountryBase.GetValueOrDefault(code) - window.Seasons.Sum(s => s.Countries.GetValueOrDefault(code));
            if (Math.Abs(gain) > 1e-9) season.Countries[code] = gain;
        }
        foreach (var (id, total) in world.ClubCoefficients)
        {
            var gain = total - window.ClubBase.GetValueOrDefault(id) - window.Seasons.Sum(s => s.Clubs.GetValueOrDefault(id));
            if (Math.Abs(gain) > 1e-9) season.Clubs[id] = gain;
        }
        window.Seasons.Add(season);
        var keep = Math.Max(0, format.Access.WindowYears - 1);
        if (window.Seasons.Count > keep) window.Seasons.RemoveRange(0, window.Seasons.Count - keep);

        foreach (var code in world.CountryCoefficients.Keys.ToList())
            world.CountryCoefficients[code] = window.CountryBase.GetValueOrDefault(code) + window.Seasons.Sum(s => s.Countries.GetValueOrDefault(code));
        foreach (var id in world.ClubCoefficients.Keys.ToList())
            world.ClubCoefficients[id] = window.ClubBase.GetValueOrDefault(id) + window.Seasons.Sum(s => s.Clubs.GetValueOrDefault(id));
    }
}
