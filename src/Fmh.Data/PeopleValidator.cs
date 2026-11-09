using Fmh.Core.Model;

namespace Fmh.Core.Data;

/// <summary>
/// Prüft vorgegebene Personen (Spieler, Personal, Manager, Schiedsrichter) auf Bezüge und Eindeutigkeit.
/// Wertebereiche einzelner Felder prüft das JSON-Schema (<see cref="SchemaCatalog"/>).
/// </summary>
public static class PeopleValidator
{
    public static IEnumerable<string> Validate(GameData data)
    {
        var clubs = data.Clubs.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        bool UnknownClub(string key) => key.Length > 0 && !clubs.Contains(key);

        foreach (var dup in data.Players.GroupBy(p => p.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
            yield return $"Spielerschlüssel '{dup.Key}' ist mehrfach vergeben.";
        foreach (var p in data.Players)
        {
            var who = $"Spieler '{(p.Key.Length > 0 ? p.Key : p.LastName)}'";
            if (string.IsNullOrWhiteSpace(p.Key)) yield return $"{who}: Schlüssel fehlt.";
            if (string.IsNullOrWhiteSpace(p.FirstName) || string.IsNullOrWhiteSpace(p.LastName)) yield return $"{who}: Vor- und Nachname sind Pflicht.";
            if (UnknownClub(p.Club)) yield return $"{who}: unbekannter Verein '{p.Club}'.";
            foreach (var (attr, value) in p.Attributes.Where(a => a.Value is < AttrInfo.Min or > AttrInfo.Max))
                yield return $"{who}: Attribut {attr} = {value} liegt nicht zwischen {AttrInfo.Min} und {AttrInfo.Max}.";
            if (p.Positions.TryGetValue(p.Position, out var s) && s != Suitability.Natural)
                yield return $"{who}: Hauptposition {p.Position} muss „Natural“ sein.";
            if (p.BirthDate is not null && p.Age is not null) yield return $"{who}: entweder Geburtsdatum oder Alter angeben.";
        }

        foreach (var s in data.Staff)
        {
            var who = $"Personal '{s.Name}'";
            if (string.IsNullOrWhiteSpace(s.Name)) yield return "Personal: Name fehlt.";
            if (UnknownClub(s.Club)) yield return $"{who}: unbekannter Verein '{s.Club}'.";
            if (s.AgeGroup is not null && s.Role != StaffRole.YouthCoach) yield return $"{who}: Altersstufe gibt es nur für Jugendtrainer.";
        }

        foreach (var m in data.Managers)
        {
            if (string.IsNullOrWhiteSpace(m.Name)) yield return "Manager: Name fehlt.";
            if (UnknownClub(m.Club)) yield return $"Manager '{m.Name}': unbekannter Verein '{m.Club}'.";
        }
        foreach (var dup in data.Managers.Where(m => m.Club.Length > 0).GroupBy(m => m.Club, StringComparer.Ordinal).Where(g => g.Count() > 1))
            yield return $"Verein '{dup.Key}' hat mehr als einen Manager.";

        var countries = data.Countries.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var r in data.Referees)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) yield return "Schiedsrichter: Name fehlt.";
            if (r.Country.Length > 0 && !countries.Contains(r.Country)) yield return $"Schiedsrichter '{r.Name}': unbekanntes Land '{r.Country}'.";
        }
    }
}
