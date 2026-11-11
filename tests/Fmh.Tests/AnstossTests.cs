using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 13a: Anstoß-Paket, Haupt- und Einzelschalter, Speicherung und Abnahme „Hauptschalter aus = ohne Paket“.</summary>
public sealed class AnstossTests
{
    private static readonly Lazy<GameData> WithPackage = new(() =>
        PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base", "anstoss" }));

    private static GameSession Session(GameData data, ulong seed, Action<World>? setup = null)
    {
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = seed, ClubKey = "nordheide", ManagerName = "Test" });
        world.Managers[0].Profile.Events = EventFrequency.Often;
        setup?.Invoke(world);
        return new GameSession(world, data, new TextMatchEngine());
    }

    /// <summary>Alle passenden Ereignisse eines Tages nacheinander ziehen (Abklingzeit verhindert Wiederholungen).</summary>
    private static HashSet<string> DrawAll(GameSession s)
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < 400; i++)
            if (s.TriggerRandomEvent() is { } e) ids.Add(e.EventId);
        return ids;
    }

    [Fact]
    public void PackageDataIsCompleteInEveryLanguage()
    {
        var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base", "anstoss" });
        var elements = data.Anstoss.Elements.Select(e => e.Id).ToList();
        Assert.Equal(new[] { "satire", "teamNights", "fanActions", "curiosities" }, elements);
        var tagged = data.Events.Where(e => e.Feature is not null).ToList();
        Assert.All(tagged, e => Assert.Contains(Anstoss.Element(e.Feature!), elements));
        Assert.All(elements, el => Assert.True(tagged.Count(e => Anstoss.Element(e.Feature!) == el) >= 5, el));
        Assert.DoesNotContain(TestData.Data.Events, e => e.Feature is not null);
        foreach (var lang in data.Languages.Keys)
        {
            data.UseLanguage(lang);
            foreach (var el in elements)
                Assert.True(data.Texts.Has($"anstoss.{el}.name") && data.Texts.Has($"anstoss.{el}.info"), lang + " " + el);
            foreach (var e in tagged)
            {
                Assert.True(data.Texts.Has($"event.{e.Id}.title") && data.Texts.Has($"event.{e.Id}.text"), lang + " " + e.Id);
                Assert.All(e.Options, o => Assert.True(data.Texts.Has($"event.{e.Id}.opt.{o.Id}"), lang + " " + e.Id + "." + o.Id));
            }
        }
    }

    [Fact]
    public void MasterAndElementSwitchesDecideWhichEventsFire()
    {
        var data = WithPackage.Value;
        string? El(string id) => data.Events.Single(e => e.Id == id).Feature is { } f ? Anstoss.Element(f) : null;

        var on = DrawAll(Session(data, 1301)).Select(El).Where(f => f is not null).ToHashSet();
        Assert.True(on.Count >= 3, string.Join(",", on));

        var noSatire = DrawAll(Session(data, 1301, w => Anstoss.SetElement(w, w.Managers[0].Id, "satire", false))).Select(El).ToList();
        Assert.DoesNotContain("satire", noSatire);
        Assert.Contains(noSatire, f => f is not null);

        var off = DrawAll(Session(data, 1301, w => Anstoss.SetMaster(w, w.Managers[0].Id, false)));
        Assert.All(off, id => Assert.Null(El(id)));
    }

    [Fact]
    public void SwitchingOffInARunningGameDropsOpenEventsAndSurvivesSaving()
    {
        var s = Session(WithPackage.Value, 1302);
        Assert.True(s.AnstossOn);
        var pending = s.TriggerRandomEvent("ansSausageKit")!;
        Assert.Contains(s.World.Messages, m => m.Action == MessageAction.Event && m.ActionRefId == pending.Id);
        s.SetAnstossElement("satire", false);
        Assert.DoesNotContain(s.World.Events, e => e.Id == pending.Id);
        Assert.DoesNotContain(s.World.Messages, m => m.Action == MessageAction.Event && m.ActionRefId == pending.Id);
        Assert.False(s.AnstossElementOn("satire"));
        s.SetAnstoss(false);

        var path = Path.Combine(Path.GetTempPath(), $"fmh-anstoss-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var settings = loaded.Anstoss[s.HumanManager.Id];
            Assert.Equal((false, "satire"), (settings.Enabled, settings.Disabled.Single()));
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.anstoss"]);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE anstoss_settings; DELETE FROM meta WHERE key = 'schema.anstoss';";
                cmd.ExecuteNonQuery();
            }
            // Älterer Spielstand: alles an.
            var old = SqliteStore.Load(path);
            Assert.Empty(old.Anstoss);
            Assert.True(Anstoss.IsOn(old, s.HumanManager.Id, "anstoss.satire"));
            new GameSession(old, WithPackage.Value, new TextMatchEngine()).Advance(2);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }

        // Wieder alles an: kein Eintrag mehr, der Spielstand sieht aus wie bei einem neuen Spiel.
        s.SetAnstoss(true);
        s.SetAnstossElement("satire", true);
        Assert.Empty(s.World.Anstoss);
    }

    /// <summary>Abnahme AP 13: Mit Hauptschalter aus läuft die Welt genau wie ohne das Paket.</summary>
    [Fact]
    public void MasterSwitchOffEqualsTheGameWithoutThePackage()
    {
        var without = Session(TestData.Data, 1303);
        var off = Session(WithPackage.Value, 1303, w => Anstoss.SetMaster(w, w.Managers[0].Id, false));
        var end = without.World.Date.AddDays(150);
        TestData.PlayUntil(without, _ => without.World.Date >= end, 50_000);
        TestData.PlayUntil(off, _ => off.World.Date >= end, 50_000);
        without.SyncRng();
        off.SyncRng();
        Assert.NotEmpty(without.World.EventHistory);
        var a = WorldFingerprint.Sections(without.World);
        var b = WorldFingerprint.Sections(off.World);
        foreach (var key in a.Keys.Except(new[] { "config", "anstoss" }))
            Assert.True(a[key] == b[key], "Abweichung im Bereich " + key);
    }
}
