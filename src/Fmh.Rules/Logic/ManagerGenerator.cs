using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// KI-Manager: jeder Ligaverein (ohne zweite Mannschaften) hat einen Trainer, dazu ein Pool vereinsloser Trainer.
/// Alles hier nutzt einen eigenen, aus dem Weltseed abgeleiteten Zufallsstrom. Der Zufall der Spielwelt (Spiele,
/// Transfers, Entwicklung) bleibt dadurch unberührt.
/// </summary>
public static class ManagerGenerator
{
    private const ulong Stream = 0x4B494D414E414745UL;

    private static readonly (ManagerStyle style, double weight)[] Styles =
    {
        (ManagerStyle.Balanced, 40), (ManagerStyle.Attacking, 18), (ManagerStyle.Defensive, 16), (ManagerStyle.Counter, 16), (ManagerStyle.Pressing, 10),
    };

    /// <summary>Zufallsstrom für einen Anlass (z. B. Saison und Verein), unabhängig vom Spielzufall und vom Spielstand.</summary>
    public static Rng RngFor(World world, long salt) => new(Rng.Mix(world.Seed ^ Stream, (ulong)salt));

    /// <summary>Braucht der Verein einen KI-Manager? Ligavereine ohne Trainer; zweite Mannschaften und Pools nicht.</summary>
    public static bool NeedsManager(World world, Club club) =>
        club.ManagerId is null && club.ReserveOf is null && world.FindLeague(club.LeagueId) is not null;

    /// <summary>Gewünschte Größe des Pools vereinsloser Trainer.</summary>
    public static int PoolTarget(World world) =>
        Math.Max(6, world.Clubs.Values.Count(c => c.ReserveOf is null && world.FindLeague(c.LeagueId) is not null) / 8);

    public static IEnumerable<Manager> FreeManagers(World world) => world.Managers.Where(m => !m.Human && m.ClubId is null);

    /// <summary>
    /// Fehlende KI-Manager ergänzen (neue Welt und ältere Spielstände) und den Pool auffüllen. Wiederholbar: Vereine mit
    /// Trainer bleiben unverändert.
    /// </summary>
    public static void Ensure(World world, GameData data)
    {
        var rng = RngFor(world, world.SeasonYear * 7919L + world.Managers.Count);
        foreach (var club in world.Clubs.Values.Where(c => NeedsManager(world, c)).OrderBy(c => c.Id).ToList())
        {
            var m = Create(world, data, rng, club.CountryCode, club.Reputation);
            Assign(m, club, new DateOnly(world.SeasonYear - rng.Next(0, 3), 7, 1));
        }
        TopUpPool(world, data, rng);
    }

    /// <summary>Pool auf die Zielgröße bringen; neue Trainer passen nach Ruf und Herkunft zu zufälligen Ligavereinen.</summary>
    public static void TopUpPool(World world, GameData data, Rng rng)
    {
        var clubs = world.Clubs.Values.Where(c => c.ReserveOf is null && world.FindLeague(c.LeagueId) is not null).OrderBy(c => c.Id).ToList();
        if (clubs.Count == 0) return;
        for (var free = FreeManagers(world).Count(); free < PoolTarget(world); free++)
        {
            var like = clubs[rng.Next(clubs.Count)];
            Create(world, data, rng, like.CountryCode, Math.Max(1, like.Reputation - 2));
        }
    }

    /// <summary>Neuer KI-Manager (ohne Verein) mit Ruf um <paramref name="reputation"/> und zufälligem Stil.</summary>
    public static Manager Create(World world, GameData data, Rng rng, string country, int reputation)
    {
        var names = data.Names.TryGetValue(country, out var n) ? n : data.Names.Values.First();
        var rep = (int)Math.Clamp(Math.Round(reputation + rng.Normal(0, 1.5)), 1, 20);
        var m = new Manager
        {
            Id = world.Managers.Count == 0 ? 1 : world.Managers.Max(x => x.Id) + 1,
            Name = $"{rng.Pick(names.FirstNames)} {rng.Pick(names.LastNames)}",
            Reputation = rep,
            Traits = new ManagerTraits
            {
                Style = Styles[rng.WeightedIndex(Styles.Select(s => s.weight).ToList())].style,
                Tactics = (int)Math.Clamp(Math.Round(rep * 0.75 + 3 + rng.Normal(0, 2)), 1, 20),
                Motivation = (int)Math.Clamp(Math.Round(rng.Normal(11, 3.5)), 1, 20),
                BirthYear = world.SeasonYear - rng.Next(36, 65),
                Nationality = country,
            },
        };
        world.Managers.Add(m);
        return m;
    }

    /// <summary>Manager übernimmt den Verein: neue Station ab <paramref name="from"/>.</summary>
    public static void Assign(Manager m, Club club, DateOnly from)
    {
        m.ClubId = club.Id;
        club.ManagerId = m.Id;
        m.Career.Stations.Add(new CareerStation { ClubId = club.Id, ClubName = club.Name, From = from });
    }

    /// <summary>Station des Managers beim Verein beenden (Entlassung, Wechsel); der Manager ist danach vereinslos.</summary>
    public static void Release(Manager m, Club club, DateOnly date, string exit)
    {
        if (m.CurrentStation is { } st) { st.To = date; st.Exit = exit; }
        if (club.ManagerId == m.Id) club.ManagerId = null;
        m.ClubId = null;
    }

    /// <summary>KI-Manager eines Vereins (null beim menschlichen Manager oder ohne Trainer).</summary>
    public static Manager? AiManagerOf(World world, Club club) =>
        club.ManagerId is { } id && world.Managers.FirstOrDefault(m => m.Id == id) is { Human: false } m ? m : null;
}
