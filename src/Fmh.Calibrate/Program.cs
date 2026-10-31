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

/// <summary>
/// Kommandozeilen-Simulator zur Kalibrierung der Matchengine (Kapitel 12).
///   fmh-calibrate [--matches 10000] [--seed 1] [--data pfad] [--strict]
///   fmh-calibrate --season [--seasons 1]       simuliert ganze Saisons und misst die Rechenzeit
///   --strict                                   Exitcode 1, wenn eine Kennzahl die Toleranz verlässt
///   --max-ms 3                                 zusätzlich Rechenzeit je Spiel prüfen (nur auf vergleichbarer Hardware)
///   --max-drift 0.05                           zusätzlich Ligastärke-Drift je Saison prüfen
///   fmh-calibrate --engine datei.json          rechnet mit alternativen Engine-Gewichten
///   fmh-calibrate --write-config datei.json    schreibt die Standard-Engine-Gewichte als JSON
/// </summary>
public static class Program
{
    private static readonly CultureInfo De = Fmt.Culture;

    public static int Main(string[] args)
    {
        var opts = Args.Parse(args);
        var data = PackageLoader.Load(PackageLoader.FindDataRoot(opts.Get("data")));

        if (opts.Get("write-config") is { } path)
        {
            // Schreibt die im Code hinterlegten, kalibrierten Standardwerte (nicht die geladene Datei).
            File.WriteAllText(path, JsonSerializer.Serialize(new EngineConfig(), Json.Options) + Environment.NewLine);
            Console.WriteLine($"Engine-Konfiguration geschrieben: {path}");
            return 0;
        }
        if (opts.Get("engine") is { } enginePath)
        {
            // Alternative Gewichte zum Ausprobieren, ohne das Datenpaket zu ändern.
            data = new GameData
            {
                Packages = data.Packages, Countries = data.Countries, Clubs = data.Clubs, Names = data.Names,
                Formations = data.Formations, PositionProfiles = data.PositionProfiles, Injuries = data.Injuries, Roles = data.Roles,
                Languages = data.Languages, DefaultLanguage = data.DefaultLanguage, Engine = Json.Load<EngineConfig>(enginePath),
                TransferRules = data.TransferRules,
            };
            data.UseLanguage(data.DefaultLanguage);
        }

        var seed = ulong.Parse(opts.Get("seed") ?? "1", CultureInfo.InvariantCulture);
        if (opts.Has("season"))
            return RunSeasons(data, seed, int.Parse(opts.Get("seasons") ?? "1", CultureInfo.InvariantCulture), opts.Has("strict"),
                opts.Get("max-drift") is { } d ? double.Parse(d, CultureInfo.InvariantCulture) : null);

        var matches = int.Parse(opts.Get("matches") ?? "10000", CultureInfo.InvariantCulture);
        // Toleranzen gelten für den Profibereich; untere Ebenen lassen sich mit --max-level einbeziehen.
        var maxLevel = int.Parse(opts.Get("max-level") ?? "2", CultureInfo.InvariantCulture);
        return RunMatches(data, seed, matches, maxLevel, opts.Has("strict"), opts.Get("max-ms") is { } ms ? double.Parse(ms, CultureInfo.InvariantCulture) : null);
    }

