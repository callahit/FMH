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
public static partial class Program
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
            RelationsReport.Observe(world, data);
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
                ManagerReport.Observe(session);
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
        var economyOk = EconomyReport.PrintSummary(world);
        var marketOk = MarketReport.PrintSummary(world, data);
        var managersOk = ManagerReport.PrintSummary(data);
        var relationsOk = RelationsReport.PrintSummary(data);
        var tournamentsOk = TournamentReport.PrintSummary(world, data, targetSeason - seasons);
        var ok = slowest.TotalMilliseconds < 5000 && world.SeasonYear >= targetSeason && (maxDrift is not { } m || drift <= m) && _wagesOk && _squadOk && _worldOk
                 && financeOk && economyOk && marketOk && managersOk && relationsOk && tournamentsOk;
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
