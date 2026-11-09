using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Fmh.Tests;

/// <summary>E4-S1: Gelb, Gelb-Rot und Rot zählen und werden je Wettbewerb abgesessen.</summary>
public class DisciplineTests
{
    private static DisciplineRules.MatchDiscipline Of(string scope, DisciplineRules.ResolvedDiscipline rules) => new(scope, rules);

    private static DisciplineRules.ResolvedDiscipline Cycle(int threshold) =>
        DisciplineRules.Resolve(new DisciplineConfig { YellowCardsForSuspension = threshold }, "x", null);

    [Fact]
    public void FiveLeagueYellowsDoNotSuspendTheCup()
    {
        var rules = Cycle(5);
        var state = new PlayerState();
        for (var card = 1; card <= 4; card++)
            Assert.False(DisciplineRules.ApplyCard(state, Of(PlayerState.LeagueScope, rules), true, false, card, (_, _) => 1).YellowBan);
        var fifth = DisciplineRules.ApplyCard(state, Of(PlayerState.LeagueScope, rules), true, false, 5, (_, _) => 1);
        Assert.True(fifth.YellowBan);
        Assert.Equal(5, fifth.AtCards);
        Assert.Equal(0, state.Yellows(PlayerState.LeagueScope));
        Assert.Equal(1, state.YellowBan(PlayerState.LeagueScope));
        Assert.Equal(0, state.Ban("DE-CUP"));
        var player = new Player { State = state };
        Assert.True(player.IsAvailableFor("DE-CUP"));
        Assert.False(player.IsAvailableFor(PlayerState.LeagueScope));
    }

    [Fact]
    public void CupThresholdDoesNotMoveTheLeagueCounter()
    {
        var state = new PlayerState();
        var cup = Cycle(3);
        for (var card = 1; card <= 3; card++)
            DisciplineRules.ApplyCard(state, Of("DE-CUP", cup), true, false, card, (_, _) => 1);
        Assert.Equal(1, state.Ban("DE-CUP"));
        Assert.Equal(0, state.Yellows(PlayerState.LeagueScope));
        Assert.Equal(0, state.Ban(PlayerState.LeagueScope));
        DisciplineRules.ApplyCard(state, Of(PlayerState.LeagueScope, Cycle(5)), true, false, 1, (_, _) => 1);
        Assert.Equal(1, state.Yellows(PlayerState.LeagueScope));
        Assert.Equal(1, state.Ban("DE-CUP"));
    }

    [Fact]
    public void RedAndSecondYellowStayInTheirCompetition()
    {
        var rules = Cycle(5);
        var state = new PlayerState();
        var red = DisciplineRules.ApplyCard(state, Of("UCL", rules), yellow: false, red: true, 1, (_, _) => 3);
        Assert.True(red.SendingOff);
        Assert.Equal(3, red.BanMatches);
        Assert.Equal(3, state.RedBan("UCL"));
        Assert.Equal(0, state.Ban(PlayerState.LeagueScope));
        var second = DisciplineRules.ApplyCard(state, Of("DE-CUP", rules), yellow: true, red: true, 1, (_, _) => 9);
        Assert.Equal(1, second.BanMatches);
        Assert.Equal(0, state.Yellows("DE-CUP"));
        Assert.Equal(1, state.RedBan("DE-CUP"));
        Assert.Equal(3, state.RedBan("UCL"));

        Assert.False(DisciplineRules.Serve(state, "UCL"));
        Assert.Equal(2, state.RedBan("UCL"));
        state.SetDiscipline("UCL", 0, 0, 1);
        Assert.True(DisciplineRules.Serve(state, "UCL"));
        Assert.Equal(0, state.Ban("UCL"));
        Assert.Equal(1, state.Ban("DE-CUP"));

        state.PersonalBan = 1;
        var player = new Player { State = state };
        Assert.False(player.IsAvailableFor(PlayerState.LeagueScope));
        Assert.False(player.IsAvailableFor("DE-CUP"));
        Assert.True(DisciplineRules.Serve(state, PlayerState.LeagueScope));
        Assert.Equal(0, state.PersonalBan);
        Assert.True(player.IsAvailableFor(PlayerState.LeagueScope));
        Assert.False(player.IsAvailableFor("DE-CUP"));
    }

