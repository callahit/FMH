using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.MatchEngine;

namespace Fmh.Tests;

/// <summary>AP 11b: Rekordbuch, Saisonbilanzen, Meisterchronik und ihre Speicherung.</summary>
public sealed class HistoryTests
{
    private static Fixture Game(int id, int home, int away, int hg, int ag, int attendance = 0, CompetitionKind kind = CompetitionKind.League) => new()
    {
        Id = id, CompetitionId = "liga", Kind = kind, HomeId = home, AwayId = away, Date = new DateOnly(2026, 9, id),
        Result = new MatchResult { HomeGoals = hg, AwayGoals = ag, Attendance = attendance },
    };

    [Fact]
    public void RecordsAreReplacedOnlyByBetterMatches()
    {
        var w = new World();
        RecordBook.Update(w, Game(1, 1, 2, 3, 0, 20_000), 2026);
        // Gleiche Tordifferenz, mehr eigene Tore: neuer Rekordsieg für Verein 1 und den Wettbewerb.
        RecordBook.Update(w, Game(2, 3, 1, 1, 4, 15_000), 2026);
        // Gleichstand bleibt beim älteren Rekord.
        RecordBook.Update(w, Game(3, 1, 4, 4, 1, 15_000), 2026);
        // Testspiele zählen nicht.
        RecordBook.Update(w, Game(4, 1, 5, 9, 0, 50_000, CompetitionKind.Friendly), 2026);

        var club = MatchRecord.ClubScope(1);
        var win = RecordBook.Find(w, club, RecordKinds.BiggestWin)!;
        Assert.Equal((3, 1, 4), (win.HomeId, win.HomeGoals, win.AwayGoals));
        Assert.Equal(2, RecordBook.Find(w, MatchRecord.CompetitionScope("liga"), RecordKinds.BiggestWin)!.Date.Day);
        Assert.Equal(20_000, RecordBook.Find(w, club, RecordKinds.Attendance)!.Attendance);
        Assert.Equal(2, RecordBook.Find(w, club, RecordKinds.MostGoals)!.Date.Day);
        Assert.Null(RecordBook.Find(w, club, RecordKinds.BiggestDefeat));
        var defeat = RecordBook.Find(w, MatchRecord.ClubScope(3), RecordKinds.BiggestDefeat)!;
        Assert.Equal(2, defeat.Date.Day);
        // Niederlagen gibt es nur je Verein, nicht je Wettbewerb.
        Assert.Null(RecordBook.Find(w, MatchRecord.CompetitionScope("liga"), RecordKinds.BiggestDefeat));
        Assert.DoesNotContain(w.Records, r => r.Scope == MatchRecord.ClubScope(5));
    }

