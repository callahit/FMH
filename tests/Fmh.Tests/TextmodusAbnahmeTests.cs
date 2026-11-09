using System.Text.RegularExpressions;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.SceneTree;

namespace Fmh.Tests;

/// <summary>
/// Abnahme des Textmodus: Platzhalter, Wetter- und Easter-Egg-Tore, Nachspielzeit,
/// Gleichlauf von Ereignisstrom und Statistik, Determinismus.
/// </summary>
public class TextmodusAbnahmeTests
{
    private static readonly Regex Placeholder = new(@"\b[12]-[A-Z]{2,4}[0-3]?\b|\b(REF|OFFENDER|VICTIM|REASON|INJURY)\b", RegexOptions.CultureInvariant);

    private static readonly string[] IcyPhrases = ["eisigen Stelle", "glatten Untergrund"];
    private static readonly string[] WetPhrases = ["nassen Rasen", "glitschigen Ball", "nasse Leder", "nassen Ball", "Fußballschuhe"];
    private static readonly string[] FloodPhrases = ["Nachthimmel", "vom Flutlicht", "durch das Flutlicht", "Flutlicht fällt", "Flutlicht wird"];
    private static readonly string[] ThunderPhrases = ["Donnergrollen", "Gewitter"];
    private static readonly string[] EasterPhrases =
    [
        "Karim", "Marcel Kruse", "Pothmann", "Geburtstag", "Roooooooolf", "Jörg Rohrer",
        "Noam Salomon", "Rolf Langenberg", "Gladbach-Fahne", "Kairo-Fahne", "Happy Birthday",
    ];

    [Fact]
    public void AssetKeepsCp1252UmlautsAndTheFloodlightTypoIsGone()
    {
        var dir = Path.Combine(TestData.RepoRoot(), "data", "textmode");
        var imported = TextmImporter.Load(dir);
        var loaded = SceneAssetStore.Read(Path.Combine(dir, "scene-tree.json"));
        Assert.Equal(imported.Line(8618), loaded.Line(8618));
        Assert.Contains("Flutlicht wird schwächer", loaded.Line(8618), StringComparison.Ordinal);
        Assert.DoesNotContain(loaded.Lines, l => l is not null && (l.Contains('\uFFFD') || l.Contains("Flutlich wird", StringComparison.Ordinal)));
        Assert.Contains(loaded.Lines, l => l is not null && l.Contains('ä'));
        Assert.Contains(loaded.Lines, l => l is not null && l.Contains('ö'));
        Assert.Contains(loaded.Lines, l => l is not null && l.Contains('ü'));
        Assert.Contains(loaded.Lines, l => l is not null && l.Contains('ß'));
        Assert.False(SceneWeightOptions.Default.EasterEggs);
    }

    [Fact]
    public void MissingSecondStrikerDoesNotRepeatTheSameName()
    {
        var player = TestData.NewSession(1).World.Players.Values.First();
        var text = SceneNarrative.Apply("1-ST passt auf 1-ST1, REF pfeift.", new SceneNarrator
        {
            Find = (_, _, index) => index == 0 ? new MatchPlayer { Player = player } : null,
            Referee = "Krüger",
        });
        Assert.Equal($"{player.LastName} passt auf ein weiterer Spieler, Krüger pfeift.", text);
        Assert.DoesNotMatch(Placeholder, text);
        Assert.True(SceneNarrative.IsGoalCheer("Tor!"));
        Assert.True(SceneNarrative.IsGoalCheer("und 1-ST hat keine Mühe. Tor!"));
        Assert.False(SceneNarrative.IsGoalCheer("über das Tor!"));
        Assert.False(SceneNarrative.IsGoalCheer("Kein Tor!"));
        Assert.False(SceneNarrative.IsGoalCheer("Knapp neben das Tor!"));
    }

