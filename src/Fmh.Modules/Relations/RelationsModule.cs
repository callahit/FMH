using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Kabine und Spielerbeziehungen (Must-now E3 / AP 7): Zufriedenheit, Gespräche, Versprechen, Führung, Stimmung.</summary>
public sealed partial class RelationsModule : GameModule, IRelationsService
{
    public override string Id => "relations";
    public override IReadOnlyList<string> DependsOn => new[] { "matchday", "board" };

    public override void OnDay(DayContext day)
    {
        CheckPromises();
        if (World.Date.DayOfWeek == DayOfWeek.Monday)
        {
            foreach (var club in World.Clubs.Values.OrderBy(c => c.Id))
            {
                EnsureLeaders(club);
                RecalculateClub(club);
                MentoringWeekly(club);
                CaptainMoodWeekly(club);
                UnhappyWeekly(club);
            }
        }
    }

    public PlayerRelations Of(Player p)
    {
        if (!World.PlayerRelations.TryGetValue(p.Id, out var r))
        {
            r = new PlayerRelations { ExpectedStatus = p.Contract?.Status };
            World.PlayerRelations[p.Id] = r;
        }
        return r;
    }

    public ClubRelations ClubOf(Club club)
    {
        if (!World.ClubRelations.TryGetValue(club.Id, out var r))
        {
            r = new ClubRelations { ClubId = club.Id };
            World.ClubRelations[club.Id] = r;
            EnsureLeaders(club);
        }
        return r;
    }

    public string? MoodReportKey(Club club)
    {
        var mood = ClubOf(club).SquadMood;
        if (mood >= 70) return "match.mood.motivated";
        if (mood <= 40) return "match.mood.apathetic";
        return null;
    }

    /// <summary>Erwartete Einsatzquote (0–1) zum Kaderstatus; Ambition hebt die Erwartung.</summary>
    public static double ExpectedMinutesShare(SquadStatus status, int ambition) =>
        status switch
        {
            SquadStatus.Star => 0.82,
            SquadStatus.Regular => 0.68,
            SquadStatus.Rotation => 0.42,
            SquadStatus.Backup => 0.18,
            _ => 0.08,
        } + Math.Clamp((ambition - 10) * 0.008, -0.05, 0.08);

    public void Recalculate(Player p)
    {
        if (p.ClubId is not { } cid || !World.Clubs.TryGetValue(cid, out var club) || p.IsYouth || p.Retired)
            return;
        var rel = Of(p);
        var reasons = new List<(string Key, int Delta)>();

        // Einsatzzeit gegen Kaderstatus (und implizite Status-Erwartung).
        var status = rel.ExpectedStatus ?? p.Contract?.Status ?? SquadStatus.Rotation;
        var league = World.FindLeague(club.LeagueId);
        var matchdays = Math.Max(1, league?.CurrentMatchday ?? 1);
        var available = matchdays * 90.0;
        var share = available <= 0 ? 0 : p.SeasonStats.Minutes / available;
        var expected = ExpectedMinutesShare(status, p.Hidden.Ambition);
        var playDelta = (int)Math.Round(Math.Clamp((share - expected) / Math.Max(0.15, expected) * 25, -30, 20));
        if (playDelta <= -8) reasons.Add(("sat.reason.playingTime.low", playDelta));
        else if (playDelta >= 8) reasons.Add(("sat.reason.playingTime.high", playDelta));
        else reasons.Add(("sat.reason.playingTime.ok", playDelta));

        // Gehalt gegen Teamgefüge (Peers mit ähnlicher Stärke).
        var wage = p.Contract?.WeeklyWage ?? 0;
        var peers = World.SquadOf(club).Where(x => !x.IsYouth && x.Contract is not null && x.Id != p.Id).ToList();
        if (peers.Count > 0 && wage > 0)
        {
            var peerWage = peers.Average(x => (double)x.Contract!.WeeklyWage);
            var wageRatio = wage / Math.Max(1, peerWage);
            var wageDelta = (int)Math.Round(Math.Clamp((wageRatio - 1) * 40, -20, 15));
            if (wageDelta <= -6) reasons.Add(("sat.reason.wage.low", wageDelta));
            else if (wageDelta >= 6) reasons.Add(("sat.reason.wage.high", wageDelta));
            else reasons.Add(("sat.reason.wage.ok", wageDelta));
        }

        // Vereinserfolg gegen Saisonziel.
        if (league is not null)
        {
            var table = LeagueTable.Compute(league);
            var pos = table.FindIndex(r => r.ClubId == club.Id) + 1;
            if (pos > 0)
            {
                var goalPos = Board.GoalPosition(club, club.Board.Goal);
                var gap = goalPos - pos;
                var goalDelta = (int)Math.Round(Math.Clamp(gap * 3.5 + (pos <= goalPos ? 5 : -5), -20, 15));
                if (goalDelta <= -6) reasons.Add(("sat.reason.goals.bad", goalDelta));
                else if (goalDelta >= 6) reasons.Add(("sat.reason.goals.good", goalDelta));
                else reasons.Add(("sat.reason.goals.ok", goalDelta));
            }
        }

        // Offene / gebrochene Versprechen.
        foreach (var promise in World.Promises.Where(x => x.PlayerId == p.Id && x.ClubId == club.Id))
        {
            if (promise.Outcome == PromiseOutcome.Broken)
                reasons.Add(("sat.reason.promise.broken", -22));
            else if (promise.Outcome == PromiseOutcome.Held)
                reasons.Add(("sat.reason.promise.held", 10));
        }

        // Loyalität dämpft Unzufriedenheit, Temperament verstärkt sie.
        var raw = 55 + reasons.Sum(r => r.Delta);
        raw += (p.Hidden.Loyalty - 10);
        if (raw < 45) raw -= Math.Max(0, 12 - p.Hidden.Temperament) / 2;
        rel.Satisfaction = Math.Clamp(raw, 0, 100);
        rel.ReasonKeys = reasons.OrderBy(r => r.Delta).Select(r => r.Key).Distinct().Take(4).ToList();

        // Moral grob an Zufriedenheit koppeln (nicht jeden Tag schwanken lassen).
        var targetMorale = rel.Satisfaction switch
        {
            < 20 => Morale.VeryBad,
            < 35 => Morale.Bad,
            < 45 => Morale.Poor,
            < 58 => Morale.Okay,
            < 72 => Morale.Good,
            < 85 => Morale.VeryGood,
            _ => Morale.Superb,
        };
        if (Math.Abs((int)p.State.Morale - (int)targetMorale) >= 2)
            p.State.Morale = (Morale)Math.Clamp((int)p.State.Morale + Math.Sign((int)targetMorale - (int)p.State.Morale), 0, 6);
    }

