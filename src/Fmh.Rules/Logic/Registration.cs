using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Meldelisten (Spielberechtigungsliste, Squad List): Wer ohne Meldung oder Ausnahme ist, darf in Liga und Pokal nicht spielen.
/// Local Player bzw. Homegrown ist, wer zwischen 15 und 21 mindestens drei Spielzeiten bei Vereinen des Landes war.
/// </summary>
public static class Registration
{
    /// <summary>Schlüssel für Ausbildungsjahre im Ausland.</summary>
    public const int Abroad = -1;
    /// <summary>Spielzeiten, ab denen ein Spieler als ausgebildet gilt.</summary>
    public const int TrainedSeasons = 3;

    /// <summary>Regeln der Spielklasse des Vereins (Liga bzw. Division); null = keine Meldepflicht.</summary>
    public static RegistrationRules? RulesFor(World world, Club club)
    {
        var all = world.Country(club.CountryCode).Registration;
        if (all.TryGetValue(club.LeagueId, out var r)) return r;
        return world.FindLeague(club.LeagueId) is { } l && all.TryGetValue(l.Format.Id, out r) ? r : null;
    }

    /// <summary>Alter, das der Spieler im Startjahr der Saison erreicht (Stichtag 1. Januar, wie bei U21/U23-Regeln).</summary>
    public static int SeasonAge(Player p, World world) => world.SeasonYear - p.BirthDate.Year;

    public static bool IsExempt(Player p, World world, RegistrationRules r) =>
        r.ExemptAge > 0 && SeasonAge(p, world) <= r.ExemptAge && !(r.LoansNeedSlot && p.Loan is not null);

    public static bool IsClubTrained(Player p, int clubId) => p.Registration.Formative.GetValueOrDefault(clubId) >= TrainedSeasons;

    public static bool IsLocal(Player p, World world, string country) =>
        p.Registration.Formative.Where(kv => world.Clubs.TryGetValue(kv.Key, out var c) && c.CountryCode == country).Sum(kv => kv.Value) >= TrainedSeasons;

    public static bool IsNational(Player p, string country) => p.Nationalities.Contains(country);

    /// <summary>Darf der Spieler für seinen Verein in Liga und Pokal spielen (gemeldet, Ausnahme oder keine Meldepflicht)?</summary>
    public static bool IsEligible(Player p, World world)
    {
        if (p.ClubId is not { } id || !world.Clubs.TryGetValue(id, out var club) || RulesFor(world, club) is not { } r) return true;
        return p.Registration.ClubId == id || IsExempt(p, world, r);
    }

    /// <summary>Plätze der Meldeliste (ohne Ausnahmen).</summary>
    public static int Capacity(World world, Club club, RegistrationRules r) =>
        r.MaxPlayers > 0 ? r.MaxPlayers : world.Country(club.CountryCode).Squad.MaxSquad;