    [Fact]
    public void NarrativesMatchTheScoreSheetAndTheSameSeedRepeats()
    {
        var cases = new (Weather Weather, bool Flood, bool Icy, bool Wet, bool Thunder, bool FloodText)[]
        {
            (Weather.Sunny, false, false, false, false, false),
            (Weather.Cloudy, false, false, false, false, false),
            (Weather.Rain, false, false, true, true, false),
            (Weather.Snow, true, true, true, false, true),
            (Weather.Wind, false, false, false, true, false),
            (Weather.Sunny, true, false, false, false, true),
        };
        foreach (var (weather, flood, icy, wet, thunder, floodText) in cases)
        {
            for (var n = 0; n < 3; n++)
            {
                var scene = Play(20 + n * 17, weather, flood);
                AssertConsistent(scene);
                var lines = Lines(scene);
                AssertSurface(lines, allowEaster: false);
                AssertPhrases(lines, IcyPhrases, icy, weather);
                AssertPhrases(lines, WetPhrases, wet, weather);
                AssertPhrases(lines, ThunderPhrases, thunder, weather);
                AssertPhrases(lines, FloodPhrases, floodText, weather);
                Assert.DoesNotContain(scene.RareTags, t => t is "karim" or "marcel" or "easter");
            }
        }

        var setup = SetupFor(9, Weather.Rain, floodlit: true);
        var first = (SceneSimulation)new SceneMatchEngine().Create(setup);
        var second = (SceneSimulation)new SceneMatchEngine().Create(setup);
        first.RunToEnd();
        second.RunToEnd();
        Assert.Equal(Signature(first), Signature(second));
    }

