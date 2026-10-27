using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Pokal und Relegation im Saisonablauf.</summary>
public sealed partial class CupsModule : GameModule, ICupsService
{
    public override string Id => "cups";

    /// <summary>Legt die Pokale der Saison an und lost die erste Runde aus.</summary>
    public void CreateCups()
    {
        foreach (var country in World.Countries)
        {
            foreach (var cfg in country.Cups)
            {
                var cup = CupLogic.CreateCup(World, country, cfg);
                World.Cups.Add(cup);
                DrawRound(cup, cup.Rounds[0], cup.ParticipantIds);
            }
        }
    }

    private void DrawRound(Cup cup, CupRound round, List<int> teams)
    {
        CupLogic.Draw(World, cup, round, teams, Rng);
        Finance.CupPrize(cup, round.Number, teams);
        var own = round.Ties.FirstOrDefault(t => t.HomeId == HumanClub.Id || t.AwayId == HumanClub.Id);
        if (own is null || HumanManager is null) return;
        if (own.Decision == "Bye")
        {
            Notify(MessageType.Cup, "msg.sender.association", TR("msg.cupDraw.subject", Comp(cup.Id), Round(round)), TR("msg.cupDraw.bye", Comp(cup.Id), Round(round)));
            return;
        }
        var home = own.HomeId == HumanClub.Id;
        var opp = World.Clubs[home ? own.AwayId : own.HomeId];
        Notify(MessageType.Cup, "msg.sender.association", TR("msg.cupDraw.subject", Comp(cup.Id), Round(round)),
            TR("msg.cupDraw.body", Comp(cup.Id), Round(round), opp.Name, home ? TR("ui.homeGame") : TR("ui.awayGame"), TextArg.OfDate(round.Dates[0])));
    }

    /// <summary>Nach einem Pokalspiel: Duell entscheiden, nächste Runde auslosen, Sieger ehren.</summary>
    public void CupMatchPlayed(Cup cup, Fixture f)
    {
        if (!CupLogic.ResolveTie(cup, f)) return;
        var round = cup.Rounds.First(r => r.Number == f.Matchday);
        var tie = CupLogic.TieOf(cup, f)!;
        if (tie.HomeId == HumanClub.Id || tie.AwayId == HumanClub.Id)
        {
            var won = tie.WinnerId == HumanClub.Id;
            var key = cup.Kind == CompetitionKind.Playoff ? (won ? "msg.playoff.won" : "msg.playoff.lost") : (won ? "msg.cup.won" : "msg.cup.out");
            Notify(MessageType.Cup, "msg.sender.association", TR(key + ".subject", Comp(cup.Id), Round(round)), TR(key + ".body", Comp(cup.Id), Round(round), CupLogic.TieScore(cup, tie)));
        }
        if (!round.Complete) return;
        var winners = round.Ties.Select(t => t.WinnerId!.Value).ToList();
        if (round.Number == cup.Rounds.Count)
        {
            cup.WinnerId = winners[0];
            if (cup.Kind == CompetitionKind.Cup) Finance.CupPrize(cup, cup.Rounds.Count + 1, winners);
            if (cup.Kind == CompetitionKind.Cup)
                Notify(MessageType.Cup, "msg.sender.association", TR("msg.cupWinner.subject", Comp(cup.Id)), TR("msg.cupWinner.body", Comp(cup.Id), World.Clubs[winners[0]].Name));
            return;
        }
        DrawRound(cup, cup.Rounds[round.Number], winners);
    }

    private IEnumerable<PlayoffConfig> ActivePlayoffs(CountryConfig country) =>
        country.Playoffs.Where(p => p.OnlySeason is null || p.OnlySeason == World.SeasonYear);

    /// <summary>Teilnehmer einer Relegation, sobald alle beteiligten Ligen fertig sind; bei geteilten Ligen aus jeder Staffel.</summary>
    private List<int>? Entrants(PlayoffConfig cfg)
    {
        var result = new List<int>();
        foreach (var e in cfg.Entrants)
        {
            var leagues = Pyramid.Matching(World, e.Division).ToList();
            if (leagues.Count == 0 || leagues.Any(l => !l.IsComplete)) return null;
            foreach (var l in leagues)
                if (LeagueTable.Compute(l).FirstOrDefault(r => r.Position == e.Position) is { } row) result.Add(row.ClubId);
        }
        return result.Count >= 2 ? result : null;
    }

    /// <summary>Tägliche Prüfung: Relegation ansetzen, Saisonende erkennen. true = alle Wettbewerbe beendet.</summary>
    public bool CompetitionsDaily()
    {
        foreach (var country in World.Countries)
        {
            foreach (var cfg in ActivePlayoffs(country))
            {
                if (World.Cups.Any(c => c.Id == cfg.Id)) continue;
                if (Entrants(cfg) is not { } entrants) continue;
                var cup = entrants.Count == 2
                    ? CupLogic.CreatePlayoff(World, country, cfg, entrants[0], entrants[1])
                    : CupLogic.CreatePlayoffTournament(World, country, cfg, entrants, Rng);
                // Termine liegen nach dem letzten Spieltag; falls nötig nach hinten schieben.
                var earliest = World.Date.AddDays(3);
                foreach (var fx in cup.Fixtures.Where(fx => fx.Date < earliest)) fx.Date = earliest.AddDays(4 * (fx.Leg - 1));
                World.Cups.Add(cup);
                Finance.CupPrize(cup, 1, entrants);
                if (cup.Rounds[0].Ties.FirstOrDefault(t => t.HomeId == HumanClub.Id || t.AwayId == HumanClub.Id) is { } own && own.HomeId != own.AwayId)
                {
                    var opp = World.Clubs[own.HomeId == HumanClub.Id ? own.AwayId : own.HomeId];
                    var first = cup.Fixtures.First(fx => fx.TieId == own.Id);
                    Notify(MessageType.Cup, "msg.sender.league", TR("msg.playoff.subject", Comp(cfg.Id)), TR("msg.playoff.body", opp.Name, TextArg.OfDate(first.Date)));
                }
            }
        }
        var playoffsPending = World.Countries.SelectMany(ActivePlayoffs).Any(p => World.Cups.FirstOrDefault(c => c.Id == p.Id) is not { IsFinished: true });
        return World.Leagues.All(l => l.IsComplete) && !playoffsPending && World.Cups.All(c => c.IsFinished);
    }
}
