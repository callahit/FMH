using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Grauzonen (AP 13c): Taten, Kosten und Sperrfrist nach rules/grayZones.json.</summary>
public static class GrayZones
{
    public const string Feature = "anstoss.grayZones";

    public static GrayAction? Find(GrayZoneRules rules, string id) => rules.Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>Betrag für einen Erstligisten, skaliert mit der Vereinsgröße.</summary>
    public static long Scaled(long amount, Club club) => amount <= 0 ? 0 : MarketValue.Round(amount * ClubRules.Scale(club));

    /// <summary>Seit der letzten Tat ist die Sperrfrist noch nicht vorbei.</summary>
    public static bool CoolingDown(GrayZoneRules rules, GrayZoneFile? file, DateOnly today) =>
        file?.Deeds.Count > 0 && today.DayNumber - file.Deeds.Max(d => d.Date.DayNumber) < rules.CooldownDays;
}