    [Fact]
    public void RareInterruptionsReachTheirTextsAndExtendStoppage()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 24; i++)
            foreach (var tag in Play(5100 + i, Weather.Rain, true, new SceneWeightOptions { RarePerMatch = 8, EasterEggs = false }).RareTags)
                seen.Add(tag);
        var floodHits = 0;
        var leakChecked = 0;
        for (var i = 0; i < 40; i++)
        {
            var month = (i % 12) + 1;
            var flood = month is 11 or 12 or 1 or 2 || i % 2 == 0;
            var weather = (i % 5) switch
            {
                0 => Weather.Sunny,
                1 => Weather.Rain,
                2 => Weather.Snow,
                3 => Weather.Wind,
                _ => Weather.Cloudy,
            };
            var scene = Play(400 + i, weather, flood, new SceneWeightOptions { RarePerMatch = 6, EasterEggs = false });
            foreach (var tag in scene.RareTags) seen.Add(tag);
            AssertSurface(Lines(scene), allowEaster: false);
            foreach (var e in scene.Events)
            {
                if (e.Narrative is null) continue;
                if (e.Narrative.Contains("Donner", StringComparison.Ordinal) || e.Narrative.Contains("Gewitter", StringComparison.Ordinal))
                    Assert.True(weather is Weather.Rain or Weather.Wind, e.Narrative);
                if (e.Narrative.Contains("Fußballschuhe", StringComparison.Ordinal))
                    Assert.True(weather is Weather.Rain or Weather.Snow, e.Narrative);
                if (e.Narrative.Contains("Flutlicht", StringComparison.Ordinal))
                    Assert.True(flood, e.Narrative);
            }
            foreach (var e in scene.Events.Where(e => IsFloodlightFailure(e.Narrative)))
            {
                floodHits++;
                var end = PeriodEnd(e.Minute);
                var stop = scene.Events.Last(s => s.Type == MatchEventType.Stoppage && s.Minute == end);
                Assert.True(stop.Value >= 6, $"Flutlicht in Minute {e.Minute} brachte nur {stop.Value} Minuten Nachspielzeit.");
                if (e.Minute <= 45 && !scene.Events.Any(x => x.Minute > 45 && x.Minute <= 90 && IsRareLine(x.Narrative)))
                {
                    leakChecked++;
                    var second = scene.Events.Single(s => s.Type == MatchEventType.Stoppage && s.Minute == 90);
                    Assert.InRange(second.Value, 1, 3);
                }
            }
        }
        var missing = SceneRare.RequiredTags.Where(t => !seen.Contains(t)).ToList();
        Assert.True(missing.Count == 0, "Seltene Texte fehlen: " + string.Join(", ", missing));
        Assert.True(floodHits > 0, "Kein Flutlichtausfall in der Stichprobe.");
        _ = leakChecked;

        var quiet = Play(7, Weather.Sunny, false, new SceneWeightOptions { RarePerMatch = 0, EasterEggs = false });
        Assert.Empty(quiet.RareTags);
        Assert.All(quiet.Events.Where(e => e.Type == MatchEventType.Stoppage), e => Assert.InRange(e.Value, 1, 3));
    }

    [Fact]
    public void EasterEggContinuationsStayOffUnlessAsked()
    {
        var plain = new HashSet<string>();
        for (var i = 0; i < 15; i++)
            foreach (var line in Lines(Play(80 + i, Weather.Rain, true, new SceneWeightOptions { RarePerMatch = 4, EasterEggs = false })))
                plain.Add(line);
        Assert.DoesNotContain(plain, line => EasterPhrases.Any(p => line.Contains(p, StringComparison.Ordinal)));

        var eggs = new HashSet<string>();
        for (var i = 0; i < 25; i++)
            foreach (var line in Lines(Play(900 + i, Weather.Rain, true, new SceneWeightOptions { RarePerMatch = 5, EasterEggs = true })))
                eggs.Add(line);
        Assert.Contains(eggs, line => line.Contains("Happy Birthday", StringComparison.Ordinal));
        Assert.Contains(eggs, line => line.Contains("Kairo-Fahne", StringComparison.Ordinal));
        Assert.Contains(eggs, line => line.Contains("Geburtstag, Gerald", StringComparison.Ordinal));
        Assert.Contains(eggs, line => line.Contains("Megaphon", StringComparison.Ordinal) && line.Contains("singt:", StringComparison.Ordinal));
    }

    [Fact]
    public void ExampleProtocolHasNoBrokenLines()
    {
        var scene = PlayObserved(4711);
        var protocol = Protocol(scene);
        var path = Path.Combine(TestData.RepoRoot(), "docs", "textmodus-beispiel.txt");
        if (Environment.GetEnvironmentVariable("FMH_WRITE_PROTOCOL") == "1")
            File.WriteAllText(path, protocol);
        AssertConsistent(scene);
        AssertSurface(Lines(scene), allowEaster: false);
        Assert.True(File.Exists(path), "Beispielprotokoll fehlt: docs/textmodus-beispiel.txt");
        var stored = File.ReadAllText(path);
        AssertSurface(ProtocolBodies(stored), allowEaster: false);
        Assert.Contains("ß", stored);
    }

    internal static void AssertSurface(IEnumerable<string> lines, bool allowEaster)
    {
        var list = lines.ToList();
        Assert.NotEmpty(list);
        foreach (var line in list)
        {
            Assert.False(string.IsNullOrWhiteSpace(line), "Leere Zeile.");
            Assert.False(line.TrimEnd().EndsWith(':'), "Hängender Satz: " + line);
            Assert.False(Placeholder.IsMatch(line), "Platzhalter offen: " + line);
            Assert.DoesNotContain('\uFFFD', line);
            Assert.DoesNotContain("Flutlich wird", line, StringComparison.Ordinal);
            if (!allowEaster)
                Assert.DoesNotContain(EasterPhrases, p => line.Contains(p, StringComparison.Ordinal));
        }
    }

    private static void AssertConsistent(SceneSimulation scene)
    {
        var problems = new List<string>();
        var pitch = new HashSet<int>();
        foreach (var p in scene.Home.Participants.Where(p => p.Started)) pitch.Add(p.Player.Id);
        foreach (var p in scene.Away.Participants.Where(p => p.Started)) pitch.Add(p.Player.Id);
        var sideOf = scene.Home.Participants.Select(p => (p.Player.Id, Side.Home))
            .Concat(scene.Away.Participants.Select(p => (p.Player.Id, Side.Away)))
            .ToDictionary(x => x.Id, x => x.Item2);
        var byName = scene.Home.Participants.Concat(scene.Away.Participants)
            .Where(p => p.Player.LastName.Length >= 3)
            .GroupBy(p => p.Player.LastName)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Player.Id).ToHashSet());
        var names = byName.Keys.OrderByDescending(n => n.Length).ToList();

        var home = 0;
        var away = 0;
        foreach (var e in scene.Events)
        {
            if (e.Type == MatchEventType.Goal)
            {
                if (e.Side == Side.Home) home++;
                else away++;
                if (e.HomeScore != home || e.AwayScore != away)
                    problems.Add($"Spielstand {e.HomeScore}:{e.AwayScore} bei Tor {home}:{away} ({e.Minute}').");
                if (e.PlayerId is not { } scorer || !pitch.Contains(scorer))
                    problems.Add($"Torschütze nicht auf dem Platz ({e.Minute}', {e.Narrative}).");
                else if (e.Value == 1)
                {
                    if (sideOf[scorer] == e.Side) problems.Add("Eigentor dem falschen Team gutgeschrieben.");
                }
                else if (sideOf[scorer] != e.Side)
                    problems.Add($"Torschütze gehört nicht zur angreifenden Mannschaft ({e.Narrative}).");
            }
            else if (e.HomeScore != home || e.AwayScore != away)
                problems.Add($"Spielstand driftet bei {e.Type} {e.Minute}': Ereignis {e.HomeScore}:{e.AwayScore}, gezählt {home}:{away}.");

            if (e.Narrative is { } text)
            {
                if (Placeholder.IsMatch(text)) problems.Add("Platzhalter: " + text);
                if (string.IsNullOrWhiteSpace(text) || text.TrimEnd().EndsWith(':')) problems.Add("Kaputte Zeile: " + text);
                if (SceneNarrative.IsGoalCheer(text) && e.Type != MatchEventType.Goal)
                    problems.Add($"Torjubel ohne Tor ({e.Type}, {e.Minute}'): {text}");
                foreach (var name in names)
                {
                    if (!Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b")) continue;
                    var ids = byName[name];
                    var allowed = ids.Any(id => pitch.Contains(id) || id == e.PlayerId || id == e.OtherPlayerId);
                    if (!allowed) problems.Add($"„{name}“ ist nicht auf dem Platz ({e.Minute}', {e.Type}): {text}");
                }
            }

            if (e.Type == MatchEventType.Substitution && e.PlayerId is { } inn && e.OtherPlayerId is { } outId)
            {
                pitch.Remove(outId);
                pitch.Add(inn);
            }
            else if (e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow && e.PlayerId is { } sent)
                pitch.Remove(sent);
            else if (e.Type == MatchEventType.Injury && e.PlayerId is { } hurt)
                pitch.Remove(hurt);
        }

        if (scene.Home.Goals != home || scene.Away.Goals != away)
            problems.Add($"Endstand {scene.Home.Goals}:{scene.Away.Goals}, Tore im Strom {home}:{away}.");
        var shots = scene.Events.Count(e => e.Type is MatchEventType.Shot or MatchEventType.Goal);
        var on = scene.Events.Count(e => e.Type == MatchEventType.Goal || e.Type == MatchEventType.Shot && e.Outcome == ShotOutcome.Saved);
        var statShots = scene.Home.Stats.Shots + scene.Away.Stats.Shots;
        var statOn = scene.Home.Stats.ShotsOnTarget + scene.Away.Stats.ShotsOnTarget;
        if (shots != statShots) problems.Add($"Schüsse Statistik {statShots}, Ereignisse {shots}.");
        if (on != statOn) problems.Add($"Aufs Tor Statistik {statOn}, Ereignisse {on}.");
        Compare("Ecken", scene.Home.Stats.Corners + scene.Away.Stats.Corners, scene.Events.Count(e => e.Type == MatchEventType.Corner), problems);
        Compare("Fouls", scene.Home.Stats.Fouls + scene.Away.Stats.Fouls, scene.Events.Count(e => e.Type == MatchEventType.Foul), problems);
        Compare("Abseits", scene.Home.Stats.Offsides + scene.Away.Stats.Offsides, scene.Events.Count(e => e.Type == MatchEventType.Offside), problems);
        Compare("Gelb", scene.Home.Stats.YellowCards + scene.Away.Stats.YellowCards, scene.Events.Count(e => e.Type == MatchEventType.YellowCard), problems);
        Compare("Rot", scene.Home.Stats.RedCards + scene.Away.Stats.RedCards, scene.Events.Count(e => e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow), problems);
        Compare("Verletzungen", scene.Home.Stats.Injuries + scene.Away.Stats.Injuries, scene.Events.Count(e => e.Type == MatchEventType.Injury), problems);
        var xgEvents = scene.Events.Where(e => e.Type is MatchEventType.Shot or MatchEventType.Goal).Sum(e => e.Xg);
        var xgStats = scene.Home.Stats.Xg + scene.Away.Stats.Xg;
        if (Math.Abs(xgEvents - xgStats) > 0.05) problems.Add($"xG Statistik {xgStats:0.000}, Ereignisse {xgEvents:0.000}.");
        var playerGoals = scene.Home.Participants.Sum(p => p.Goals) + scene.Away.Participants.Sum(p => p.Goals);
        var own = scene.Events.Count(e => e.Type == MatchEventType.Goal && e.Value == 1);
        if (playerGoals + own != home + away) problems.Add($"Spielertore {playerGoals} + Eigentore {own} != {home + away}.");

        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(8)));
    }

    private static void Compare(string label, int stats, int events, List<string> problems)
    {
        if (stats != events) problems.Add($"{label}: Statistik {stats}, Ereignisse {events}.");
    }

    private static void AssertPhrases(IReadOnlyList<string> lines, IReadOnlyList<string> phrases, bool allowed, Weather weather)
    {
        foreach (var line in lines)
        foreach (var phrase in phrases)
            if (line.Contains(phrase, StringComparison.Ordinal))
                Assert.True(allowed, $"{weather}: „{phrase}“ in „{line}“");
    }

    private static bool IsFloodlightFailure(string? text) =>
        text is not null && (text.Contains("Flutlicht fällt", StringComparison.Ordinal) || text.Contains("Flutlicht wird", StringComparison.Ordinal));

    private static bool IsRareLine(string? text) =>
        text is not null && (text.Contains("unterbrochen", StringComparison.Ordinal)
                             || text.Contains("Donner", StringComparison.Ordinal)
                             || text.Contains("Gewitter", StringComparison.Ordinal)
                             || text.Contains("Flutlicht", StringComparison.Ordinal)
                             || text.Contains("Hubschrauber", StringComparison.Ordinal)
                             || text.Contains("Megaphon", StringComparison.Ordinal)
                             || text.Contains("Riesenbanner", StringComparison.Ordinal)
                             || text.Contains("Fußballschuhe", StringComparison.Ordinal)
                             || text.Contains("Elster", StringComparison.Ordinal)
                             || text.Contains("Tiefflieger", StringComparison.Ordinal)
                             || text.Contains("Vogelschwarm", StringComparison.Ordinal)
                             || text.Contains("zerrissen", StringComparison.Ordinal));

    private static int PeriodEnd(int minute) => minute <= 45 ? 45 : minute <= 90 ? 90 : minute <= 105 ? 105 : 120;

    private static SceneSimulation Play(int seed, Weather weather, bool floodlit, SceneWeightOptions? options = null) =>
        Run(SetupFor(seed, weather, floodlit), options);

    internal static SceneSimulation PlayObserved(int seed)
    {
        var session = TestData.NewSession((ulong)seed);
        var fixture = session.World.AllFixtures.First(f => f.Result is null && f.Involves(session.HumanClub.Id));
        var setup = session.StartMatch(fixture, humanControlled: true).Setup;
        return Run(setup, new SceneWeightOptions());
    }

    private static SceneSimulation Run(MatchSetup setup, SceneWeightOptions? options)
    {
        var scene = (SceneSimulation)new SceneMatchEngine(options: options).Create(setup);
        scene.RunToEnd();
        return scene;
    }

    private static MatchSetup SetupFor(int seed, Weather weather, bool floodlit)
    {
        var session = TestData.NewSession((ulong)seed);
        var world = session.World;
        var league = world.Leagues.First(l => l.ClubIds.Count >= 2);
        var fixture = new Fixture
        {
            Id = 4_000_000 + seed,
            CompetitionId = league.Id,
            Kind = CompetitionKind.League,
            HomeId = league.ClubIds[0],
            AwayId = league.ClubIds[1],
            Date = new DateOnly(world.SeasonYear, 6, 12),
            RefereeId = world.Referees[seed % world.Referees.Count].Id,
        };
        var setup = session.StartMatch(fixture, humanControlled: false).Setup;
        setup.Weather = weather;
        setup.Floodlit = floodlit;
        return setup;
    }

    private static List<string> Lines(SceneSimulation scene) =>
        scene.Events.Select(e => e.Narrative).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();

    private static string Signature(SceneSimulation scene) =>
        string.Join("\n", scene.Events.Select(e => $"{e.Minute}+{e.Stoppage}|{e.Type}|{e.Side}|{e.PlayerId}|{e.OtherPlayerId}|{e.HomeScore}:{e.AwayScore}|{e.Value}|{e.Xg}|{e.Outcome}|{e.Narrative}"));

    /// <summary>Zeilen des Beispielprotokolls. Git auf Windows checkt die Datei mit CR LF aus.</summary>
    private static IEnumerable<string> ProtocolBodies(string stored) =>
        stored.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Contains('|', StringComparison.Ordinal) ? l[(l.IndexOf('|') + 1)..].Trim() : l)
            .Where(l => l.Length > 0);

    internal static string Protocol(SceneSimulation scene)
    {
        var home = scene.Setup.Home.Club.Name;
        var away = scene.Setup.Away.Club.Name;
        var lines = new List<string>
        {
            $"{home} – {away}",
            $"Schiedsrichter: {scene.Setup.RefereeName ?? "–"}",
            $"Wetter: {scene.Setup.Weather}, Flutlicht: {(scene.Setup.Floodlit ? "ja" : "nein")}",
            "",
        };
        foreach (var e in scene.Events)
        {
            if (string.IsNullOrWhiteSpace(e.Narrative)) continue;
            lines.Add($"{e.MinuteLabel} | {e.Narrative}");
        }
        lines.Add("");
        lines.Add($"Endstand {scene.Home.Goals}:{scene.Away.Goals}");
        return string.Join("\n", lines) + "\n";
    }
}