    [Fact]
    public void UefaBansOnOddYellowsAndEnglandUsesCutoffs()
    {
        var uefa = DisciplineRules.Resolve(new DisciplineConfig { YellowCardsForSuspension = 3, YellowRepeat = 2 }, "UCL", null);
        var state = new PlayerState();
        for (var card = 1; card <= 7; card++)
        {
            var outcome = DisciplineRules.ApplyCard(state, Of("UCL", uefa), true, false, card, (_, _) => 1);
            Assert.Equal(card is 3 or 5 or 7, outcome.YellowBan);
        }
        Assert.Equal(7, state.Yellows("UCL"));
        Assert.Equal(3, state.Ban("UCL"));

        var ladder = DisciplineRules.Resolve(new DisciplineConfig
        {
            YellowCardsForSuspension = 0,
            Thresholds = new()
            {
                new YellowThreshold { Cards = 5, Matches = 1, UntilRound = 19 },
                new YellowThreshold { Cards = 10, Matches = 2, UntilRound = 32 },
                new YellowThreshold { Cards = 15, Matches = 3 },
            },
        }, "ENG1", PlayerState.LeagueScope);
        var late = new PlayerState();
        for (var card = 1; card <= 5; card++)
            Assert.False(DisciplineRules.ApplyCard(late, Of(PlayerState.LeagueScope, ladder), true, false, 20, (_, _) => 1).YellowBan);
        var early = new PlayerState();
        DisciplineRules.CardOutcome fifth = default;
        for (var card = 1; card <= 5; card++)
            fifth = DisciplineRules.ApplyCard(early, Of(PlayerState.LeagueScope, ladder), true, false, 19, (_, _) => 1);
        Assert.True(fifth.YellowBan);
        Assert.Equal(1, early.Ban(PlayerState.LeagueScope));
        Assert.Equal(2, ladder.YellowBanLength(10, 30));
        Assert.Equal(0, ladder.YellowBanLength(10, 33));
    }

    [Fact]
    public void SeasonDropsYellowsAndKeepsRedBans()
    {
        var country = new DisciplineConfig
        {
            ResetYellowsOnSeason = true,
            CarryYellowBansOnSeason = false,
            CarryRedBansOnSeason = true,
        };
        var state = new PlayerState();
        state.SetDiscipline(PlayerState.LeagueScope, 4, 1, 2);
        state.SetDiscipline("DE-CUP", 3, 0, 1);
        DisciplineRules.ApplySeason(state, country, europe: null);
        Assert.Equal((0, 0, 2), (state.Yellows(PlayerState.LeagueScope), state.YellowBan(PlayerState.LeagueScope), state.RedBan(PlayerState.LeagueScope)));
        Assert.Equal((0, 1), (state.Yellows("DE-CUP"), state.RedBan("DE-CUP")));
    }

    [Fact]
    public void YellowsClearAfterTheConfiguredRoundButTheBanStands()
    {
        var rules = DisciplineRules.Resolve(new DisciplineConfig { YellowCardsForSuspension = 5, ClearYellowsAfterRound = 19 }, "ES1", PlayerState.LeagueScope);
        var match = Of(PlayerState.LeagueScope, rules);
        var four = new PlayerState();
        four.SetDiscipline(PlayerState.LeagueScope, 4, 0, 0);
        DisciplineRules.ClearYellowsIfRoundEnds(four, match, 19);
        Assert.Equal(0, four.Yellows(PlayerState.LeagueScope));

        var fifth = new PlayerState();
        fifth.SetDiscipline(PlayerState.LeagueScope, 4, 0, 0);
        Assert.True(DisciplineRules.ApplyCard(fifth, match, true, false, 19, (_, _) => 1).YellowBan);
        DisciplineRules.ClearYellowsIfRoundEnds(fifth, match, 19);
        Assert.Equal(1, fifth.Ban(PlayerState.LeagueScope));
        Assert.Equal(0, fifth.Yellows(PlayerState.LeagueScope));
    }

    [Fact]
    public void ShippedRulesDifferByCompetition()
    {
        var de = TestData.Data.Countries.Single(c => c.Code == "DE").Discipline;
        Assert.Equal(5, DisciplineRules.Resolve(de, "DE1", PlayerState.LeagueScope).YellowCardsForSuspension);
        Assert.Equal(5, DisciplineRules.Resolve(de, "DE-CUP", PlayerState.CupScope).YellowCardsForSuspension);
        Assert.False(de.CarryYellowBansOnSeason);
        Assert.True(de.CarryRedBansOnSeason);

        var euro = TestData.Data.Europe!.Discipline!;
        var ucl = DisciplineRules.Resolve(euro, "UCL", null);
        Assert.Equal(3, ucl.YellowCardsForSuspension);
        Assert.Equal(2, ucl.YellowRepeat);
        Assert.Equal(1, ucl.RedCardBanMin);
        Assert.Equal(1, ucl.RedCardBanMax);

        var all = TestData.LoadAll();
        var eng = DisciplineRules.Resolve(all.Countries.Single(c => c.Code == "ENG").Discipline, "ENG1", PlayerState.LeagueScope);
        Assert.Equal(1, eng.YellowBanLength(5, 19));
        Assert.Equal(0, eng.YellowBanLength(5, 20));
        Assert.Equal(2, eng.YellowBanLength(10, 32));
        var spain = all.Countries.Single(c => c.Code == "ESP").Discipline;
        Assert.Equal(3, DisciplineRules.Resolve(spain, "ES-CUP", PlayerState.CupScope).YellowCardsForSuspension);
        Assert.Equal(19, DisciplineRules.Resolve(spain, "ES1", PlayerState.LeagueScope).ClearYellowsAfterRound);
        Assert.Equal(5, DisciplineRules.Resolve(all.Countries.Single(c => c.Code == "FRA").Discipline, "FRA-CUP", PlayerState.CupScope).YellowCardsForSuspension);
    }