    [Fact]
    public void DerivedRecordsComeFromHistoryPlayersAndTransfers()
    {
        var w = new World { SeasonYear = 2028 };
        w.History.Add(new SeasonRecord { SeasonYear = 2026, LeagueId = "liga", LeagueName = "Liga", ChampionId = 1, ChampionName = "A" });
        w.History.Add(new SeasonRecord { SeasonYear = 2027, LeagueId = "liga", LeagueName = "Liga", ChampionId = 1, ChampionName = "A" });
        w.History.Add(new SeasonRecord { SeasonYear = 2027, LeagueId = "pokal", LeagueName = "Pokal", ChampionId = 1, ChampionName = "A" });
        w.History.Add(new SeasonRecord { SeasonYear = 2028, LeagueId = "liga", LeagueName = "Liga", ChampionId = 2, ChampionName = "B" });
        var honours = RecordBook.Honours(w, 1);
        Assert.Equal(new[] { "liga", "pokal" }, honours.Select(h => h.CompetitionId));
        Assert.Equal(new[] { 2026, 2027 }, honours[0].Years);
        Assert.Equal(new[] { (1, 2), (2, 1) }, RecordBook.TitleTable(w, "liga").Select(t => (t.ClubId, t.Titles)));

        var p = new Player { Id = 7 };
        p.History.Add(new PlayerSeasonStats { SeasonYear = 2026, ClubId = 1, Appearances = 30, Goals = 12 });
        p.History.Add(new PlayerSeasonStats { SeasonYear = 2027, ClubId = 2, Appearances = 20, Goals = 25 });
        p.SeasonStats = new PlayerSeasonStats { SeasonYear = 2028, ClubId = 1, Appearances = 5, Goals = 3 };
        w.Players[p.Id] = p;
        Assert.Equal(new PlayerTotal(7, 15), RecordBook.ClubLeaders(w, 1, s => s.Goals, 5).Single());
        Assert.Equal(35, RecordBook.ClubLeaders(w, 1, s => s.Appearances, 5).Single().Value);

        w.ClubSeasons.Add(new ClubSeason { ClubId = 2, SeasonYear = 2027, LeagueId = "liga", Position = 1, Points = 80, Played = 34 });
        w.ClubSeasons.Add(new ClubSeason { ClubId = 1, SeasonYear = 2027, LeagueId = "liga", Position = 2, Points = 70, Played = 34 });
        Assert.Equal(2, RecordBook.MostPoints(w, "liga")!.ClubId);
        Assert.Equal(70, RecordBook.BestSeason(w, 1)!.Points);
        Assert.Equal(new PlayerSeasonBest(7, 2, 2027, 25), RecordBook.TopScorerSeason(w, "liga"));

        w.Transfers.Add(new TransferRecord { PlayerId = 7, FromClubId = 1, ToClubId = 2, Fee = 5_000_000, SeasonYear = 2027 });
        w.Transfers.Add(new TransferRecord { PlayerId = 7, FromClubId = 2, ToClubId = 1, Fee = 9_000_000, Loan = true, SeasonYear = 2028 });
        Assert.Equal(5_000_000, RecordBook.RecordTransfer(w, 2, incoming: true)!.Fee);
        Assert.Null(RecordBook.RecordTransfer(w, 1, incoming: true));
        Assert.Equal(5_000_000, RecordBook.RecordTransfer(w, 1, incoming: false)!.Fee);
    }

    [Fact]
    public void ASeasonFillsTheRecordBookAndSurvivesSaving()
    {
        var s = TestData.NewSession(1101);
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd, 50_000);
        var w = s.World;
        Assert.Equal(w.Leagues.Sum(l => l.ClubIds.Count), w.ClubSeasons.Count(c => c.SeasonYear == w.SeasonYear));
        foreach (var league in w.Leagues)
        {
            var champion = w.History.Last(h => h.LeagueId == league.Id).ChampionId;
            Assert.Equal(1, w.ClubSeasons.Single(c => c.SeasonYear == w.SeasonYear && c.ClubId == champion && c.LeagueId == league.Id).Position);
        }
        var own = MatchRecord.ClubScope(s.HumanClub.Id);
        Assert.NotNull(RecordBook.Find(w, own, RecordKinds.MostGoals));
        Assert.NotNull(RecordBook.Find(w, own, RecordKinds.Attendance));
        Assert.NotNull(RecordBook.Find(w, MatchRecord.CompetitionScope(s.HumanClub.LeagueId), RecordKinds.BiggestWin));

        var path = Path.Combine(Path.GetTempPath(), $"fmh-history-{Guid.NewGuid():N}.fmh");
        try
        {
            SqliteStore.Save(w, path);
            var loaded = SqliteStore.Load(path);
            Assert.Equal(w.ClubSeasons.Count, loaded.ClubSeasons.Count);
            Assert.Equal(w.Records.Count, loaded.Records.Count);
            var a = RecordBook.Find(w, own, RecordKinds.Attendance)!;
            var b = RecordBook.Find(loaded, own, RecordKinds.Attendance)!;
            Assert.Equal((a.Date, a.HomeId, a.AwayId, a.Attendance, a.CompetitionId), (b.Date, b.HomeId, b.AwayId, b.Attendance, b.CompetitionId));
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.history"]);

            // Älterer Spielstand ohne Abschnitt: Tabellen per Migration, leere Historie, Spiel läuft weiter.
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "DROP TABLE club_seasons; DROP TABLE match_records; DELETE FROM meta WHERE key = 'schema.history';";
                cmd.ExecuteNonQuery();
            }
            var old = SqliteStore.Load(path);
            Assert.Empty(old.ClubSeasons);
            Assert.Empty(old.Records);
            Assert.Equal("6", SqliteStore.ReadMetaTable(path)["schema.history"]);
            new GameSession(old, TestData.Data, new TextMatchEngine()).Advance(2);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
