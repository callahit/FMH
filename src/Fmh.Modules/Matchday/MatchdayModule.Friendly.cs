using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Testspiele (AP 9): kein Eintrag in Tabelle, Saisonstatistik, Form, Sperren oder Gelbkartenkonto. Sie bringen
/// Spielpraxis, Kondition (Physis-Training) und Taktikvertrautheit, im Trainingslager verstärkt. Verletzungen gibt es
/// wie im Pflichtspiel. Zuschauereinnahmen nach vereinbarter Teilung; im Trainingslager keine.
/// </summary>
public sealed partial class MatchdayModule
{
    private void FinishFriendly(Fixture f, IMatchSimulation sim, MatchResult result)
    {
        var rules = Data.Friendlies;
        var friendly = World.Friendlies.First(x => x.Fixture.Id == f.Id);
        var home = World.Clubs[f.HomeId];
        var away = World.Clubs[f.AwayId];
        if (!friendly.Camp && friendly.TournamentId is null)
        {
            var (standing, seating, _) = Finance.Attendance(home, away, f, result.Weather);
            result.Attendance = (int)Math.Round((standing + seating) * rules.AttendanceFactor);
            BookFriendlyGate(friendly, home, away, result.Attendance);
        }
        f.Result = result;
        var bonus = friendly.Camp ? rules.CampBonus : 1;
        ApplyFriendlyPlayers(sim.Home, home, sim.Minute, bonus);
        ApplyFriendlyPlayers(sim.Away, away, sim.Minute, bonus);
        Training.AddFamiliarity(home, home.Tactic.FormationId, rules.FamiliarityGain * bonus);
        Training.AddFamiliarity(away, away.Tactic.FormationId, rules.FamiliarityGain * bonus);
    }

    /// <summary>Zuschauereinnahmen (ohne Dauerkarten) nach dem Anteil des Gastgebers.</summary>
    private void BookFriendlyGate(Friendly friendly, Club home, Club away, int attendance)
    {
        var prices = home.Finance.Prices;
        var gate = (long)(attendance * (prices.Standing + prices.Seating) / 2.0);
        if (gate <= 0) return;
        var homePart = gate * friendly.HomeSharePercent / 100;
        if (homePart > 0 && Finance.HasFinances(home)) Finance.Book(home, TransactionType.Tickets, homePart, TR("tx.friendly", away.Name));
        if (gate - homePart > 0 && Finance.HasFinances(away)) Finance.Book(away, TransactionType.Tickets, gate - homePart, TR("tx.friendly", home.Name));
    }

    private void ApplyFriendlyPlayers(MatchTeamState team, Club club, int matchMinutes, double bonus)
    {
        var rules = Data.Friendlies;
        var human = club.Id == HumanClub.Id && !Board.AwaitingNewJob;
        foreach (var mp in team.Participants)
        {
            var p = mp.Player;
            var share = mp.MinutesPlayed(matchMinutes) / 90.0;
            p.State.Condition = Math.Round(mp.Condition, 1);
            p.State.MatchPractice = Math.Min(100, p.State.MatchPractice + share * rules.MatchPracticeGain);
            p.Training.Physical += share * rules.PhysicalGain * bonus;
            if (mp.Injured && mp.InjuryType is { } type)
                InjurePlayer(p, type, mp.InjuryDays, human);
        }
    }
}