    /// <summary>
    /// Füllt die Meldeliste nach Spielstärke: Plätze für Spieler ohne Ausbildung im Land, ohne Ausbildung im Verein und ohne
    /// Staatsangehörigkeit sind so begrenzt, dass die Mindestzahlen erreichbar bleiben; fehlen solche Spieler, bleiben Plätze
    /// frei. Mit <paramref name="keep"/> bleiben bisherige Meldungen stehen (Mensch, Nachmeldungen im Fenster).
    /// </summary>
    public static void Fill(World world, Club club, GameData data, bool keep)
    {
        var squad = world.SquadOf(club).ToList();
        if (RulesFor(world, club) is not { } r)
        {
            foreach (var p in squad) p.Registration.ClubId = club.Id;
            return;
        }
        var cap = Capacity(world, club, r);
        var country = club.CountryCode;
        var listed = new List<Player>();
        bool Fits(Player p) =>
            listed.Count < cap
            && (IsLocal(p, world, country) || listed.Count(x => !IsLocal(x, world, country)) < cap - r.MinLocal)
            && (IsClubTrained(p, club.Id) || listed.Count(x => !IsClubTrained(x, club.Id)) < cap - r.MinClubTrained)
            && (IsNational(p, country) || listed.Count(x => !IsNational(x, country)) < cap - r.MinNationals);
        var candidates = squad.Where(p => !IsExempt(p, world, r)).ToList();
        if (keep)
            foreach (var p in candidates.Where(p => p.Registration.ClubId == club.Id).ToList())
                if (Fits(p)) listed.Add(p);
        foreach (var p in candidates.Where(p => !listed.Contains(p))
                     .OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, data.PositionProfiles)).ThenBy(p => p.Id))
            if (Fits(p)) listed.Add(p);
        foreach (var p in candidates) p.Registration.ClubId = listed.Contains(p) ? club.Id : null;
        foreach (var p in squad.Where(p => IsExempt(p, world, r))) p.Registration.ClubId = club.Id;
    }

    /// <summary>Melden bzw. abmelden durch den Manager; Textschlüssel eines Fehlers oder null.</summary>
    public static string? Set(World world, Club club, Player p, bool registered)
    {
        if (p.ClubId != club.Id) return "registration.error.notOwn";
        if (RulesFor(world, club) is not { } r || IsExempt(p, world, r)) return "registration.error.exempt";
        if (!registered) { p.Registration.ClubId = null; return null; }
        var listed = world.SquadOf(club).Where(x => x != p && !IsExempt(x, world, r) && x.Registration.ClubId == club.Id).ToList();
        var cap = Capacity(world, club, r);
        var country = club.CountryCode;
        if (listed.Count >= cap) return "registration.error.full";
        if (!IsLocal(p, world, country) && listed.Count(x => !IsLocal(x, world, country)) >= cap - r.MinLocal) return "registration.error.local";
        if (!IsClubTrained(p, club.Id) && listed.Count(x => !IsClubTrained(x, club.Id)) >= cap - r.MinClubTrained) return "registration.error.clubTrained";
        if (!IsNational(p, country) && listed.Count(x => !IsNational(x, country)) >= cap - r.MinNationals) return "registration.error.national";
        p.Registration.ClubId = club.Id;
        return null;
    }

    /// <summary>
    /// Ausbildungsjahre eines neu erzeugten Spielers: je Spielzeit zwischen 15 und 21 beim Verein (Anteil aus den Daten), bei
    /// einem anderen Verein des Landes oder im Ausland; Jugendspieler stets beim eigenen Verein.
    /// </summary>
    public static void InitFormative(Player p, World world, Club? club, Rng rng, bool youth = false)
    {
        var seasons = Math.Clamp(Math.Min(SeasonAge(p, world), 21) - 14, 0, 7);
        p.Registration.Formative.Clear();
        if (seasons == 0) return;
        var home = p.Nationalities.FirstOrDefault() ?? club?.CountryCode ?? "";
        var shares = world.Countries.FirstOrDefault(c => c.Code == home)?.Formative ?? new FormativeShares();
        var u = rng.NextDouble();
        int key;
        if (youth && club is not null) key = club.Id;
        else if (club is not null && u < shares.Club) key = club.Id;
        else if (u < shares.Club + shares.Country && world.Clubs.Values.Where(c => c.CountryCode == home).OrderBy(c => c.Id).ToList() is { Count: > 0 } clubs)
            key = clubs[rng.Next(clubs.Count)].Id;
        else key = Abroad;
        p.Registration.Formative[key] = seasons;
    }

    /// <summary>Saisonstart: Spieler zwischen 15 und 21 sammeln eine Ausbildungsspielzeit bei ihrem Verein.</summary>
    public static void CountSeason(Player p, World world)
    {
        if (p.ClubId is { } id && SeasonAge(p, world) is >= 15 and <= 21)
            p.Registration.Formative[id] = p.Registration.Formative.GetValueOrDefault(id) + 1;
    }
}
