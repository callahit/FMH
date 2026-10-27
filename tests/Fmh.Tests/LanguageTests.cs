using System.Text.RegularExpressions;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>Sprachwechsel verändert globale Formate (Fmt); diese Tests laufen deshalb nicht parallel zu anderen.</summary>
[CollectionDefinition("Sprache", DisableParallelization = true)]
public sealed class LanguageCollection;

/// <summary>Mehrsprachigkeit (Kapitel 12): vollständige Sprachdateien, sprachneutrale Texte, Formate, Sprachwechsel.</summary>
[Collection("Sprache")]
public class LanguageTests
{
    private static readonly Regex Placeholder = new(@"\{\d+\}");

    /// <summary>Eigene Datenkopie, damit der Sprachwechsel die gemeinsamen Testdaten nicht berührt.</summary>
    private static GameData Fresh() => PackageLoader.Load(PackageLoader.FindDataRoot());

    [Fact]
    public void GermanAndEnglishAreAvailable()
    {
        var data = TestData.Data;
        Assert.Equal("de", data.DefaultLanguage);
        Assert.Contains("de", data.Languages.Keys);
        Assert.Contains("en", data.Languages.Keys);
        Assert.Equal("Deutsch", data.Languages["de"].Name);
        Assert.Equal("English", data.Languages["en"].Name);
    }

