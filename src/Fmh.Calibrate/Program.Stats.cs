using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Calibrate;

/// <summary>Kennzahlen des Saisonlaufs: Managerkarriere, Gehälter und Finanzen.</summary>
public static partial class Program
{
    /// <summary>Kennzahlen der Karriere-Simulation: Pokal, Relegation, Transfers, Kader, Spielstärke nach Alter.</summary>
    private static void PrintCareerStats(GameSession session)
    {
        var w = session.World;
        var profiles = session.Data.PositionProfiles;
        foreach (var cup in w.Cups.Where(c => c.SeasonYear == w.SeasonYear))
        {
            var winner = cup.WinnerId is { } id ? w.Clubs[id].Name : "-";
            var final = cup.Fixtures.Where(f => f.Matchday == cup.Rounds.Count).Select(f => $"{w.Clubs[f.HomeId].ShortName} {f.Result?.HomeGoals}:{f.Result?.AwayGoals} {w.Clubs[f.AwayId].ShortName}{(f.Result?.PenaltiesHome is { } ph ? $" i.E. {ph}:{f.Result.PenaltiesAway}" : f.Result?.ExtraTime == true ? " n.V." : "")}");
            Console.WriteLine($"{cup.Name}: Sieger {winner} ({string.Join(", ", final)})");
        }
        var season = w.Transfers.Where(t => t.SeasonYear == w.SeasonYear).ToList();
        // Wechsel nach Vertragsende (Vorvertrag, ohne Ablöse) getrennt von Ablösetransfers zählen (Review F-07).
        Console.WriteLine($"Transfers: {season.Count(t => !t.Loan && t.FromClubId is not null && t.Fee > 0)} mit Ablöse ({season.Sum(t => t.Fee) / 1_000_000.0:F1} Mio.), " +
                          $"{season.Count(t => !t.Loan && t.FromClubId is not null && t.Fee == 0)} nach Vertragsende, {season.Count(t => t.FromClubId is null)} Vereinslose, {season.Count(t => t.Loan)} Leihen");
        MarketReport.PrintSeason(w);
        var leagueClubs = w.Clubs.Values.Where(c => w.FindLeague(c.LeagueId) is not null).ToList();
        // Mindestkader (Review F-06): kein Verein darunter; die KI-Vereine und der eigene Verein (Autopilot) getrennt.
        var thin = leagueClubs.Where(c => c.PlayerIds.Count < ClubRules.DivisionFinance(w, c).MinSquad).ToList();
        _squadOk &= thin.Count == 0;
        Console.WriteLine($"Unter dem Mindestkader: {thin.Count}" + (thin.Count > 0 ? $" ({string.Join(", ", thin.Take(5).Select(c => $"{c.Name} {c.PlayerIds.Count}{(c.Id == session.HumanClub.Id ? " eigener Verein" : "")}"))})" : ""));
        // Größe der Spielwelt (Review F-05): aktive Spieler höchstens +10 % gegenüber dem ersten Saisonende (gleicher Zeitpunkt
        // im Jahreslauf: Jugendjahrgang aufgenommen, 19-Jährige noch nicht freigegeben), Vereinslose vor den Karriereenden
        // zum Saisonwechsel höchstens 12 % der Profis.
        var contracted = w.Players.Values.Count(p => !p.Retired && p.ClubId is not null && !p.IsYouth);
        var free = session.FreeAgents.Count();
        var active = w.Players.Values.Count(p => !p.Retired);
        if (_activeStart == 0) _activeStart = active;
        var growth = active / (double)_activeStart - 1;
        _worldOk &= growth <= MaxActiveGrowth && free <= contracted * MaxFreeShare;
        Console.WriteLine($"Spielwelt: {active} aktive Spieler ({growth:+0.0%;-0.0%} seit dem ersten Saisonende, Grenze +{MaxActiveGrowth:P0}), {contracted} Profis, " +
                          $"{free} vereinslos ({free / (double)contracted:P1}, Grenze {MaxFreeShare:P0}), {w.Players.Count - active} im Ruhestand");
        Console.WriteLine($"Kader: Ø {leagueClubs.Average(c => c.PlayerIds.Count):F1} (min {leagueClubs.Min(c => c.PlayerIds.Count)}, max {leagueClubs.Max(c => c.PlayerIds.Count)}), " +
                          $"Jugend Ø {w.Clubs.Values.Average(c => c.YouthIds.Count):F1}, vereinslos {session.FreeAgents.Count()}, Spieler gesamt {w.Players.Count}");
        foreach (var league in w.Leagues)
        {
            var players = league.ClubIds.SelectMany(id => w.SquadOf(w.Clubs[id])).ToList();
            string Avg(Func<Player, bool> f) => players.Where(f).Select(p => Ratings.PositionRating(p, p.MainPosition, profiles)).DefaultIfEmpty(0).Average().ToString("F2", CultureInfo.InvariantCulture);
            var top = league.ClubIds.Select(id => w.SquadOf(w.Clubs[id]).OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, profiles)).Take(11)
                .Average(p => Ratings.PositionRating(p, p.MainPosition, profiles))).Average();
            Console.WriteLine($"{league.Name}: Top-11 Ø {top:F2}, U21 {Avg(p => p.Age(w.Date) <= 21)}, 22-29 {Avg(p => p.Age(w.Date) is > 21 and < 30)}, 30+ {Avg(p => p.Age(w.Date) >= 30)}, Alter Ø {players.Average(p => p.Age(w.Date)):F1}");
        }
        PrintFinanceStats(session);
        var b = session.HumanClub.Budget;
        Console.WriteLine($"Eigener Verein: Kontostand {b.Balance / 1_000_000.0:F1} Mio., Transferbudget {b.TransferBudget / 1_000_000.0:F1} Mio.");
        Console.WriteLine();
    }

    /// <summary>Gehälter der letzten ausgewerteten Saison im Zielkorridor (±30 % um <c>targetAnnualWage</c> je Division)?</summary>
    private static bool _wagesOk = true;
    private static bool _squadOk = true;
    private static bool _worldOk = true;
    private static int _activeStart;
    private const double MaxActiveGrowth = 0.10;
    private const double MaxFreeShare = 0.12;

    /// <summary>Erlaubte Lohnentwicklung je Saison (real rund 4 % im Jahr, DFL-Personalaufwand 2019/20–2024/25).</summary>
    private const double WageGrowth = 1.04;

    private static int _startYear;

    /// <summary>Durchschnittliches Jahresgehalt der Kaderspieler je Division gegen das Kalibrierungsziel aus den Daten.</summary>
    private static void PrintWageStats(World w)
    {
        _wagesOk = true;
        if (_startYear == 0) _startYear = w.SeasonYear;
        var growth = Math.Pow(WageGrowth, w.SeasonYear - _startYear);
        foreach (var div in w.Leagues.GroupBy(l => (l.CountryCode, l.Format.Id)))
        {
            var clubs = div.SelectMany(l => l.ClubIds).Select(id => w.Clubs[id]).ToList();
            var target = (long)(ClubRules.DivisionFinance(w, clubs[0]).TargetAnnualWage * growth);
            // Leihspieler mit dem Anteil, den der aufnehmende Verein zahlt.
            var wages = clubs.SelectMany(c => w.SquadOf(c)).Select(p => (p.Contract?.WeeklyWage ?? 0) * 52.0 * (p.Loan?.WageSharePercent ?? 100) / 100).ToList();
            var avg = wages.Average();
            var median = wages.Order().ElementAt(wages.Count / 2);
            var ok = target == 0 || Math.Abs(avg / target - 1) <= 0.3;
            _wagesOk &= ok;
            Console.WriteLine($"Gehälter {div.First().Format.Name}: Ø {avg / 1000:F0} Tsd. im Jahr, Median {median / 1000:F0} Tsd., Kader Ø {clubs.Average(c => Budgets.WageBill(c, w) * 52.0) / 1_000_000:F1} Mio." +
                              (target > 0 ? $", Ziel {target / 1000} Tsd. ±30 % {(ok ? "ok" : "ABWEICHUNG")}" : ""));
        }
    }

    /// <summary>Finanzkennzahlen je Liga: Einnahmen, Ausgaben, Saldo und Kontostände (Kalibrierung Phase 3).</summary>
    private static void PrintFinanceStats(GameSession session)
    {
        var w = session.World;
        static string M(double v) => (v / 1_000_000).ToString("F1", CultureInfo.InvariantCulture);
        PrintWageStats(w);
        ReferenceReport.Print(w);
        EconomyReport.PrintSeason(w);
        foreach (var league in w.Leagues)
        {
            var clubs = league.ClubIds.Select(id => w.Clubs[id]).ToList();
            var income = clubs.Average(c => c.Finance.Season.Values.Where(v => v > 0).Sum());
            var expenses = clubs.Average(c => c.Finance.Season.Values.Where(v => v < 0).Sum());
            var balances = clubs.Select(c => (double)c.Budget.Balance).ToList();
            Console.WriteLine($"{league.Name} Finanzen: Einnahmen Ø {M(income)} Mio., Ausgaben Ø {M(expenses)} Mio., Saldo Ø {M(income + expenses)} Mio., " +
                              $"Konto Ø {M(balances.Average())} (min {M(balances.Min())}, max {M(balances.Max())}), im Minus {balances.Count(x => x < 0)}, " +
                              $"Zuschauer Ø {clubs.Average(c => c.Stadium.Capacity):F0} Plätze");
            var types = clubs.SelectMany(c => c.Finance.Season).GroupBy(kv => kv.Key)
                .Select(g => (g.Key, avg: g.Sum(kv => kv.Value) / (double)clubs.Count)).Where(x => Math.Abs(x.avg) >= 10_000).OrderByDescending(x => x.avg);
            Console.WriteLine("  " + string.Join(", ", types.Select(x => $"{x.Key} {M(x.avg)}")));
        }
        foreach (var league in w.Leagues)
        {
            var clubs = league.ClubIds.Select(id => w.Clubs[id]).ToList();
            string Groups(Club c) => string.Join("/", Enum.GetValues<YouthAgeGroup>().Select(g => session.YouthGroup(c, g).Count));
            Console.WriteLine($"{league.Name} Nachwuchs: Wert Ø {clubs.Average(c => c.Academy.Rating):F1}, Sterne Ø {clubs.Average(c => c.Academy.Stars):F1}, " +
                              $"Budget Ø {M(clubs.Average(c => (double)c.Academy.Budget))} Mio., Jugend Ø {clubs.Average(c => c.YouthIds.Count):F1} (U15/U17/U19 z. B. {Groups(clubs[0])}), " +
                              $"Absolventen Ø {clubs.Average(c => c.Academy.Graduates.Count):F1}; Lizenz: {string.Join(", ", clubs.GroupBy(c => c.License.Outcome).Select(g => $"{g.Key} {g.Count()}"))}, " +
                              $"Europa-Lizenz {clubs.Count(c => c.License.UefaLicense)}, FFP {string.Join(", ", clubs.GroupBy(c => c.License.Ffp).Select(g => $"{g.Key} {g.Count()}"))}");
        }
        foreach (var c in w.Leagues.SelectMany(l => l.ClubIds).Select(id => w.Clubs[id]).Where(c => c.License.Outcome != LicenseOutcome.Granted || !c.License.UefaLicense && w.FindLeague(c.LeagueId)?.Level == 1))
            Console.WriteLine($"  Lizenz {c.ShortName} ({c.LeagueId}): {c.License.Outcome}, Eigenkapital {M(session.Equity(c))}, Kredite {M(c.Finance.Loans.Sum(l => l.Remaining))}, " +
                              $"Sterne {c.Academy.Stars}, Flutlicht {c.Stadium.Floodlight}, Sitze {c.Stadium.SeatingTotal}, Auflagen [{string.Join(",", c.License.Conditions)}], Europa [{string.Join(",", c.License.UefaReasons)}]");
        Console.WriteLine("Europa: " + string.Join(", ", w.EuropeanEntries.Select(e => $"{w.Clubs[e.ClubId].ShortName} {e.CompetitionId} {e.SeasonYear} R{e.Stage}{(e.Replacement ? " (nachgerückt)" : "")}")));
        var h = session.HumanClub;
        Console.WriteLine($"Eigener Verein: Vorstand {h.Board.Trust:F0}, Fans {h.Fans.Mood:F0}, Medien {session.HumanManager.Career.Media:F0}, " +
                          $"Ereignisse {w.Events.Count(e => e.ClubId == h.Id)}, Erfolge {session.HumanManager.Career.Achievements.Count}, " +
                          $"Sponsoren {h.Sponsors.Count}, Personal {w.Staff.Count(s => s.ClubId == h.Id)}, Gebäude {h.Grounds.Buildings.Count}");
    }
}