    [Fact]
    public void SuspensionScopesRoundTripThroughSave()
    {
        var session = TestData.NewSession(7);
        var player = session.World.Players.Values.First(p => p.ClubId == session.HumanClub.Id);
        player.State.SetDiscipline(PlayerState.LeagueScope, 4, 1, 2);
        player.State.SetDiscipline("DE-CUP", 2, 0, 1);
        player.State.SetDiscipline("UCL", 3, 0, 0);
        player.State.PersonalBan = 1;
        var path = Path.Combine(Path.GetTempPath(), $"fmh-susp-{Guid.NewGuid():N}.fmh");
        try
        {
            SaveGame.Save(session.World, path);
            var loaded = SqliteStore.Load(path).Players[player.Id].State;
            Assert.Equal((4, 1, 2), (loaded.Yellows(PlayerState.LeagueScope), loaded.YellowBan(PlayerState.LeagueScope), loaded.RedBan(PlayerState.LeagueScope)));
            Assert.Equal((2, 0, 1), (loaded.Yellows("DE-CUP"), loaded.YellowBan("DE-CUP"), loaded.RedBan("DE-CUP")));
            Assert.Equal(3, loaded.Yellows("UCL"));
            Assert.Equal(0, loaded.Ban("UEL"));
            Assert.Equal(1, loaded.PersonalBan);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Eine ganze Saison mit Liga und Pokal: eine Ligasperre sperrt den Pokal nicht und umgekehrt,
    /// Rot bleibt im Wettbewerb, die KI stellt gesperrte Spieler nicht auf.
    /// </summary>
    [Fact]
    public void FullSeasonKeepsSuspensionsInsideTheirCompetition()
    {
        var session = TestData.NewSession(42);
        var world = session.World;
        var ai = world.Clubs.Values.First(c => c.Id != session.HumanClub.Id && c.ReserveOf is null
            && world.AllFixtures.Any(f => f.Result is null && f.Involves(c.Id) && f.Kind == CompetitionKind.Cup)
            && world.AllFixtures.Any(f => f.Result is null && f.Involves(c.Id) && f.Kind == CompetitionKind.League));
        Fixture Next(CompetitionKind kind) => world.AllFixtures
            .Where(f => f.Result is null && f.Involves(ai.Id) && f.Kind == kind).OrderBy(f => f.Date).First();
        var cupFix = Next(CompetitionKind.Cup);
        var leagueFix = Next(CompetitionKind.League);
        var cupScope = DisciplineRules.ScopeOf(cupFix, world);
        var leagueScope = DisciplineRules.ScopeOf(leagueFix, world);
        Assert.Equal(PlayerState.LeagueScope, leagueScope);
        Assert.NotEqual(leagueScope, cupScope);

        void Prepare(Fixture fixture)
        {
            var opponent = world.Clubs[fixture.HomeId == ai.Id ? fixture.AwayId : fixture.HomeId];
            AiTactics.Prepare(ai, opponent, fixture.HomeId == ai.Id, world, session.Data, session.BenchSize(ai), DisciplineRules.ScopeOf(fixture, world));
        }

        var profiles = session.Data.PositionProfiles;
        Prepare(cupFix);
        var player = ai.Tactic.Lineup.Where(id => id.HasValue).Select(id => world.Players[id!.Value])
            .Where(p => p.State.Injury is null)
            .OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, profiles))
            .First();
        player.State.SetDiscipline(leagueScope, 0, 0, 1);
        Prepare(cupFix);
        Assert.Contains(player.Id, ai.Tactic.Lineup);
        Prepare(leagueFix);
        Assert.DoesNotContain(player.Id, ai.Tactic.Lineup.OfType<int>().Concat(ai.Tactic.Bench));

        player.State.SetDiscipline(leagueScope, 0, 0, 0);
        var sentOff = DisciplineRules.ApplyCard(player.State, DisciplineRules.ForMatch(world, session.Data, cupFix, ai), false, true, cupFix.Matchday, (_, _) => 2);
        Assert.Equal(2, sentOff.BanMatches);
        Assert.Equal(2, player.State.RedBan(cupScope));
        Assert.Equal(0, player.State.Ban(leagueScope));
        Prepare(leagueFix);
        Assert.Contains(player.Id, ai.Tactic.Lineup);
        Prepare(cupFix);
        Assert.DoesNotContain(player.Id, ai.Tactic.Lineup.OfType<int>().Concat(ai.Tactic.Bench));
        player.State.SetDiscipline(cupScope, 0, 0, 0);

        var kinds = new HashSet<CompetitionKind>();
        var seasonEnded = false;
        for (var step = 0; step < 20_000 && !seasonEnded; step++)
        {
            var result = session.Advance();
            if (result.Reason == StopReason.Decision) { session.DelegateDecisions(); continue; }
            if (result.Reason == StopReason.HumanMatch && result.Fixture is { } fixture)
            {
                var scope = DisciplineRules.ScopeOf(fixture, world);
                var simulation = session.StartMatch(fixture, humanControlled: false);
                foreach (var team in new[] { simulation.Setup.Home, simulation.Setup.Away })
                    foreach (var starter in team.Starting.Concat(team.Bench))
                        Assert.True(starter.IsAvailableFor(scope), $"{starter.Name} steht trotz Sperre in {scope}.");
                var before = Snapshot(world, fixture);
                simulation.RunToEnd();
                session.FinishMatch(fixture, simulation);
                AssertOtherScopesUnchanged(world, fixture, scope, before);
                foreach (var card in fixture.Result!.Cards.Where(c => c.Red))
                {
                    var previous = before.TryGetValue(card.PlayerId, out var row) && row.TryGetValue(scope, out var value) ? value.RedBan : 0;
                    Assert.True(world.Players[card.PlayerId].State.RedBan(scope) > previous);
                }
                kinds.Add(fixture.Kind);
                continue;
            }
            if (result.Reason == StopReason.SeasonEnd) seasonEnded = true;
        }
        Assert.True(seasonEnded);
        Assert.Contains(CompetitionKind.League, kinds);
        Assert.Contains(CompetitionKind.Cup, kinds);
        var europe = world.Cups.Where(c => c.Kind == CompetitionKind.Europe).SelectMany(c => c.Fixtures).ToList();
        if (europe.Count > 0) Assert.Contains(europe, f => f.Result is not null);

        var marked = world.SquadOf(session.HumanClub).Where(p => p.Contract is not null).OrderBy(p => p.Age(world.Date)).First();
        marked.Contract!.Until = new DateOnly(world.SeasonYear + 2, 6, 30);
        marked.State.SetDiscipline(PlayerState.LeagueScope, 4, 1, 2);
        marked.State.SetDiscipline("DE-CUP", 3, 0, 0);
        var year = world.SeasonYear;
        TestData.PlayUntil(session, r => r.Reason == StopReason.NewSeason || session.World.SeasonYear > year, 20_000);
        Assert.Equal(year + 1, session.World.SeasonYear);
        Assert.Equal((0, 0, 2), (marked.State.Yellows(PlayerState.LeagueScope), marked.State.YellowBan(PlayerState.LeagueScope), marked.State.RedBan(PlayerState.LeagueScope)));
        Assert.Equal(0, marked.State.Yellows("DE-CUP"));
    }

