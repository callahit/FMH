using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>
/// Meldelisten je Spielklasse: Spielberechtigungsliste der 1. und 2. Liga mit Local Playern, Squad List der Premier League mit
/// 25 Plätzen und Homegrown-Quote, U23-Pflicht im Spieltagskader der 3. Liga, Ausbildungsjahre und Spielstand.
/// </summary>
public class RegistrationTests
{
    private static GameSession NewEngland(ulong seed = 5)
    {
        var data = TestData.AllData;
        var world = WorldGenerator.Create(data, new NewGameOptions
        {
            Seed = seed, ClubKey = data.Clubs.First(c => c.Division == "ENG1").Key, ManagerName = "Test",
            Activation = new() { ["DE"] = CountryActivation.Inactive, ["ESP"] = CountryActivation.Inactive, ["ITA"] = CountryActivation.Inactive, ["FRA"] = CountryActivation.Inactive },
        });
        return new GameSession(world, data, new TextMatchEngine());
    }

    /// <summary>Generierte Spieler: rund 60 % im Land ausgebildet, davon ein Teil im eigenen Verein; Jugendspieler im eigenen Verein.</summary>
    [Fact]
    public void GeneratedPlayersHaveFormativeYears()
    {
        var w = TestData.NewSession().World;
        var pros = w.League("DE1").ClubIds.SelectMany(id => w.SquadOf(w.Clubs[id])).Where(p => Registration.SeasonAge(p, w) >= 17).ToList();
        var local = pros.Count(p => Registration.IsLocal(p, w, "DE")) / (double)pros.Count;
        var club = pros.Count(p => Registration.IsClubTrained(p, p.ClubId!.Value)) / (double)pros.Count;
        Assert.InRange(local, 0.45, 0.75);
        Assert.InRange(club, 0.08, 0.25);
        var youth = w.Players.Values.Where(p => p.IsYouth && Registration.SeasonAge(p, w) >= 15).ToList();
        Assert.NotEmpty(youth);
        Assert.All(youth, p => Assert.Equal(new[] { p.ClubId!.Value }, p.Registration.Formative.Keys));
    }

    /// <summary>1. Liga: Alle Spieler passen auf die Liste (32 Plätze), Mindestzahlen sind erreicht.</summary>
    [Fact]
    public void BundesligaListsTheWholeSquad()
    {
        var w = TestData.NewSession().World;
        foreach (var club in w.League("DE1").ClubIds.Select(id => w.Clubs[id]))
        {
            var squad = w.SquadOf(club).ToList();
            Assert.All(squad, p => Assert.True(Registration.IsEligible(p, w)));
            Assert.True(squad.Count(p => Registration.IsLocal(p, w, "DE")) >= 8);
        }
    }

    /// <summary>Premier Division: höchstens 25 Spieler ab 21 gemeldet, höchstens 17 ohne Homegrown-Status; U21 ohne Meldeplatz.</summary>
    [Fact]
    public void PremierDivisionListHas25PlacesAndHomegrownQuota()
    {
        var w = NewEngland().World;
        var rules = w.Country("ENG").Registration["ENG1"];
        foreach (var club in w.League("ENG1").ClubIds.Select(id => w.Clubs[id]))
        {
            var squad = w.SquadOf(club).ToList();
            var listed = squad.Where(p => !Registration.IsExempt(p, w, rules) && p.Registration.ClubId == club.Id).ToList();
            Assert.InRange(listed.Count, 1, 25);
            Assert.True(listed.Count(p => !Registration.IsLocal(p, w, "ENG")) <= 17);
            Assert.All(squad.Where(p => Registration.IsExempt(p, w, rules)), p => Assert.True(Registration.IsEligible(p, w)));
        }
        // Mehr als 25 Spieler ab 21 im Kader: Die Schwächsten bleiben ungemeldet und sind nicht spielberechtigt.
        var full = w.League("ENG1").ClubIds.Select(id => w.Clubs[id]).First(c => w.SquadOf(c).Count(p => !Registration.IsExempt(p, w, rules)) > 25);
        var unlisted = w.SquadOf(full).Where(p => !Registration.IsEligible(p, w)).ToList();
        Assert.NotEmpty(unlisted);
        var eligible = AiTactics.MatchEligibility(full, w, TestData.AllData);
        AiTactics.Prepare(full, w.Clubs[w.League("ENG1").ClubIds.First(id => id != full.Id)], true, w, TestData.AllData, w.Country("ENG").Squad.BenchSize);
        Assert.All(full.Tactic.Lineup.OfType<int>().Concat(full.Tactic.Bench), id => Assert.True(eligible(w.Players[id])));
    }