    public void RecalculateClub(Club club)
    {
        var squad = World.SquadOf(club).Where(p => !p.IsYouth && !p.Retired).ToList();
        foreach (var p in squad) Recalculate(p);
        var cr = ClubOf(club);
        cr.SquadMood = squad.Count == 0 ? 55 : squad.Average(p => Of(p).Satisfaction);
    }

    public void ApplyMatchMood(MatchTeamState team)
    {
        var mood = ClubOf(team.Club).SquadMood;
        var bonus = (mood - 55) / 50.0 * 1.6; // etwa −1,8 … +1,4 Attributpunkte Tagesform
        var captainId = team.Tactic.CaptainId;
        if (captainId is { } cap && World.Players.TryGetValue(cap, out var captain) && captain.ClubId == team.Club.Id)
        {
            var capSat = Of(captain).Satisfaction;
            bonus += (capSat - 55) / 100.0; // Kapitän zieht die Stimmung leicht mit
        }
        foreach (var mp in team.OnPitch.Concat(team.Bench))
        {
            mp.DayForm += bonus;
            if (mood >= 70 && Rng.Chance(0.25))
                mp.Morale = (Morale)Math.Min(6, (int)mp.Morale + 1);
            if (mood <= 40 && Rng.Chance(0.25))
                mp.Morale = (Morale)Math.Max(0, (int)mp.Morale - 1);
        }
    }

    public void RecordNegotiatedStatus(Player p, SquadStatus status)
    {
        var rel = Of(p);
        rel.ExpectedStatus = status;
        if (p.ClubId is not { } cid) return;
        // Implizites Versprechen: Status bleibt die Saison über glaubwürdig (Spielzeit).
        if (World.Promises.Any(x => x.PlayerId == p.Id && x.Implicit && x.Outcome == PromiseOutcome.Pending && x.Kind == PromiseKind.SquadRole))
            return;
        World.Promises.Add(new PlayerPromise
        {
            Id = World.NextPromiseId++,
            PlayerId = p.Id,
            ClubId = cid,
            Kind = PromiseKind.SquadRole,
            MadeOn = World.Date,
            Deadline = new DateOnly(World.SeasonYear + 1, 5, 31),
            Outcome = PromiseOutcome.Pending,
            TargetStatus = status,
            Implicit = true,
        });
    }
}
