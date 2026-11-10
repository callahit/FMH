using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Nationalmannschaften (AP 8): Teams je Land, Nominierung durch die KI nach Stärke und Form, erzeugte Kader der
/// Hintergrundländer. Alles nutzt eigene Zufallsströme; der Spielzufall bleibt unberührt.
/// </summary>
public static class NationalSquads
{
    private const ulong Stream = 0x4E4154494F4E414CUL;

    /// <summary>Teams anlegen: je Land mit Vereinen der Welt eines, dazu die Hintergrundländer aus den Daten.</summary>
    public static void Ensure(World world, GameData data)
    {
        foreach (var country in world.Countries.GroupBy(c => c.Code).Select(g => g.First()).OrderBy(c => c.Code, StringComparer.Ordinal))
            if (world.NationalTeams.All(t => t.Code != country.Code) && world.Clubs.Values.Any(c => c.CountryCode == country.Code))
                world.NationalTeams.Add(new NationalTeam { Code = country.Code, Name = country.Name });
        foreach (var bg in data.Internationals.BackgroundNations)
            if (world.NationalTeams.All(t => t.Code != bg.Code))
                world.NationalTeams.Add(new NationalTeam { Code = bg.Code, Name = bg.Name, Background = true, Strength = bg.Strength });
        // Feste Reihenfolge wie im Spielstand
        world.NationalTeams.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
    }

    public static NationalTeam? TeamOf(World world, string code) => world.NationalTeams.FirstOrDefault(t => t.Code == code);

    /// <summary>Spielstärke für die Nominierung: Positionsstärke plus Form (Aufschlag je Formpunkt über 5).</summary>
    public static double Score(Player p, GameData data) =>
        Ratings.PositionRating(p, p.MainPosition, data.PositionProfiles) + (p.State.Form - 5) * data.Internationals.FormWeight;

    /// <summary>Wer für <paramref name="code"/> spielen darf: Nationalität, Alter, gesund, bei einem Verein, nicht für ein anderes Land festgespielt.</summary>
    public static bool Eligible(Player p, string code, World world, GameData data) =>
        !p.Retired && !p.IsYouth && p.ClubId is not null && p.Nationalities.Contains(code)
        && p.Age(world.Date) >= data.Internationals.MinAge && p.State.Injury is null && p.State.SuspendedMatches == 0
        && (!world.Caps.TryGetValue(p.Id, out var career) || career.Nation == "" || career.Nation == code);

    /// <summary>
    /// Kader nominieren: je Position die Besten nach der Kaderschablone, übrige Plätze nach Stärke. Gleiche Stärke
    /// entscheidet die Spieler-Id, damit die Auswahl reproduzierbar bleibt.
    /// </summary>
    public static List<int> Nominate(World world, GameData data, NationalTeam team, IReadOnlySet<int>? taken = null)
    {
        var size = data.Internationals.SquadSize;
        // Doppelte Staatsbürger: Wer schon für ein anderes Land nominiert ist, steht nicht zur Verfügung.
        var pool = world.Players.Values.Where(p => taken?.Contains(p.Id) != true && Eligible(p, team.Code, world, data))
            .Select(p => (p, score: Score(p, data))).OrderByDescending(x => x.score).ThenBy(x => x.p.Id).ToList();
        var chosen = new List<int>();
        foreach (var (pos, count) in PlayerGenerator.Template(size))
            chosen.AddRange(pool.Where(x => x.p.MainPosition == pos && !chosen.Contains(x.p.Id)).Take(count).Select(x => x.p.Id));
        foreach (var (p, _) in pool)
        {
            if (chosen.Count >= size) break;
            if (!chosen.Contains(p.Id)) chosen.Add(p.Id);
        }
        return chosen.Take(size).ToList();
    }

    /// <summary>Kader berufen: Die Spieler fehlen ab jetzt ihrem Verein.</summary>
    public static void Call(World world, NationalTeam team, List<int> squad)
    {
        team.Squad = squad;
        foreach (var id in squad)
            if (world.Players.TryGetValue(id, out var p)) p.State.OnNationalDuty = true;
    }

    /// <summary>Kader entlassen: Die Spieler kehren mit Reisebelastung zum Verein zurück.</summary>
    public static void Release(World world, GameData data, NationalTeam team)
    {
        foreach (var id in team.Squad)
            if (world.Players.TryGetValue(id, out var p))
            {
                p.State.OnNationalDuty = false;
                p.State.Condition = Math.Max(0, Math.Round(p.State.Condition - data.Internationals.TravelFatigue, 1));
            }
        team.Squad.Clear();
    }

    /// <summary>
    /// Kader eines Hintergrundlandes für ein Spiel: erzeugte Spieler mit negativen Ids, die nicht in die Welt gelangen.
    /// Je Saison und Land gleich (eigener Zufallsstrom).
    /// </summary>
    public static List<Player> BackgroundSquad(World world, GameData data, NationalTeam team)
    {
        var rng = new Rng(Rng.Mix(world.Seed ^ Stream, (ulong)(world.SeasonYear * 1000L + team.Code.Aggregate(17L, (h, ch) => h * 31 + ch))));
        var nextId = world.NextPlayerId;
        var squad = new List<Player>();
        var index = 0;
        foreach (var (pos, count) in PlayerGenerator.Template(data.Internationals.SquadSize))
            for (var i = 0; i < count; i++)
            {
                var p = PlayerGenerator.Create(world, data, rng, pos, team.Strength + rng.Normal(0, 1), rng.Next(21, 33), team.Code);
                p.Id = -(team.Code.Aggregate(1, (h, ch) => h * 7 + ch % 97) % 10_000 * 100 + ++index);
                p.ClubId = null;
                p.Contract = null;
                squad.Add(p);
            }
        // Die erzeugten Spieler gehören nicht zur Welt: Id-Zähler zurücksetzen.
        world.NextPlayerId = nextId;
        return squad;
    }
}
