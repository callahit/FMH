using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Erzeugt fiktive Spieler aus Namenslisten und Positionsprofilen.</summary>
public static class PlayerGenerator
{
    /// <summary>Kaderschablone für einen Profikader (25 Spieler).</summary>
    public static readonly (Position pos, int count)[] SquadTemplate =
    {
        (Position.GK, 3), (Position.DL, 2), (Position.DC, 4), (Position.DR, 2),
        (Position.DM, 2), (Position.MC, 3), (Position.ML, 1), (Position.MR, 1),
        (Position.AMC, 2), (Position.AML, 1), (Position.AMR, 1), (Position.ST, 3),
    };

    /// <summary>Zusätzliche Plätze für größere Kader, in dieser Reihenfolge.</summary>
    private static readonly Position[] Extras = { Position.DC, Position.MC, Position.ST, Position.DM, Position.DL, Position.DR, Position.AMC, Position.GK };

    /// <summary>Kaderschablone für <paramref name="size"/> Spieler: Grundschablone (25) plus Extras bzw. gekürzt am Ende.</summary>
    public static (Position pos, int count)[] Template(int size)
    {
        var counts = SquadTemplate.ToDictionary(t => t.pos, t => t.count);
        for (var i = 0; i < size - 25; i++) counts[Extras[i % Extras.Length]]++;
        for (var i = 0; i < 25 - size; i++) counts[Extras[i % Extras.Length]] = Math.Max(1, counts[Extras[i % Extras.Length]] - 1);
        return SquadTemplate.Select(t => (t.pos, counts[t.pos])).ToArray();
    }

    /// <summary>Durchschnittliche Positionsstärke (1–20) eines Kaders je Vereinsreputation.</summary>
    public static double ClubLevel(int reputation) => 6.6 + reputation * 0.4;

    /// <summary>Wochengehalt in Vereinswährung, abhängig von der Spielstärke (Profimarkt, siehe <see cref="MarketValue.Wage"/>).</summary>
    public static int WeeklyWage(double level, World world, Rng rng) => (int)(Math.Round(MarketValue.Wage(level, world, null) * rng.Range(0.8, 1.25) / 100) * 100);

    public static Player Create(World world, GameData data, Rng rng, Position pos, double level, int age, string country)
    {
        var names = data.Names.TryGetValue(country, out var n) ? n : data.Names.Values.First();
        var p = new Player
        {
            Id = world.NextPlayerId++,
            FirstName = rng.Pick(names.FirstNames),
            LastName = rng.Pick(names.LastNames),
            BirthDate = new DateOnly(world.Date.Year - age, 1, 1).AddDays(-rng.Next(0, 364)),
            Nationalities = new List<string> { country },
            MainPosition = pos,
        };
        // Geburtstag so legen, dass das Alter am Stichtag stimmt.
        while (p.Age(world.Date) > age) p.BirthDate = p.BirthDate.AddYears(1);
        while (p.Age(world.Date) < age) p.BirthDate = p.BirthDate.AddYears(-1);

        GenerateAttributes(p, data.PositionProfiles, rng, pos, level, age);
        GeneratePositions(p, rng);
        GenerateBody(p, rng, pos);
        GenerateHidden(p, data.PositionProfiles, rng, age);

        p.Reputation = (int)Math.Clamp(Math.Round(level - 3 + rng.Normal(0, 1.5)), 1, 20);
        p.Contract = new Contract
        {
            WeeklyWage = WeeklyWage(level, world, rng),
            Until = new DateOnly(world.SeasonYear + rng.Next(1, 4), 6, 30),
        };
        p.State.Condition = rng.Range(92, 100);
        p.State.MatchPractice = rng.Range(55, 80);
        p.State.Morale = (Morale)rng.Next(3, 4);
        return p;
    }