    [Fact]
    public void ManagerChangesTheListWithinTheRules()
    {
        var s = NewEngland();
        var w = s.World;
        var club = s.HumanClub;
        var rules = w.Country("ENG").Registration["ENG1"];
        var listed = w.SquadOf(club).Where(p => !Registration.IsExempt(p, w, rules) && p.Registration.ClubId == club.Id).ToList();
        var outside = w.SquadOf(club).FirstOrDefault(p => !Registration.IsExempt(p, w, rules) && p.Registration.ClubId != club.Id);
        // Abmelden und wieder melden geht; ist die Liste voll, nicht.
        var first = listed[0];
        Assert.Null(Registration.Set(w, club, first, false));
        Assert.False(Registration.IsEligible(first, w));
        Assert.Null(Registration.Set(w, club, first, true));
        if (listed.Count == 25 && outside is not null) Assert.Equal("registration.error.full", Registration.Set(w, club, outside, true));
        var young = w.SquadOf(club).FirstOrDefault(p => Registration.IsExempt(p, w, rules));
        if (young is not null) Assert.Equal("registration.error.exempt", Registration.Set(w, club, young, false));
    }

    /// <summary>3. Liga: Fehlen U23-Spieler im Spieltagskader, ersetzen junge Spieler die schwächsten älteren auf der Bank.</summary>
    [Fact]
    public void ThirdDivisionNeedsFourYoungPlayers()
    {
        var w = TestData.NewSession().World;
        var club = w.Clubs[w.League("DE3").ClubIds[0]];
        bool Young(Player p) => Registration.SeasonAge(p, w) <= 23;
        // Bank nur mit älteren Spielern besetzen.
        var older = w.SquadOf(club).Where(p => !Young(p) && !club.Tactic.Lineup.Contains(p.Id)).Select(p => p.Id).ToList();
        club.Tactic.Bench = older.Take(club.Tactic.Bench.Count).ToList();
        var youngAvailable = w.SquadOf(club).Count(p => Young(p) && p.IsAvailable);
        AiTactics.EnsureYoungPlayers(club, w, TestData.Data);
        var squad = club.Tactic.Lineup.OfType<int>().Concat(club.Tactic.Bench).Select(id => w.Players[id]).ToList();
        Assert.True(squad.Count(Young) >= Math.Min(4, youngAvailable));
    }

    [Fact]
    public void SeasonAddsAFormativeYear()
    {
        var w = TestData.NewSession().World;
        var p = w.Players.Values.First(x => x.ClubId is not null && Registration.SeasonAge(x, w) is >= 15 and <= 20);
        var before = p.Registration.Formative.GetValueOrDefault(p.ClubId!.Value);
        Registration.CountSeason(p, w);
        Assert.Equal(before + 1, p.Registration.Formative[p.ClubId.Value]);
    }

    /// <summary>Saisonwechsel: Gezählt wird die abgelaufene Saison mit dem Alter und Verein vor dem Wechsel.</summary>
    [Fact]
    public void RolloverCountsTheFinishedSeason()
    {
        var s = TestData.NewSession();
        var w = s.World;
        var players = w.Players.Values.Where(p => p.ClubId is not null && !p.IsYouth).ToList();
        var last = players.First(p => Registration.SeasonAge(p, w) == 21);
        var tooOld = players.First(p => Registration.SeasonAge(p, w) == 22);
        var before = (Last: last.Registration.Formative.GetValueOrDefault(last.ClubId!.Value), Club: last.ClubId.Value,
            Old: tooOld.Registration.Formative.Values.Sum());
        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason, 20_000);
        Assert.Equal(before.Last + 1, last.Registration.Formative[before.Club]);
        Assert.Equal(before.Old, tooOld.Registration.Formative.Values.Sum());
    }

    /// <summary>Spielstand Version 6; ältere Spielstände (ohne Spalte) laden mit Meldung beim aktuellen Verein.</summary>
    [Fact]
    public void RegistrationSurvivesSavingAndOldSavesMigrate()
    {
        var s = TestData.NewSession();
        var path = Path.Combine(Path.GetTempPath(), $"fmh-reg-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(s.World, path);
            var loaded = SqliteStore.Load(path);
            var p = s.World.Players.Values.First(x => x.Registration.Formative.Count > 0 && x.Registration.ClubId is not null);
            Assert.Equal(p.Registration.ClubId, loaded.Players[p.Id].Registration.ClubId);
            Assert.Equal(p.Registration.Formative, loaded.Players[p.Id].Registration.Formative);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE players DROP COLUMN registration; UPDATE meta SET value = '5' WHERE key = 'schema.players';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.All(old.Players.Values.Where(x => x.ClubId is not null), x => Assert.Equal(x.ClubId, x.Registration.ClubId));
            Assert.Equal("7", SqliteStore.ReadMetaTable(path)["schema.players"]);
            // Die Sitzung ergänzt die Ausbildungsjahre.
            var session = new GameSession(old, TestData.Data, new TextMatchEngine());
            Assert.Contains(session.World.Players.Values, x => x.Registration.Formative.Count > 0);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
