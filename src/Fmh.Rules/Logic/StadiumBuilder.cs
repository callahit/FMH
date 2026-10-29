using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Stadien erzeugen: Startstadion nach Vereinsgröße und Neubau nach Entwurf.</summary>
public static class StadiumBuilder
{
    /// <summary>Stadion aus der Kapazität ableiten: vier Tribünen, Stehplatzanteil nach Vereinsgröße.</summary>
    public static Stadium CreateStadium(Club club, Rng rng)
    {
        var cap = Math.Max(1_000, club.StadiumCapacity);
        var standingShare = Math.Clamp(0.55 - club.Reputation * 0.02, 0.15, 0.55);
        var shares = new[] { (StandSide.North, 0.22), (StandSide.South, 0.22), (StandSide.East, 0.28), (StandSide.West, 0.28) };
        var st = new Stadium
        {
            Floodlight = club.Reputation >= 9 ? 3 + (club.Reputation >= 15 ? 1 : 0) : club.Reputation >= 5 ? 2 : 1,
            UndersoilHeating = club.Reputation >= 11,
            Scoreboard = Math.Clamp(club.Reputation / 6, 0, 3),
            Pitch = 75 + club.Reputation,
        };
        foreach (var (side, share) in shares)
        {
            var c = (int)(cap * share);
            // Stehplätze vor allem in den Kurven (Nord/Süd).
            var standing = side is StandSide.North or StandSide.South ? (int)(c * Math.Min(0.95, standingShare * 1.8)) : (int)(c * standingShare * 0.2);
            st.Stands.Add(new Stand
            {
                Side = side, Standing = standing, Seating = c - standing,
                Roof = club.Reputation >= 8 || side == StandSide.West,
                Boxes = side == StandSide.West ? Math.Clamp((club.Reputation - 4) * 3, 0, 40) : side == StandSide.East ? Math.Clamp((club.Reputation - 12) * 2, 0, 20) : 0,
            });
        }
        club.StadiumCapacity = st.Capacity;
        return st;
    }

    public static Stadium BuildNewStadium(Club club, StadiumDesign d)
    {
        var standing = d.Capacity * Math.Clamp(d.StandingPercent, 0, 60) / 100;
        var seating = d.Capacity - standing;
        Stand Make(StandSide side, double standShare, double seatShare, double boxShare) => new()
        {
            Side = side, Standing = (int)(standing * standShare), Seating = (int)(seating * seatShare), Roof = d.Roof,
            Boxes = (int)Math.Min(40, Math.Round(d.Boxes * boxShare)),
        };
        // Kurven mit den Stehplätzen, Haupt- und Gegentribüne mit Sitzplätzen und Logen.
        return new Stadium
        {
            Stands = new List<Stand>
            {
                Make(StandSide.North, 0.5, 0.1, 0),
                Make(StandSide.South, 0.5, 0.1, 0),
                Make(StandSide.East, 0, 0.35, 0.4),
                Make(StandSide.West, 0, 0.45, 0.6),
            },
            Floodlight = 4, UndersoilHeating = true, Scoreboard = 3, Pitch = 100,
        };
    }
}