    private static GameSession NewSession(GameData data, ulong seed)
    {
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = seed, ClubKey = data.Clubs[0].Key });
        return new GameSession(world, data, new TextMatchEngine());
    }

    private static int RunMatches(GameData data, ulong seed, int count, int maxLevel, bool strict, double? maxMs)
    {
        var session = NewSession(data, seed);
        var world = session.World;
        var rng = new Rng(seed ^ 0xC0FFEE);
        var m = new Metrics();
        var leagues = world.Leagues.Where(l => l.Level <= maxLevel).ToList();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var league = leagues[i % leagues.Count];
            var home = league.ClubIds[rng.Next(league.ClubIds.Count)];
            int away;
            do away = league.ClubIds[rng.Next(league.ClubIds.Count)]; while (away == home);
            var fixture = new Fixture { Id = 1_000_000 + i, CompetitionId = league.Id, HomeId = home, AwayId = away, Date = world.Date, RefereeId = world.Referees[rng.Next(world.Referees.Count)].Id };
            var sim = session.StartMatch(fixture, humanControlled: false);
            sim.RunToEnd();
            m.Add(sim);
        }
        sw.Stop();

        Console.WriteLine($"FMH Matchengine-Kalibrierung: {Fmt.Int(count)} Spiele (Ebene 1–{maxLevel}), Seed {seed}, {sw.Elapsed.TotalSeconds:F1} s ({sw.Elapsed.TotalMilliseconds / count:F2} ms/Spiel)");
        Console.WriteLine();
        Console.WriteLine($"{"Kennzahl",-34} {"Ist",10} {"Ziel",10} {"Toleranz",10}  ");
        Console.WriteLine(new string('-', 72));
        var ok = true;
        // Toleranzen nach Abschnitt 8.1 des Umsetzungskonzepts (docs/umsetzungskonzept.md).
        ok &= Row("Tore pro Spiel", m.Goals / count, 2.8, 0.15);
        ok &= Row("Unentschieden (%)", 100.0 * m.Draws / count, 25, 2.5);
        ok &= Row("Heimsiege (%)", 100.0 * m.HomeWins / count, 45, 2.5);
        Row("Auswärtssiege (%)", 100.0 * m.AwayWins / count, null, null);
        ok &= Row("Gelbe Karten pro Spiel", m.Yellow / count, 3.8, 0.3);
        ok &= Row("Rote Karten pro Spiel", m.Red / count, 0.2, 0.05);
        ok &= Row("Verletzungen pro Team und Spiel", m.Injuries / (2.0 * count), 0.3, 0.05);
        if (maxMs is { } limit)
        {
            var ms = sw.Elapsed.TotalMilliseconds / count;
            Console.WriteLine($"{"Rechenzeit je Spiel (ms)",-34} {ms.ToString("0.00", De),10} {"≤ " + limit.ToString("0.00", De),10} {"",10}  {(ms <= limit ? "ok" : "ABWEICHUNG")}");
            ok &= ms <= limit;
        }
        Console.WriteLine(new string('-', 72));
        Row("Schüsse pro Team", m.Shots / (2.0 * count), null, null);
        Row("Schüsse aufs Tor pro Team", m.OnTarget / (2.0 * count), null, null);
        Row("xG pro Spiel", m.Xg / count, null, null);
        Row("Ballbesitz Heim (%)", m.HomePossession / count, null, null);
        Row("Pässe pro Team", m.Passes / (2.0 * count), null, null);
        Row("Passquote (%)", 100.0 * m.PassesCompleted / Math.Max(1, m.Passes), null, null);
        Row("Ecken pro Spiel", m.Corners / count, null, null);
        Row("Fouls pro Spiel", m.Fouls / count, null, null);
        Row("Abseits pro Spiel", m.Offsides / count, null, null);
        Row("Elfmeter pro Spiel", m.Penalties / count, null, null);
        Row("Wechsel pro Team", m.Subs / (2.0 * count), null, null);
        Row("Ø Note (1,0–6,0)", m.GradeSum / Math.Max(1, m.Graded), null, null);
        Row("Ø Kondition bei Abpfiff (%)", m.EndCondition / Math.Max(1, m.EndPlayers), null, null);
        Row("Laufleistung pro Team (km)", m.Distance / (2.0 * count), null, null);
        Row("Angriffe ins letzte Drittel", m.Phases / count, null, null);
        Console.WriteLine();
        Console.WriteLine(ok ? "Alle Zielwerte im Toleranzbereich." : "ACHTUNG: Mindestens ein Zielwert liegt außerhalb der Toleranz.");
        return strict && !ok ? 1 : 0;
    }

    private static bool Row(string name, double value, double? target, double? tol)
    {
        var inRange = target is null || Math.Abs(value - target.Value) <= tol!.Value;
        var t = target is null ? "" : target.Value.ToString("0.00", De);
        var to = tol is null ? "" : "±" + tol.Value.ToString("0.00", De);
        Console.WriteLine($"{name,-34} {value.ToString("0.00", De),10} {t,10} {to,10}  {(target is null ? "" : inRange ? "ok" : "ABWEICHUNG")}");
        return inRange;
    }

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

    private static int RunSeasons(GameData data, ulong seed, int seasons, bool strict, double? maxDrift)
    {
        var session = NewSession(data, seed);
        var world = session.World;
        var sw = Stopwatch.StartNew();
        var matchdays = 0;
        var slowest = TimeSpan.Zero;
        var targetSeason = world.SeasonYear + seasons;
        var topLeague = world.Leagues.OrderBy(l => l.Level).First();
        var strengthStart = TopEleven(session, topLeague);
        Console.WriteLine($"Start: Top-11 {topLeague.Name} Ø {strengthStart:F3}");
        var seasonEnds = new List<double>();
        PrintWageStats(world);
        if (seasons == 0) return 0;

        Stopwatch? dayWatch = null;
        while (world.SeasonYear < targetSeason)
        {
            // Tageweise, damit Insolvenzen und Lizenzentscheidungen je Tag gezählt werden (gleicher Spielverlauf).
            var result = session.Advance(1);
            EconomyReport.Observe(world);
            MarketReport.Observe(world);
            // Autopilot: Der eigene Verein hält wie ein Manager den Mindestkader mit Vereinslosen (die KI tut das selbst).
            if (world.Date.DayOfWeek == DayOfWeek.Monday && !session.AwaitingNewJob)
                for (var i = 0; i < 5 && session.HumanClub.PlayerIds.Count < ClubRules.DivisionFinance(world, session.HumanClub).MinSquad; i++)
                    if (!session.SignBestFreeAgent(session.HumanClub, session.NeededPosition(session.HumanClub))) break;
            if (result.Reason == StopReason.Decision) session.DelegateDecisions();
            if (dayWatch is not null)
            {
                // Spieltag gemessen vom eigenen Spiel bis alle übrigen Spiele des Tages berechnet sind.
                if (dayWatch.Elapsed > slowest) slowest = dayWatch.Elapsed;
                dayWatch = null;
            }
            if (result.Reason == StopReason.HumanMatch && result.Fixture is { } f)
            {
                dayWatch = Stopwatch.StartNew();
                session.PlayInstant(f);
                matchdays++;
            }
            if (result.Reason == StopReason.SeasonEnd)
            {
                foreach (var league in world.Leagues)
                {
                    Console.WriteLine($"{league.Name} {GameSession.SeasonLabel(world.SeasonYear)}");
                    foreach (var row in LeagueTable.Compute(league))
                    {
                        var c = world.Clubs[row.ClubId];
                        Console.WriteLine($"{row.Position,3}. {c.Name,-26} {row.Played,3} {row.Won,3} {row.Drawn,3} {row.Lost,3} {row.GoalsFor,4}:{row.GoalsAgainst,-4} {row.Points,4}");
                    }
                    Console.WriteLine();
                }
                PrintCareerStats(session);
                seasonEnds.Add(TopEleven(session, topLeague));
            }
            // Nach der Saisonauswertung, damit eine Entlassung am letzten Spieltag die Saison nicht überspringt.
            if (session.AwaitingNewJob)
            {
                var reason = world.Messages.LastOrDefault(m => m.SubjectRef?.Key == "msg.sacked.subject")?.BodyText(data.Texts);
                Console.WriteLine($"Entlassen am {world.Date:yyyy-MM-dd}: {reason}");
                // Mit dem ersten Jobangebot weiter, damit die Kalibrierung nicht am Zufall einer Entlassung hängt.
                if (session.HumanManager.Career.Offers.FirstOrDefault() is { } offer && session.AcceptJob(offer) is null)
                {
                    Console.WriteLine($"Neuer Verein: {session.HumanClub.Name}");
                    continue;
                }
                PrintFinanceStats(session);
                break;
            }
        }
        sw.Stop();
        Console.WriteLine($"{seasons} Saison(s) in {sw.Elapsed.TotalSeconds:F1} s, {matchdays} Spieltage des eigenen Vereins, langsamster Spieltag {slowest.TotalMilliseconds:F0} ms (Ziel < 5000 ms).");
        // Drift von Saisonende zu Saisonende (gleicher Zeitpunkt im Jahreslauf, Review F-07): Die erzeugte Startwelt hat volle
        // Kader, am ersten Tag nach einer Saison fehlen je nach Transferfenster noch die Neuzugänge. Mit nur einer Saison
        // bleibt der Vergleich mit dem Start.
        var fromStart = (TopEleven(session, topLeague) - strengthStart) / Math.Max(1, seasons);
        var drift = seasonEnds.Count >= 2 ? (seasonEnds[^1] - seasonEnds[0]) / (seasonEnds.Count - 1) : fromStart;
        Console.WriteLine($"Ligastärke-Drift {topLeague.Name}: {drift:+0.000;-0.000} je Saison von Saisonende zu Saisonende" +
                          (maxDrift is { } md ? $" (Grenze {md:0.000})" : " (Ziel ≤ 0,05)") + $", ab Start {fromStart:+0.000;-0.000}");
        var financeOk = ReferenceReport.MeansOk();
        var economyOk = EconomyReport.PrintSummary();
        var marketOk = MarketReport.PrintSummary(world, data);
        var ok = slowest.TotalMilliseconds < 5000 && world.SeasonYear >= targetSeason && (maxDrift is not { } m || drift <= m) && _wagesOk && _squadOk && _worldOk
                 && financeOk && economyOk && marketOk;
        if (!ok) Console.WriteLine("ACHTUNG: Saisonlauf außerhalb der Vorgaben.");
        return strict && !ok ? 1 : 0;
    }

    /// <summary>Durchschnitt der besten elf Spieler je Verein einer Liga.</summary>
    private static double TopEleven(GameSession session, League league)
    {
        var w = session.World;
        var profiles = session.Data.PositionProfiles;
        return league.ClubIds.Select(id => w.SquadOf(w.Clubs[id]).Select(p => Ratings.PositionRating(p, p.MainPosition, profiles))
            .OrderDescending().Take(11).DefaultIfEmpty(0).Average()).Average();
    }

    private sealed class Metrics
    {
        public double Goals, Draws, HomeWins, AwayWins, Yellow, Red, Injuries, Shots, OnTarget, Xg, HomePossession;
        public double Passes, PassesCompleted, Corners, Fouls, Offsides, Penalties, Subs, GradeSum, Graded, EndCondition, EndPlayers, Distance, Phases;

        public void Add(IMatchSimulation sim)
        {
            var h = sim.Home;
            var a = sim.Away;
            Goals += h.Goals + a.Goals;
            if (h.Goals == a.Goals) Draws++;
            else if (h.Goals > a.Goals) HomeWins++;
            else AwayWins++;
            foreach (var t in new[] { h, a })
            {
                Yellow += t.Stats.YellowCards;
                Red += t.Stats.RedCards;
                Injuries += t.Stats.Injuries;
                Shots += t.Stats.Shots;
                OnTarget += t.Stats.ShotsOnTarget;
                Xg += t.Stats.Xg;
                Passes += t.Stats.Passes;
                PassesCompleted += t.Stats.PassesCompleted;
                Corners += t.Stats.Corners;
                Fouls += t.Stats.Fouls;
                Offsides += t.Stats.Offsides;
                Subs += t.SubstitutionsUsed;
                Distance += t.Stats.DistanceKm;
                foreach (var p in t.Participants)
                {
                    if (p.Grade is { } g) { GradeSum += g; Graded++; }
                }
                foreach (var p in t.OnPitch) { EndCondition += p.Condition; EndPlayers++; }
            }
            var total = h.Stats.PossessionSeconds + a.Stats.PossessionSeconds;
            HomePossession += total <= 0 ? 50 : 100 * h.Stats.PossessionSeconds / total;
            Penalties += sim.Events.Count(e => e.Type == MatchEventType.Penalty);
            Phases += sim.Events.Count(e => e.Type is MatchEventType.Attack or MatchEventType.Counter);
        }
    }
}
