using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Pokal und Relegation im Saisonablauf.</summary>
public sealed partial class GameSession
{
    /// <summary>Legt die Pokale der Saison an und lost die erste Runde aus.</summary>
    public void CreateCups()
    {
        foreach (var country in World.Countries)
        {
            foreach (var cfg in country.Cups)
            {
                var cup = CupLogic.CreateCup(World, country, cfg, T);
                World.Cups.Add(cup);
                DrawRound(cup, cup.Rounds[0], cup.ParticipantIds);
            }
        }
    }

    private void DrawRound(Cup cup, CupRound round, List<int> teams)
    {
        CupLogic.Draw(World, cup, round, teams, _rng);
        var own = round.Ties.FirstOrDefault(t => t.HomeId == HumanClub.Id || t.AwayId == HumanClub.Id);
        if (own is null || HumanManager is null) return;
        if (own.Decision == "Bye")
        {
            Notify(MessageType.Cup, "msg.sender.association", T.Format("msg.cupDraw.subject", cup.Name, round.Name), T.Format("msg.cupDraw.bye", cup.Name, round.Name));
            return;
        }
        var home = own.HomeId == HumanClub.Id;
        var opp = World.Clubs[home ? own.AwayId : own.HomeId];
        Notify(MessageType.Cup, "msg.sender.association", T.Format("msg.cupDraw.subject", cup.Name, round.Name),
            T.Format("msg.cupDraw.body", cup.Name, round.Name, opp.Name, home ? T["ui.homeGame"] : T["ui.awayGame"], Fmt.Date(round.Dates[0])));
    }

    /// <summary>Nach einem Pokalspiel: Duell entscheiden, nächste Runde auslosen, Sieger ehren.</summary>
    private void CupMatchPlayed(Cup cup, Fixture f)
    {
        if (!CupLogic.ResolveTie(cup, f)) return;
        var round = cup.Rounds.First(r => r.Number == f.Matchday);
        var tie = CupLogic.TieOf(cup, f)!;
        if (tie.HomeId == HumanClub.Id || tie.AwayId == HumanClub.Id)
        {
            var won = tie.WinnerId == HumanClub.Id;
            var key = cup.Kind == CompetitionKind.Playoff ? (won ? "msg.playoff.won" : "msg.playoff.lost") : (won ? "msg.cup.won" : "msg.cup.out");
            Notify(MessageType.Cup, "msg.sender.association", T.Format(key + ".subject", cup.Name, round.Name), T.Format(key + ".body", cup.Name, round.Name, CupLogic.TieScore(cup, tie)));
        }
        if (!round.Complete) return;
        var winners = round.Ties.Select(t => t.WinnerId!.Value).ToList();
        if (round.Number == cup.Rounds.Count)
        {
            cup.WinnerId = winners[0];
            if (cup.Kind == CompetitionKind.Cup)
                Notify(MessageType.Cup, "msg.sender.association", T.Format("msg.cupWinner.subject", cup.Name), T.Format("msg.cupWinner.body", cup.Name, World.Clubs[winners[0]].Name));
            return;
        }
        DrawRound(cup, cup.Rounds[round.Number], winners);
    }

    /// <summary>Tägliche Prüfung: Relegation ansetzen, Saisonende erkennen. true = alle Wettbewerbe beendet.</summary>
    private bool CompetitionsDaily()
    {
        foreach (var country in World.Countries)
        {
            foreach (var cfg in country.Playoffs)
            {
                if (World.Cups.Any(c => c.Id == cfg.Id)) continue;
                var upper = World.FindLeague(cfg.UpperDivision);
                var lower = World.FindLeague(cfg.LowerDivision);
                if (upper is null || lower is null || !upper.IsComplete || !lower.IsComplete) continue;
                var u = LeagueTable.Compute(upper).First(r => r.Position == cfg.UpperPosition).ClubId;
                var l = LeagueTable.Compute(lower).First(r => r.Position == cfg.LowerPosition).ClubId;
                var cup = CupLogic.CreatePlayoff(World, country, cfg, u, l);
                // Termine liegen nach dem letzten Spieltag; falls nötig nach hinten schieben.
                var earliest = World.Date.AddDays(3);
                foreach (var fx in cup.Fixtures.Where(fx => fx.Date < earliest)) fx.Date = earliest.AddDays(4 * (fx.Leg - 1));
                World.Cups.Add(cup);
                if (u == HumanClub.Id || l == HumanClub.Id)
                {
                    var opp = World.Clubs[u == HumanClub.Id ? l : u];
                    Notify(MessageType.Cup, "msg.sender.league", T.Format("msg.playoff.subject", cfg.Name), T.Format("msg.playoff.body", opp.Name, Fmt.Date(cup.Fixtures[0].Date)));
                }
            }
        }
        var playoffsPending = World.Countries.SelectMany(c => c.Playoffs).Any(p => World.Cups.FirstOrDefault(c => c.Id == p.Id) is not { IsFinished: true });
        return World.Leagues.All(l => l.IsComplete) && !playoffsPending && World.Cups.All(c => c.IsFinished);
    }
}
