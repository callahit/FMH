using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 13c: Grauzonen (Anstoß-Element grayZones) – Wirkung, Aufdeckung, Strafen, Entlassung, Spielstand.</summary>
public sealed class GrayZonesTests
{
    private static GameData Load(double detection, int sackAfter = 3)
    {
        var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base", "anstoss" });
        foreach (var a in data.GrayZones!.Actions) a.Detection = detection;
        data.GrayZones.SackAfter = sackAfter;
        return data;
    }

    private static readonly Lazy<GameData> Never = new(() => Load(0));
    private static readonly Lazy<GameData> Always = new(() => Load(1));
    private static readonly Lazy<GameData> AlwaysSack = new(() => Load(1, sackAfter: 2));

    private static GameSession Session(GameData data, ulong seed)
    {
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = seed, ClubKey = "nordheide", ManagerName = "Test" });
        world.Managers[0].Profile.Events = EventFrequency.Off;
        return new GameSession(world, data, new TextMatchEngine());
    }

    /// <summary>Bis zum Spieltag des Menschen vorspulen und das Spiel zurückgeben (noch nicht gespielt).</summary>
    private static Fixture UntilMatch(GameSession s, int fixtureId)
    {
        for (var i = 0; i < 400; i++)
        {
            var r = s.Advance();
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f)
            {
                if (f.Id == fixtureId) return f;
                s.PlayInstant(f);
            }
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
        throw new InvalidOperationException("Spiel nicht erreicht.");
    }

    [Fact]
    public void RulesAndTextsAreComplete()
    {
        Assert.Null(TestData.Data.GrayZones);
        var data = Never.Value;
        Assert.Contains("grayZones", data.Anstoss.Elements.Select(e => e.Id));
        var ids = data.GrayZones!.Actions.Select(a => a.Id).ToList();
        Assert.Equal(new[] { "referee", "doping", "contractTrick" }, ids);
        foreach (var lang in data.Languages.Keys)
        {
            data.UseLanguage(lang);
            foreach (var id in ids)
                foreach (var part in new[] { "name", "info", "detected" })
                    Assert.True(data.Texts.Has($"gray.{id}.{part}"), $"{lang} gray.{id}.{part}");
        }
        data.UseLanguage("de");
    }

    [Fact]
    public void UndetectedBribeCostsMoneyAndLiftsTheTeamInTheNextMatch()
    {
        var data = Never.Value;
        var s = Session(data, 1321);
        var plain = Session(data, 1321);
        s.Advance(1);
        plain.Advance(1);
        var target = s.GrayZoneFixture!;
        var action = GrayZones.Find(data.GrayZones!, "referee")!;
        var balance = s.HumanClub.Budget.Balance;
        Assert.Null(s.UseGrayZone("referee"));
        Assert.Equal(balance - GrayZones.Scaled(action.Cost, s.HumanClub), s.HumanClub.Budget.Balance);
        Assert.Equal("gray.error.cooldown", s.UseGrayZone("doping"));
        Assert.Equal(target.Id, s.GrayZoneFile!.Deeds.Single().FixtureId);

        var f = UntilMatch(s, target.Id);
        var g = UntilMatch(plain, target.Id);
        var sim = s.StartMatch(f, humanControlled: false);
        var reference = plain.StartMatch(g, humanControlled: false);
        var own = f.HomeId == s.HumanClub.Id ? sim.Home : sim.Away;
        var ownRef = g.HomeId == plain.HumanClub.Id ? reference.Home : reference.Away;
        var form = own.OnPitch.ToDictionary(mp => mp.Player.Id, mp => mp.DayForm);
        Assert.All(ownRef.OnPitch, mp => Assert.Equal(mp.DayForm + action.DayForm, form[mp.Player.Id], 6));

        sim.RunToEnd();
        s.FinishMatch(f, sim);
        var deed = s.GrayZoneFile!.Deeds.Single();
        Assert.True(deed.Resolved);
        Assert.False(deed.Detected);
        Assert.DoesNotContain(s.World.Messages, m => m.SubjectRef?.Key == "gray.detected.subject");
    }

    [Fact]
    public void DetectedDopingBansPlayersAndFines()
    {
        var data = Always.Value;
        var s = Session(data, 1322);
        s.Advance(1);
        var target = s.GrayZoneFixture!;
        Assert.Null(s.UseGrayZone("doping"));
        var trust = s.HumanClub.Board.Trust;
        var media = s.HumanManager.Career.Media;
        var f = UntilMatch(s, target.Id);
        s.PlayInstant(f);

        var deed = s.GrayZoneFile!.Deeds.Single();
        Assert.True(deed.Detected);
        Assert.NotEmpty(deed.Players);
        var ban = GrayZones.Find(data.GrayZones!, "doping")!.Penalty.PlayerBan;
        Assert.All(deed.Players, id => Assert.True(s.World.Players[id].State.SuspendedMatches >= ban - 1));
        Assert.True(s.HumanClub.Board.Trust < trust);
        Assert.True(s.HumanManager.Career.Media < media);
        Assert.Contains(s.World.Messages, m => m.SubjectRef?.Key == "gray.detected.subject");
        Assert.Equal(1, s.GrayZoneFile.Detected);
    }

    /// <summary>Nach Entlassung oder Wechsel verfällt eine geplante Tat; der neue Verein wird nicht bestraft.</summary>
    [Fact]
    public void DeedOfAFormerClubLapsesWithoutPenalty()
    {
        var s = Session(Always.Value, 1322);
        s.Advance(1);
        var target = s.GrayZoneFixture!;
        Assert.Null(s.UseGrayZone("doping"));
        var deed = s.GrayZoneFile!.Deeds.Single();
        Assert.Equal(s.HumanClub.Id, deed.ClubId);
        // Die Tat stammt aus der Zeit bei einem anderen Verein.
        deed.ClubId = s.World.Clubs.Keys.First(id => id != s.HumanClub.Id);
        var f = UntilMatch(s, target.Id);
        s.PlayInstant(f);

        Assert.True(deed.Resolved);
        Assert.False(deed.Detected);
        Assert.Empty(deed.Players);
        Assert.Equal(0, s.GrayZoneFile.Detected);
        Assert.DoesNotContain(s.World.Messages, m => m.SubjectRef?.Key == "gray.detected.subject");
    }

    /// <summary>Ältere Taten ohne Verein (Spielstände vor dem Feld) gelten, wenn das Spiel den jetzigen Verein betrifft.</summary>
    [Fact]
    public void DeedWithoutClubFromAnOlderSaveStillCountsForTheOwnMatch()
    {
        var s = Session(Always.Value, 1322);
        s.Advance(1);
        var target = s.GrayZoneFixture!;
        Assert.Null(s.UseGrayZone("doping"));
        var deed = s.GrayZoneFile!.Deeds.Single();
        deed.ClubId = 0;
        s.PlayInstant(UntilMatch(s, target.Id));
        Assert.True(deed.Detected);
        Assert.Equal(1, s.GrayZoneFile.Detected);
    }

    [Fact]
    public void ContractTrickLowersTheDemandOrBackfires()
    {
        var player = (GameSession s) => s.World.Players.Values.First(p => p.ClubId is { } c && c != s.HumanClub.Id);
        var lucky = Session(Never.Value, 1323);
        var n = new Negotiation { PlayerId = player(lucky).Id, BuyerId = lucky.HumanClub.Id, WageDemand = 10_000 };
        Assert.Equal("gray.error.noNegotiation", lucky.UseGrayZone("contractTrick"));
        Assert.Null(lucky.UseGrayZone("contractTrick", n));
        Assert.Equal(8_500, n.WageDemand);

        var caught = Session(Always.Value, 1323);
        var p = player(caught);
        var morale = p.State.Morale;
        var m = new Negotiation { PlayerId = p.Id, BuyerId = caught.HumanClub.Id, WageDemand = 10_000 };
        var balance = caught.HumanClub.Budget.Balance;
        Assert.Null(caught.UseGrayZone("contractTrick", m));
        Assert.Equal(10_000, m.WageDemand);
        Assert.True(p.State.Morale < morale || morale == Morale.VeryBad);
        Assert.True(caught.HumanClub.Budget.Balance < balance);
    }

    [Fact]
    public void RepeatedExposureEndsInDismissal()
    {
        var s = Session(AlwaysSack.Value, 1324);
        var n = (GameSession x) => new Negotiation { PlayerId = x.World.Players.Values.First(p => p.ClubId != x.HumanClub.Id).Id, BuyerId = x.HumanClub.Id, WageDemand = 5_000 };
        Assert.Null(s.UseGrayZone("contractTrick", n(s)));
        Assert.False(s.AwaitingNewJob);
        Assert.Equal("gray.error.cooldown", s.UseGrayZone("contractTrick", n(s)));
        TestData.PlayUntil(s, _ => s.World.Date.DayNumber - s.GrayZoneFile!.Deeds[0].Date.DayNumber >= 14, 5_000);
        Assert.Null(s.UseGrayZone("contractTrick", n(s)));
        Assert.True(s.AwaitingNewJob);
        Assert.Equal("gray.error.off", s.UseGrayZone("referee"));
    }

    [Fact]
    public void ElementOffBlocksDeedsAndSavingAndMigrationWork()
    {
        var s = Session(Never.Value, 1325);
        s.SetAnstossElement("grayZones", false);
        Assert.False(s.GrayZonesOn);
        Assert.Empty(s.GrayActions);
        Assert.Equal("gray.error.off", s.UseGrayZone("referee"));
        s.SetAnstossElement("grayZones", true);
        Assert.Null(s.UseGrayZone("referee"));

        var path = Path.Combine(Path.GetTempPath(), $"fmh-gray-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var deed = loaded.GrayZones[s.HumanManager.Id].Deeds.Single();
            Assert.Equal(("referee", s.GrayZoneFixture!.Id), (deed.Action, deed.FixtureId!.Value));
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.grayZones"]);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE gray_zones; DELETE FROM meta WHERE key = 'schema.grayZones';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Empty(old.GrayZones);
            var resumed = new GameSession(old, Never.Value, new TextMatchEngine());
            Assert.Null(resumed.UseGrayZone("referee"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