    private static void GenerateAttributes(Player p, PositionProfiles profiles, Rng rng, Position pos, double level, int age)
    {
        var weights = profiles.Weights.TryGetValue(pos, out var w) ? w : new Dictionary<Attr, double>();
        var maxW = weights.Count == 0 ? 1 : weights.Values.Max();
        var isGk = pos == Position.GK;

        foreach (var a in AttrInfo.All)
        {
            var group = AttrInfo.GroupOf(a);
            double value;
            if (group == AttrGroup.Goalkeeping && !isGk)
                value = rng.Normal(2.5, 1.2);
            else
            {
                var wn = weights.TryGetValue(a, out var wa) ? wa / maxW : 0;
                var bias = -3.0 + 4.5 * wn;
                if (isGk && group == AttrGroup.Technical && a is not (Attr.Kicking or Attr.FirstTouch or Attr.Passing))
                    bias -= 3;
                if (group == AttrGroup.Physical)
                {
                    // Junge Spieler sind noch nicht ausgewachsen, ältere verlieren Tempo.
                    if (age < 20) bias -= (20 - age) * 0.6;
                    if (age > 29 && a is Attr.Pace or Attr.Acceleration or Attr.Agility) bias -= (age - 29) * 0.7;
                }
                if (group == AttrGroup.Mental && age > 26) bias += Math.Min(2, (age - 26) * 0.3);
                value = level + bias + rng.Normal(0, 1.8);
            }
            p.Attributes[a] = (int)Math.Round(value);
        }

        // Feinjustierung: Positionsstärke auf das gewünschte Niveau ziehen.
        for (var iter = 0; iter < 4; iter++)
        {
            var diff = level - Ratings.PositionRating(p, pos, profiles);
            if (Math.Abs(diff) < 0.3) break;
            foreach (var a in weights.Keys)
                p.Attributes[a] = (int)Math.Round(p.Attributes[a] + diff);
        }
    }

    private static void GeneratePositions(Player p, Rng rng)
    {
        p.Positions[p.MainPosition] = Suitability.Natural;
        foreach (var nb in PositionInfo.Neighbours(p.MainPosition))
        {
            var r = rng.NextDouble();
            if (r < 0.35) p.Positions[nb] = Suitability.Good;
            else if (r < 0.7) p.Positions[nb] = Suitability.Adequate;
        }
    }

    private static void GenerateBody(Player p, Rng rng, Position pos)
    {
        p.Height = pos switch
        {
            Position.GK => rng.Next(184, 199),
            Position.DC => rng.Next(181, 196),
            Position.ST => rng.Next(174, 196),
            _ => rng.Next(167, 189),
        };
        p.Weight = p.Height - 100 + rng.Next(-8, 8);

        var leftSided = pos is Position.DL or Position.ML or Position.AML;
        var r = rng.NextDouble();
        var strong = rng.Next(15, 20);
        var weak = rng.Next(3, 11);
        if (r < 0.05) { p.LeftFoot = strong; p.RightFoot = strong - rng.Next(0, 3); }
        else if (r < (leftSided ? 0.65 : 0.22)) { p.LeftFoot = strong; p.RightFoot = weak; }
        else { p.RightFoot = strong; p.LeftFoot = weak; }
    }

    private static void GenerateHidden(Player p, PositionProfiles profiles, Rng rng, int age)
    {
        int Personality() => (int)Math.Clamp(Math.Round(rng.Normal(11, 4)), 1, 20);
        var h = p.Hidden;
        h.Ambition = Personality();
        h.Professionalism = Personality();
        h.Loyalty = Personality();
        h.Pressure = Personality();
        h.Temperament = Personality();
        h.Sportsmanship = Personality();
        h.Adaptability = Personality();
        h.Controversy = Personality();
        h.Consistency = Personality();
        h.InjuryProneness = Personality();
        h.ImportantMatches = Personality();

        h.CurrentAbility = Ratings.CurrentAbility(p, profiles);
        // Im Mittel holt ein junger Spieler nur den Altersabschlag auf (WorldGenerator.AgeLevelOffset, 1 Stärkepunkt ≈ 10,5 CA);
        // die Streuung ergibt Talente und Stagnierer. Mehr Spielraum ließe jede Generation stärker werden (Review F-02).
        var headroom = age switch
        {
            <= 18 => rng.Next(10, 45),
            <= 20 => rng.Next(5, 30),
            <= 22 => rng.Next(2, 14),
            <= 24 => rng.Next(0, 8),
            <= 27 => rng.Next(0, 4),
            _ => 0,
        };
        h.PotentialAbility = Math.Clamp(h.CurrentAbility + headroom, h.CurrentAbility, 200);
    }

    /// <summary>Alter für einen Kaderplatz: wenige ganz junge und ganz alte Spieler.</summary>
    public static int RandomAge(Rng rng)
    {
        var age = (int)Math.Round(rng.Normal(25.5, 4.2));
        return Math.Clamp(age, 17, 36);
    }
}