    /// <summary>Jede Sprache enthält alle Schlüssel der Standardsprache mit denselben Platzhaltern.</summary>
    [Fact]
    public void EveryLanguageIsCompleteAndPlaceholdersMatch()
    {
        var data = TestData.Data;
        var reference = data.Languages[data.DefaultLanguage].Texts.Entries;
        foreach (var (code, pack) in data.Languages.Where(l => l.Key != data.DefaultLanguage))
        {
            var entries = pack.Texts.Entries;
            var missing = reference.Keys.Where(k => !entries.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{code}: fehlende Texte: {string.Join(", ", missing)}");
            var extra = entries.Keys.Where(k => !reference.ContainsKey(k)).ToList();
            Assert.True(extra.Count == 0, $"{code}: unbekannte Schlüssel: {string.Join(", ", extra)}");
            foreach (var (key, text) in reference)
            {
                var a = Placeholder.Matches(text).Select(m => m.Value).ToHashSet();
                var b = Placeholder.Matches(entries[key]).Select(m => m.Value).ToHashSet();
                Assert.True(a.SetEquals(b), $"{code}/{key}: Platzhalter {string.Join(" ", a)} ≠ {string.Join(" ", b)}");
            }
        }
    }

    [Fact]
    public void EveryLanguageHasOwnCommentaryForAllEvents()
    {
        var data = TestData.Data;
        var reference = data.Languages[data.DefaultLanguage].Commentary;
        foreach (var (code, pack) in data.Languages)
        {
            foreach (var key in reference.Keys)
            {
                Assert.True(pack.Commentary.TryGetValue(key, out var lines) && lines.Count > 0, $"{code}: Kommentar fehlt: {key}");
                if (code != data.DefaultLanguage)
                    Assert.False(lines!.SequenceEqual(reference[key]), $"{code}: Kommentar '{key}' ist nicht übersetzt");
            }
        }
    }

    [Fact]
    public void FormatsFollowTheLanguage()
    {
        var data = Fresh();
        try
        {
            data.UseLanguage("de");
            Assert.Equal("4,21 Mio. EUR", Fmt.Money(4_210_000, "EUR"));
            Assert.Equal("350 Tsd. EUR", Fmt.Money(350_000, "EUR"));
            Assert.Equal("1.234.567", Fmt.Int(1_234_567));
            Assert.Equal("01.07.2026", Fmt.Date(new DateOnly(2026, 7, 1)));
            Assert.Equal("Mi", Fmt.Weekday(new DateOnly(2026, 7, 1)));

            data.UseLanguage("en");
            Assert.Equal("EUR 4.21m", Fmt.Money(4_210_000, "EUR"));
            Assert.Equal("EUR 350k", Fmt.Money(350_000, "EUR"));
            Assert.Equal("1,234,567", Fmt.Int(1_234_567));
            Assert.Equal("01/07/2026", Fmt.Date(new DateOnly(2026, 7, 1)));
            Assert.Equal("Wed", Fmt.Weekday(new DateOnly(2026, 7, 1)));
            Assert.Equal("2.5", Fmt.Num(2.5));
        }
        finally
        {
            data.UseLanguage("de");
        }
    }

    [Fact]
    public void TextRefsRenderInTheCurrentLanguage()
    {
        var data = Fresh();
        try
        {
            var r = TextRef.Of("msg.injury.body", "Müller", TextRef.Of("injury.acl"), 180);
            data.UseLanguage("de");
            Assert.Equal("Müller hat sich im Spiel verletzt. Diagnose: Kreuzbandriss. Voraussichtliche Ausfallzeit: 180 Tage.", data.Texts.Render(r));
            data.UseLanguage("en");
            Assert.Equal("Müller was injured in the match. Diagnosis: Torn cruciate ligament. Expected time out: 180 days.", data.Texts.Render(r));

            var money = TextRef.Of("neg.club.counter", TextArg.Money(2_500_000, "EUR"));
            Assert.Equal("The club demands EUR 2.50m.", data.Texts.Render(money));
            var comp = TextRef.Of("msg.cup.out.body", TextArg.Key("competition.DE-CUP", "Pokal"), TextArg.Key("cup.round.n"), "1:2");
            Assert.Contains("The Cup is over for us.", data.Texts.Render(comp));
            // Unbekannter Schlüssel mit Rückfallwert aus den Daten.
            Assert.Equal("Kreisliga", data.Texts.Render(TextArg.Key("competition.XYZ", "Kreisliga")));
            // Wörtliche Texte (alte Spielstände) bleiben unverändert.
            Assert.Equal("Alter Text", data.Texts.Render(TextRef.Literal("Alter Text")));
        }
        finally
        {
            data.UseLanguage("de");
        }
    }

    [Fact]
    public void UnknownLanguageFallsBackToDefault()
    {
        var data = Fresh();
        data.UseLanguage("xx");
        Assert.Equal("de", data.Language);
    }

    /// <summary>Ein Spielstand ist sprachneutral: Nachrichten erscheinen nach dem Laden in der jeweils gewählten Sprache.</summary>
    [Fact]
    public void SavedMessagesAreTranslatedAfterSwitchingLanguage()
    {
        var data = Fresh();
        var dir = Path.Combine(Path.GetTempPath(), "fmh-lang-" + Guid.NewGuid());
        try
        {
            data.UseLanguage("de");
            var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 7, ClubKey = "nordheide", ManagerName = "Test" });
            var s = new GameSession(world, data, new TextMatchEngine());
            for (var i = 0; i < 5; i++)
            {
                var r = s.Advance();
                if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
                if (r.Reason == StopReason.Decision) s.DelegateDecisions();
            }
            Assert.True(s.Inbox.Count() >= 3);
            var path = Path.Combine(dir, "lang.fmh");
            SqliteStore.Save(s.World, path);

            var loaded = SqliteStore.Load(path);
            var original = s.Inbox.Select(m => m.SubjectText(data.Texts)).ToList();
            var reloaded = loaded.Messages.OrderByDescending(m => m.Id).Select(m => m.SubjectText(data.Texts)).ToList();
            Assert.Equal(original, reloaded);

            data.UseLanguage("en");
            var english = loaded.Messages.OrderByDescending(m => m.Id).Select(m => m.SubjectText(data.Texts)).ToList();
            Assert.NotEqual(original, english);
            Assert.Contains(english, t => t.StartsWith("Welcome to", StringComparison.Ordinal));
            Assert.Contains(loaded.Messages, m => m.SenderText(data.Texts) == "Board");
            Assert.DoesNotContain(english.Concat(loaded.Messages.Select(m => m.BodyText(data.Texts))), t => t.Contains('[') && t.Contains(']'));
        }
        finally
        {
            data.UseLanguage("de");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MatchCommentaryUsesTheSelectedLanguage()
    {
        var data = Fresh();
        try
        {
            data.UseLanguage("en");
            var world = WorldGenerator.Create(data, new NewGameOptions { Seed = 3, ClubKey = "nordheide", ManagerName = "Test" });
            var s = new GameSession(world, data, new TextMatchEngine());
            var f = s.World.Leagues[0].Fixtures[0];
            var sim = s.StartMatch(f, humanControlled: false);
            sim.RunToEnd();
            var c = new CommentaryGenerator(data, sim);
            var lines = sim.Events.Select(e => c.Describe(e, tickerOnly: false)).Where(l => l is not null).Select(l => l!.Text).ToList();
            Assert.Contains(lines, l => l.StartsWith("Kick-off!", StringComparison.Ordinal) || l.StartsWith("Here we go!", StringComparison.Ordinal)
                                        || l.StartsWith("The referee gets us underway", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("Anpfiff", StringComparison.Ordinal));
        }
        finally
        {
            data.UseLanguage("de");
        }
    }
}
