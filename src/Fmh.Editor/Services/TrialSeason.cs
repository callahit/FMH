using System.Diagnostics;
using Fmh.Core.Data;
using Fmh.Core.Data.Editing;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.MatchEngine;

namespace Fmh.Editor.Services;

/// <summary>Ergebnis einer Liga im Probelauf.</summary>
public sealed record TrialLeague(string Country, int Level, string Name, int Clubs, double GoalsPerMatch, string Champion,
                                 IReadOnlyList<string> Up, IReadOnlyList<string> Down, long AverageBalanceChange);

/// <summary>Ergebnis des Probelaufs; bei Fehlern in den Daten startet keine Saison.</summary>
public sealed record TrialResult(IReadOnlyList<string> Errors, IReadOnlyList<TrialLeague> Leagues, int Cups, TimeSpan Duration);

/// <summary>
/// Probelauf „Saison simulieren“ (Umsetzungskonzept 7.3): Das Paket mit dem aktuellen, auch ungespeicherten Stand wird
/// in einen temporären Ordner geschrieben und wie beim Spielstart geladen. Gespielt wird eine Saison nur des gewählten
/// Landes (übrige Länder ruhen); danach zeigen Kennzahlen Tore je Spiel, Meister, Auf- und Absteiger und Finanzen.
/// </summary>
public static class TrialSeason
{
    public static TrialResult Run(PackageWorkspace ws, string? countryCode, ulong seed = 1, IProgress<string>? progress = null,
                                  CancellationToken cancel = default)
    {
        var watch = Stopwatch.StartNew();
        var temp = Path.Combine(Path.GetTempPath(), "fmh-trial-" + Guid.NewGuid().ToString("N"));
        try
        {
            ws.Export(Path.Combine(temp, ws.Manifest.Id));
            GameData data;
            try { data = PackageLoader.Load(new[] { ws.DataRoot, temp }); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or KeyNotFoundException)
            {
                return new TrialResult(new[] { ex.Message }, Array.Empty<TrialLeague>(), 0, watch.Elapsed);
            }
            var errors = DataValidator.Validate(data);
            if (errors.Count > 0) return new TrialResult(errors, Array.Empty<TrialLeague>(), 0, watch.Elapsed);

            var code = countryCode ?? data.Countries.FirstOrDefault(c => c.Activation == CountryActivation.Playable)?.Code;
            var country = data.Countries.Where(c => c.Code == code).OrderBy(c => c.ValidFrom).FirstOrDefault()
                          ?? throw new InvalidOperationException($"Land '{code}' fehlt.");
            var top = country.Levels.OrderBy(l => l.Level).First().Divisions[0];
            var club = data.Clubs.First(c => c.Division == top.Id || top.Groups.Any(g => g.Id == c.Division));
            var options = new NewGameOptions
            {
                Seed = seed, ClubKey = club.Key, ManagerName = "Probelauf",
                Activation = data.Countries.Select(c => c.Code).Distinct().Where(c => c != country.Code)
                    .ToDictionary(c => c, _ => CountryActivation.Inactive),
            };
            var world = WorldGenerator.Create(data, options);
            var session = new GameSession(world, data, new TextMatchEngine());
            var start = world.Clubs.Values.ToDictionary(c => c.Id, c => c.Budget.Balance);
            var leagues = new List<TrialLeague>();
            for (var step = 0; step < 50_000; step++)
            {
                cancel.ThrowIfCancellationRequested();
                var r = session.Advance();
                if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) session.PlayInstant(f);
                if (r.Reason == StopReason.Decision) session.DelegateDecisions();
                if (step % 20 == 0) progress?.Report(world.Date.ToString("d", System.Globalization.CultureInfo.CurrentCulture));
                if (r.Reason != StopReason.SeasonEnd) continue;
                foreach (var league in world.Leagues.Where(l => l.CountryCode == country.Code).OrderBy(l => l.Level))
                {
                    var table = LeagueTable.Compute(league);
                    var played = table.Sum(t => t.Played);
                    string NameOf(int id) => world.Clubs[id].Name;
                    leagues.Add(new TrialLeague(country.Code, league.Level, league.Name, league.ClubIds.Count,
                        played == 0 ? 0 : table.Sum(t => t.GoalsFor) / (played / 2.0),
                        table.Count > 0 ? NameOf(table[0].ClubId) : "",
                        table.Where(t => Pyramid.DirectZone(league, t.Position, world.SeasonYear) == MovementDirection.Up).Select(t => NameOf(t.ClubId)).ToList(),
                        table.Where(t => Pyramid.DirectZone(league, t.Position, world.SeasonYear) == MovementDirection.Down).Select(t => NameOf(t.ClubId)).ToList(),
                        league.ClubIds.Count == 0 ? 0 : (long)league.ClubIds.Average(id => world.Clubs[id].Budget.Balance - start.GetValueOrDefault(id))));
                }
                break;
            }
            return new TrialResult(Array.Empty<string>(), leagues, world.Cups.Count(c => c.CountryCode == country.Code), watch.Elapsed);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }
}
