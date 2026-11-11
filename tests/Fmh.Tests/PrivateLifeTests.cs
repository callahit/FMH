using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 13b: Privatleben des Managers (Anstoß-Element privateLife).</summary>
public sealed class PrivateLifeTests
{
    private static readonly Lazy<GameData> WithPackage = new(() =>
        PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base", "anstoss" }));

    private static GameSession Session(ulong seed, Action<World>? setup = null)
    {
        var data = WithPackage.Value;
        var world = WorldGenerator.Create(data, new NewGameOptions { Seed = seed, ClubKey = "nordheide", ManagerName = "Test" });
        world.Managers[0].Profile.Events = EventFrequency.Off;
        setup?.Invoke(world);
        return new GameSession(world, data, new TextMatchEngine());
    }

    [Fact]
    public void RulesAreCompleteAndStartWithFreeBasics()
    {
        var data = PackageLoader.Load(new[] { PackageLoader.FindDataRoot() }, enabledPackages: new[] { "base", "anstoss" });
        var rules = data.PrivateLife!;
        Assert.Null(TestData.Data.PrivateLife);
        Assert.Equal(0, rules.Homes.Min(h => h.Price));
        Assert.Equal(0, rules.Cars.Min(c => c.Price));
        Assert.Contains("privateLife", data.Anstoss.Elements.Select(e => e.Id));
        var items = rules.Homes.Concat(rules.Cars).Concat(rules.Hobbies).ToList();
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        foreach (var lang in data.Languages.Keys)
        {
            data.UseLanguage(lang);
            Assert.All(items, i => Assert.True(data.Texts.Has("private." + i.Id), lang + " " + i.Id));
        }
    }

    [Fact]
    public void ChangingHomeCarAndHobbiesFollowsTheRules()
    {
        var rules = WithPackage.Value.PrivateLife!;
        var life = PrivateLives.Create(rules, 10_000);
        Assert.Equal(200_000, life.Savings);
        Assert.Equal(("flat", "compact"), (life.Home, life.Car));
        var start = PrivateLives.MonthlyCost(rules, life);

        Assert.Equal("private.error.money", PrivateLives.Change(rules, life, PrivateLives.Kind.Home, "villa"));
        Assert.Equal("private.error.owned", PrivateLives.Change(rules, life, PrivateLives.Kind.Car, "compact"));
        Assert.Null(PrivateLives.Change(rules, life, PrivateLives.Kind.Car, "sportsCar"));
        Assert.Equal(40_000, life.Savings);
        // Umstieg: 60 % des alten Preises kommen zurück.
        var limo = rules.Cars.Single(c => c.Id == "limousine");
        Assert.Equal(limo.Price - (long)(160_000 * rules.ResaleShare), PrivateLives.NetPrice(rules, life, PrivateLives.Kind.Car, limo));
        Assert.Null(PrivateLives.Change(rules, life, PrivateLives.Kind.Car, "compact"));
        Assert.Equal(40_000 + 96_000, life.Savings);

        Assert.Null(PrivateLives.Change(rules, life, PrivateLives.Kind.Hobby, "fishing"));
        Assert.Null(PrivateLives.Change(rules, life, PrivateLives.Kind.Hobby, "golf"));
        Assert.Equal("private.error.hobbies", PrivateLives.Change(rules, life, PrivateLives.Kind.Hobby, "cards"));
        Assert.True(PrivateLives.Relief(rules, life) > 0.5);
        Assert.Null(PrivateLives.Change(rules, life, PrivateLives.Kind.Hobby, "fishing"));
        Assert.Equal(new[] { "golf" }, life.Hobbies);
        Assert.Equal(start + 900, PrivateLives.MonthlyCost(rules, life));
        Assert.Equal("private.error.unknown", PrivateLives.Change(rules, life, PrivateLives.Kind.Home, "castle"));
    }

    [Fact]
    public void SalaryCostsAndStressRunEveryDay()
    {
        var s = Session(1311);
        s.Advance(1);
        var life = s.PrivateLife!;
        var rules = WithPackage.Value.PrivateLife!;
        var wage = s.HumanClub.Board.ManagerWeeklyWage;
        Assert.Equal((long)Math.Round(wage * rules.StartSavingsWeeks), life.Savings);

        // Tag für Tag bis zur ersten Buchung: Gehalt minus laufende Kosten, genau einmal am Monatsersten.
        var savings = life.Savings;
        for (var i = 0; i < 40 && life.Savings == savings; i++)
        {
            var r = s.Advance(1);
            if (r.Reason == StopReason.HumanMatch && r.Fixture is { } f) s.PlayInstant(f);
            if (r.Reason == StopReason.Decision) s.DelegateDecisions();
        }
        Assert.Equal(savings + (long)Math.Round(wage * 52 / 12.0) - PrivateLives.MonthlyCost(rules, life), life.Savings);

        // Hoher Stress: Warnung am Montag, Vorstandsvertrauen sinkt jede Woche.
        life.Stress = 95;
        var trust = s.HumanClub.Board.Trust;
        TestData.PlayUntil(s, _ => s.World.Date.DayOfWeek == DayOfWeek.Tuesday && life.StressWarned, 5_000);
        Assert.Contains(s.World.Messages, m => m.SubjectRef?.Key == "private.stress.subject");
        Assert.InRange(life.Stress, 0, 100);
        Assert.True(s.HumanClub.Board.Trust < trust + 5);
    }

    [Fact]
    public void ElementOffMeansNoPrivateLifeAndSavingAndMigrationWork()
    {
        var off = Session(1312, w => Anstoss.SetElement(w, w.Managers[0].Id, "privateLife", false));
        off.Advance(3);
        Assert.Empty(off.World.PrivateLives);
        Assert.Null(off.PrivateLife);
        Assert.Equal("private.error.off", off.ChangePrivate(PrivateLives.Kind.Hobby, "golf"));

        var s = Session(1313);
        s.Advance(2);
        Assert.Null(s.ChangePrivate(PrivateLives.Kind.Hobby, "fishing"));
        // Im laufenden Spiel abgeschaltet: Das Konto bleibt stehen.
        s.SetAnstossElement("privateLife", false);
        var frozen = s.World.PrivateLives[s.HumanManager.Id].Savings;
        TestData.PlayUntil(s, _ => s.World.Date.Day == 2, 5_000);
        Assert.Equal(frozen, s.World.PrivateLives[s.HumanManager.Id].Savings);
        s.SetAnstossElement("privateLife", true);

        var path = Path.Combine(Path.GetTempPath(), $"fmh-private-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var a = s.World.PrivateLives[s.HumanManager.Id];
            var b = loaded.PrivateLives[s.HumanManager.Id];
            Assert.Equal((a.Savings, a.Stress, a.Home, a.Car, "fishing"), (b.Savings, b.Stress, b.Home, b.Car, b.Hobbies.Single()));
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.privateLife"]);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE private_lives; DELETE FROM meta WHERE key = 'schema.privateLife';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Empty(old.PrivateLives);
            var resumed = new GameSession(old, WithPackage.Value, new TextMatchEngine());
            resumed.Advance(1);
            Assert.NotNull(resumed.PrivateLife);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
