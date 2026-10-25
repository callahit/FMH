using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fmh.Core.Model;

/// <summary>Sichtbare Spielerattribute auf der Skala 1–20 (Kapitel 4).</summary>
public enum Attr
{
    // Technik
    FirstTouch, Dribbling, Crossing, Heading, Passing, Technique, Finishing, LongShots,
    FreeKicks, Penalties, Corners, LongThrows, Tackling, Marking,
    // Mental
    Anticipation, Decisions, Concentration, Determination, Bravery, Leadership, Flair,
    WorkRate, OffTheBall, Positioning, Teamwork, Aggression, Composure,
    // Physis
    Acceleration, Pace, Stamina, Strength, Agility, Balance, JumpingReach, NaturalFitness,
    // Torwart
    Reflexes, CommandOfArea, Handling, OneOnOnes, Kicking, Throwing, RushingOut,
    Communication, Eccentricity,
}

public enum AttrGroup { Technical, Mental, Physical, Goalkeeping }

public static class AttrInfo
{
    public const int Count = (int)Attr.Eccentricity + 1;
    public const int Min = 1;
    public const int Max = 20;

    public static readonly Attr[] All = Enum.GetValues<Attr>();
    public static readonly Attr[] Technical = All.Where(a => GroupOf(a) == AttrGroup.Technical).ToArray();
    public static readonly Attr[] Mental = All.Where(a => GroupOf(a) == AttrGroup.Mental).ToArray();
    public static readonly Attr[] Physical = All.Where(a => GroupOf(a) == AttrGroup.Physical).ToArray();
    public static readonly Attr[] Goalkeeping = All.Where(a => GroupOf(a) == AttrGroup.Goalkeeping).ToArray();

    public static AttrGroup GroupOf(Attr a) => a switch
    {
        <= Attr.Marking => AttrGroup.Technical,
        <= Attr.Composure => AttrGroup.Mental,
        <= Attr.NaturalFitness => AttrGroup.Physical,
        _ => AttrGroup.Goalkeeping,
    };

    public static string TextKey(Attr a) => "attr." + a;
}

/// <summary>Die 44 sichtbaren Attribute eines Spielers. Werte werden immer auf 1–20 begrenzt.</summary>
[JsonConverter(typeof(PlayerAttributesConverter))]
public sealed class PlayerAttributes
{
    private readonly int[] _values = new int[AttrInfo.Count];

    public PlayerAttributes()
    {
        Array.Fill(_values, AttrInfo.Min);
    }

    public int this[Attr a]
    {
        get => _values[(int)a];
        set => _values[(int)a] = Math.Clamp(value, AttrInfo.Min, AttrInfo.Max);
    }

    public PlayerAttributes Clone()
    {
        var c = new PlayerAttributes();
        Array.Copy(_values, c._values, _values.Length);
        return c;
    }
}

public sealed class PlayerAttributesConverter : JsonConverter<PlayerAttributes>
{
    public override PlayerAttributes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new PlayerAttributes();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Attribute-Objekt erwartet.");
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            var name = reader.GetString() ?? throw new JsonException();
            reader.Read();
            if (!Enum.TryParse<Attr>(name, true, out var attr))
                throw new JsonException($"Unbekanntes Attribut '{name}'.");
            result[attr] = reader.GetInt32();
        }
        throw new JsonException("Attribute-Objekt nicht abgeschlossen.");
    }

    public override void Write(Utf8JsonWriter writer, PlayerAttributes value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var a in AttrInfo.All) writer.WriteNumber(a.ToString(), value[a]);
        writer.WriteEndObject();
    }
}

/// <summary>Versteckte Werte (Kapitel 4). Fähigkeit und Potenzial 1–200, Charakter 1–20.</summary>
public sealed class HiddenAttributes
{
    public int CurrentAbility { get; set; } = 100;
    public int PotentialAbility { get; set; } = 120;

    public int Ambition { get; set; } = 10;
    public int Professionalism { get; set; } = 10;
    public int Loyalty { get; set; } = 10;
    public int Pressure { get; set; } = 10;
    public int Temperament { get; set; } = 10;
    public int Sportsmanship { get; set; } = 10;
    public int Adaptability { get; set; } = 10;
    public int Controversy { get; set; } = 10;

    public int Consistency { get; set; } = 10;
    public int InjuryProneness { get; set; } = 10;
    public int ImportantMatches { get; set; } = 10;
}