    private static Dictionary<int, Dictionary<string, (int Yellows, int YellowBan, int RedBan)>> Snapshot(World world, Fixture fixture)
    {
        var map = new Dictionary<int, Dictionary<string, (int, int, int)>>();
        foreach (var clubId in new[] { fixture.HomeId, fixture.AwayId })
            foreach (var player in world.SquadOf(world.Clubs[clubId]))
            {
                var row = new Dictionary<string, (int, int, int)>();
                foreach (var scope in player.State.ScopeKeys())
                    row[scope] = (player.State.Yellows(scope), player.State.YellowBan(scope), player.State.RedBan(scope));
                map[player.Id] = row;
            }
        return map;
    }

    private static void AssertOtherScopesUnchanged(World world, Fixture fixture, string scope,
        Dictionary<int, Dictionary<string, (int Yellows, int YellowBan, int RedBan)>> before)
    {
        foreach (var clubId in new[] { fixture.HomeId, fixture.AwayId })
            foreach (var player in world.SquadOf(world.Clubs[clubId]))
            {
                var prior = before.TryGetValue(player.Id, out var row) ? row : new Dictionary<string, (int, int, int)>();
                foreach (var key in prior.Keys.Union(player.State.ScopeKeys()))
                {
                    if (key == scope) continue;
                    var expected = prior.TryGetValue(key, out var value) ? value : (0, 0, 0);
                    Assert.Equal(expected, (player.State.Yellows(key), player.State.YellowBan(key), player.State.RedBan(key)));
                }
            }
    }
}
