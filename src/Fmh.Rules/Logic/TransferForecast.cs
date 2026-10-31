using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>Vorschau künftiger Transfer- und Gehaltslasten über mehrere Saisons (Must now E1-S7).</summary>
public static class TransferForecast
{
    public const int Seasons = 4;

    public sealed record SeasonLine(int SeasonYear, long Wages, long InstalmentsOut, long InstalmentsIn, long AddOns, long LoanFees)
    {
        public long TotalOut => Wages + InstalmentsOut + AddOns + LoanFees;
        public long TotalIn => InstalmentsIn;
        public long Net => TotalIn - TotalOut;
    }

    /// <summary>
    /// Schätzt Verpflichtungen des Vereins über <see cref="Seasons"/> Saisons ab dem aktuellen Datum:
    /// Gehälter (laufende Verträge), offene Ablöseraten, geschätzte Zusatzzahlungen und fällige Leihraten.
    /// </summary>
    public static IReadOnlyList<SeasonLine> ForClub(World world, Club club)
    {
        var lines = new SeasonLine[Seasons];
        for (var i = 0; i < Seasons; i++)
            lines[i] = new SeasonLine(world.SeasonYear + i, 0, 0, 0, 0, 0);

        void Add(int seasonYear, Func<SeasonLine, SeasonLine> update)
        {
            var idx = seasonYear - world.SeasonYear;
            if (idx < 0 || idx >= Seasons) return;
            lines[idx] = update(lines[idx]);
        }

        foreach (var p in world.SquadOf(club))
        {
            if (p.Contract is not { } c) continue;
            for (var y = world.SeasonYear; y < world.SeasonYear + Seasons; y++)
            {
                var seasonEnd = new DateOnly(y + 1, 6, 30);
                if (c.Until < new DateOnly(y, 7, 1)) break;
                var weeks = 52;
                Add(y, line => line with { Wages = line.Wages + (long)c.WeeklyWage * weeks });
                if (c.Until <= seasonEnd) break;
            }
        }

        foreach (var inst in world.Installments)
        {
            var season = inst.Due.Month >= 7 ? inst.Due.Year : inst.Due.Year - 1;
            if (inst.FromClubId == club.Id)
                Add(season, line => line with { InstalmentsOut = line.InstalmentsOut + inst.Amount });
            if (inst.ToClubId == club.Id)
                Add(season, line => line with { InstalmentsIn = line.InstalmentsIn + inst.Amount });
        }

        foreach (var cond in world.TransferConditions.Where(c => c.PayerId == club.Id))
        {
            // Grobe Schätzung: restliche Laufzeit × erwarteter Einheiten, abgezinst wie beim Verkäufer.
            if (!world.Players.TryGetValue(cond.PlayerId, out var p)) continue;
            var yearsLeft = Math.Max(0, (cond.Until.DayNumber - world.Date.DayNumber) / 365.25);
            var perYear = cond.Kind == TransferConditionKind.Appearance
                ? TransferTerms.ExpectedAppearances(p) * cond.Amount
                : TransferTerms.ExpectedAppearances(p) * TransferTerms.ExpectedGoalsPerAppearance(p) * cond.Amount;
            for (var i = 0; i < Seasons && i < Math.Ceiling(yearsLeft); i++)
            {
                var amount = (long)(perYear * TransferTerms.AddOnDiscount);
                Add(world.SeasonYear + i, line => line with { AddOns = line.AddOns + amount });
            }
        }

        return lines;
    }
}
