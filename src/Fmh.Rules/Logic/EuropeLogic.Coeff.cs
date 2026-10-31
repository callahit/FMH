using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

public static partial class EuropeLogic
{
    /// <summary>Koeffizienten nach einem Europapokalspiel aktualisieren (Land und Verein).</summary>
    public static void ApplyMatchCoefficients(World world, GameData data, Fixture f)
    {
        if (data.Europe is not { Access: var a } || f.Result is not { } r) return;
        var home = world.Clubs[f.HomeId];
        var away = world.Clubs[f.AwayId];
        void Add(int clubId, string country, double pts)
        {
            world.ClubCoefficients[clubId] = world.ClubCoefficients.GetValueOrDefault(clubId) + pts;
            world.CountryCoefficients[country] = world.CountryCoefficients.GetValueOrDefault(country, a.DefaultCountryScore) + pts;
        }
        if (r.HomeGoals > r.AwayGoals) { Add(f.HomeId, home.CountryCode, a.Win); Add(f.AwayId, away.CountryCode, 0); }
        else if (r.HomeGoals < r.AwayGoals) { Add(f.AwayId, away.CountryCode, a.Win); Add(f.HomeId, home.CountryCode, 0); }
        else { Add(f.HomeId, home.CountryCode, a.Draw); Add(f.AwayId, away.CountryCode, a.Draw); }

        if (f.TieId is not null && world.FindCup(f.CompetitionId) is { } cup
            && CupLogic.TieOf(cup, f) is { WinnerId: not null, Decision: not ("Bye" or "League") } tie
            && f.Leg >= tie.Legs)
        {
            var round = cup.Rounds.First(x => x.Ties.Any(t => t.Id == tie.Id));
            if (round.StageId is "q1" or "q2" or "q3" or "po")
                world.ClubCoefficients[tie.WinnerId.Value] =
                    world.ClubCoefficients.GetValueOrDefault(tie.WinnerId.Value) + a.BonusQualifyingWin;
        }
    }

    /// <summary>Stufenboni beim Erreichen einer Runde (Ligaphase, K.-o.).</summary>
    public static void ApplyStageBonus(World world, GameData data, string competitionId, string stageId, IEnumerable<int> clubIds)
    {
        if (data.Europe is not { Access: var a }) return;
        var bonus = stageId switch
        {
            "league" => a.BonusLeaguePhase,
            "r16" => a.BonusRoundOf16,
            "qf" => a.BonusQuarter,
            "sf" => a.BonusSemi,
            "final" => a.BonusFinal,
            _ => 0,
        };
        if (bonus <= 0) return;
        foreach (var id in clubIds.Distinct())
        {
            world.ClubCoefficients[id] = world.ClubCoefficients.GetValueOrDefault(id) + bonus;
            var code = world.Clubs[id].CountryCode;
            world.CountryCoefficients[code] = world.CountryCoefficients.GetValueOrDefault(code, a.DefaultCountryScore) + bonus * 0.2;
        }
    }

    public static void ApplyTitleBonus(World world, GameData data, int clubId)
    {
        if (data.Europe is not { Access: var a }) return;
        world.ClubCoefficients[clubId] = world.ClubCoefficients.GetValueOrDefault(clubId) + a.BonusTitle;
        var code = world.Clubs[clubId].CountryCode;
        world.CountryCoefficients[code] = world.CountryCoefficients.GetValueOrDefault(code, a.DefaultCountryScore) + a.BonusTitle * 0.25;
    }

    /// <summary>Verlierer einer Qualifikationsrunde in den tieferen Wettbewerb aufnehmen (falls vorhanden).</summary>
    public static void ApplyDrop(World world, GameData data, int clubId, string toCompetition, Rng rng)
    {
        if (world.FindCup(toCompetition) is not { Kind: CompetitionKind.Europe } cup) return;
        if (cup.ParticipantIds.Contains(clubId)) return;
        cup.ParticipantIds.Add(clubId);
        // Nächste noch nicht ausgeloste Qualirunde bzw. Ligaphase.
        var round = cup.Rounds.FirstOrDefault(r => !r.Drawn && r.StageType == "knockout" && r.StageId is "q1" or "q2" or "q3" or "po")
                    ?? cup.Rounds.FirstOrDefault(r => r.StageType == "league" && !LeagueRoundComplete(cup, r));
        if (round is null) return;
        round.Entrants.Add(clubId);
        TouchEntry(world, toCompetition, clubId, round.Number - 2, eliminated: false);
        // Wenn die Runde schon läuft (Ties), nicht neu auslosen; Entrant wartet auf die nächste freie Runde.
        if (!round.Drawn && round.StageType == "knockout" && round.Entrants.Count >= 2
            && cup.Rounds.Take(round.Number - 1).All(r => r.Complete || r.Entrants.Count == 0 && !r.Drawn))
        {
            // Nur auslosen, wenn alle vorherigen Runden durch sind und diese noch leer an Ties ist.
        }
        _ = data;
        _ = rng;
    }
}
