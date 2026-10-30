using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Leihgrenzen nach FIFA-Transferstatut bzw. nationalen Regeln: Leihen zwischen zwei Vereinen eines Landes zählen nach
/// dessen Regeln (ggf. abweichend für Leihen innerhalb einer Division), Leihen über Landesgrenzen nach den FIFA-Regeln.
/// Beide Kontingente sind getrennt, wie beim Nebeneinander von FIFA- und Verbandsregeln.
/// </summary>
public static class LoanLimits
{
    /// <summary>Regeln, nach denen eine Leihe von <paramref name="parent"/> an <paramref name="borrower"/> zählt.</summary>
    public static LoanRules RulesFor(World world, GameData data, Club parent, Club borrower)
    {
        if (parent.CountryCode != borrower.CountryCode) return data.TransferRules.InternationalLoans;
        var loans = world.Country(parent.CountryCode).Loans;
        var division = Division(world, parent);
        if (division is not null && division == Division(world, borrower) && loans.Divisions.TryGetValue(division, out var r)) return r;
        return loans.Default;
    }

    private static string? Division(World world, Club club) => world.FindLeague(club.LeagueId)?.Format.Id;

    /// <summary>Startjahr der Saison, zu der ein Datum gehört (Saisonbeginn 1. Juli).</summary>
    public static int SeasonOf(DateOnly date) => date.Month >= 7 ? date.Year : date.Year - 1;

    /// <summary>Ist die Leihe von den Höchstzahlen ausgenommen (junger bzw. selbst ausgebildeter Spieler)?</summary>
    public static bool IsExempt(Player p, Club parent, LoanRules r, DateOnly start)
    {
        if (r.ExemptAge <= 0) return false;
        if (r.ExemptNeedsClubTrained && !Registration.IsClubTrained(p, parent.Id)) return false;
        var season = SeasonOf(start);
        var cutoff = r.AgeCutoff == LoanAgeCutoff.December31 ? new DateOnly(season, 12, 31) : new DateOnly(season + 1, 6, 30);
        return p.Age(cutoff) <= r.ExemptAge;
    }

    /// <summary>
    /// Verstößt eine neue Leihe gegen die Grenzen? Gibt den Textschlüssel des Grundes zurück, sonst null.
    /// <paramref name="activeLoans"/>: alle derzeit verliehenen Spieler, falls schon ermittelt (sonst aus der Welt).
    /// </summary>
    public static string? Check(World world, GameData data, Player p, Club parent, Club borrower, int months, IEnumerable<Player>? activeLoans = null)
    {
        var rules = RulesFor(world, data, parent, borrower);
        if (rules.MaxMonths > 0 && months > rules.MaxMonths) return "neg.error.loanMonths";
        var exempt = IsExempt(p, parent, rules, world.Date);
        var (maxOut, maxIn) = rules.Limits(world.SeasonYear);

        int outCount = 0, inCount = 0, pairCount = 0;
        foreach (var other in activeLoans ?? world.Players.Values)
        {
            if (other.Loan is not { } loan || other.ClubId is not { } holderId || other.Id == p.Id) continue;
            var involvesParent = loan.ParentClubId == parent.Id || holderId == parent.Id;
            var involvesBorrower = loan.ParentClubId == borrower.Id || holderId == borrower.Id;
            if (!involvesParent && !involvesBorrower) continue;
            var lender = world.Clubs[loan.ParentClubId];
            var holder = world.Clubs[holderId];
            if (!ReferenceEquals(RulesFor(world, data, lender, holder), rules)) continue;
            var otherExempt = IsExempt(other, lender, rules, loan.Since ?? world.Date);
            if (lender.Id == parent.Id && holder.Id == borrower.Id && (!otherExempt || rules.PerClubCountsExempt)) pairCount++;
            if (otherExempt) continue;
            if (lender.Id == parent.Id) outCount++;
            if (holder.Id == borrower.Id) inCount++;
        }
        if (rules.MaxPerClub > 0 && (!exempt || rules.PerClubCountsExempt) && pairCount >= rules.MaxPerClub) return "neg.error.loanClubLimit";
        if (exempt) return null;
        if (maxOut > 0 && outCount >= maxOut) return "neg.error.loanOutLimit";
        if (maxIn > 0 && inCount >= maxIn) return "neg.error.loanInLimit";
        if (rules.MaxInPerSeason > 0)
        {
            var season = world.Transfers.Count(t => t.Loan && t.ToClubId == borrower.Id && t.SeasonYear == world.SeasonYear
                && t.FromClubId is { } from && world.Clubs.TryGetValue(from, out var lender) && world.Players.TryGetValue(t.PlayerId, out var lp)
                && ReferenceEquals(RulesFor(world, data, lender, borrower), rules) && !IsExempt(lp, lender, rules, t.Date));
            if (season >= rules.MaxInPerSeason) return "neg.error.loanSeasonLimit";
        }
        return null;
    }
}
