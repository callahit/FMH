using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Modules;

/// <summary>
/// Nationaltrainer (AP 8c): Jedes Land mit Spielern hat einen Trainer. Freie Posten bietet der Verband zuerst dem
/// Manager an (ab einem Mindestruf, auch neben seinem Verein), sonst übernimmt ein vereinsloser KI-Manager. Nach jedem
/// Turnier wird abgerechnet: Ziel erreicht verlängert bis zum nächsten Turnier, verfehlt kostet den Posten.
/// </summary>
public sealed partial class InternationalsModule
{
    private NationalCoachRules CoachRules => Data.Internationals.Coach;

    /// <summary>Stabile Kennung eines Landes für Posteingangs-Verweise.</summary>
    public static int RefId(string code) => code.Aggregate(17, (h, ch) => unchecked(h * 31 + ch));

    /// <summary>Das Land, dessen Nationaltrainer der menschliche Manager ist.</summary>
    public NationalTeam? HumanNation => World.NationalTeams.FirstOrDefault(t => t.CoachId == HumanManager.Id);

    /// <summary>Offenes Angebot an den Manager.</summary>
    public NationalTeam? PendingOffer => World.NationalTeams.FirstOrDefault(t => t.OfferExpires is { } d && d >= World.Date);

    /// <summary>Freie Posten besetzen: zuerst ein Angebot an den Manager, sonst ein vereinsloser KI-Manager.</summary>
    private void EnsureCoaches()
    {
        NationalSquads.Ensure(World, Data);
        // Verfallenes Angebot gilt als Absage: Der Posten geht an einen KI-Manager statt erneut an den Manager.
        foreach (var t in World.NationalTeams.Where(t => t.OfferExpires is { } d && d < World.Date).ToList()) DeclineNationalJob(t);
        foreach (var team in World.NationalTeams.Where(t => !t.Background && t.CoachId is null && t.OfferExpires is null).ToList())
        {
            if (CanOfferToHuman())
            {
                Offer(team);
                continue;
            }
            if (BestFreeCoach(team) is { } coach) Appoint(team, coach);
        }
    }

    private bool CanOfferToHuman() =>
        HumanNation is null && PendingOffer is null && HumanManager.Reputation >= CoachRules.OfferReputation;

    private void Offer(NationalTeam team)
    {
        team.OfferExpires = World.Date.AddDays(CoachRules.OfferDays);
        Notify(MessageType.Career, "msg.sender.association", TR("intl.coachOffer.subject", team.Name),
            TR("intl.coachOffer.body", team.Name, TR("intl.target." + Target(team)), NextTournamentYear()),
            MessageAction.NationalJobOffer, RefId(team.Code), required: false);
    }

    /// <summary>Bester vereinsloser KI-Manager, Landsleute zuerst; jeder Manager trainiert höchstens ein Land.</summary>
    private Manager? BestFreeCoach(NationalTeam team)
    {
        var busy = World.NationalTeams.Where(t => t.CoachId is not null).Select(t => t.CoachId!.Value).ToHashSet();
        return World.Managers.Where(m => !m.Human && m.ClubId is null && !busy.Contains(m.Id))
            .OrderByDescending(m => m.Traits.Nationality == team.Code)
            .ThenByDescending(m => m.Reputation).ThenBy(m => m.Id).FirstOrDefault();
    }

    private void Appoint(NationalTeam team, Manager coach)
    {
        team.CoachId = coach.Id;
        team.CoachTarget = Target(team);
        team.CoachUntil = NextTournamentYear();
        team.OfferExpires = null;
    }

    /// <summary>Erwartung des Verbands nach der Stärkerangliste aller Nationalteams.</summary>
    private string Target(NationalTeam team)
    {
        var rank = World.NationalTeams.OrderByDescending(t => InternationalTournaments.Rating(World, Data, t))
            .ThenBy(t => t.Code, StringComparer.Ordinal).ToList().IndexOf(team) + 1;
        return rank <= CoachRules.FinalRank ? "KO2" : rank <= CoachRules.SemiFinalRank ? "KO4" : "Q";
    }

    private int NextTournamentYear()
    {
        var years = Data.Internationals.Tournaments.Select(r =>
        {
            var y = Math.Max(r.FirstYear, World.SeasonYear + 1);
            // Ein schon gespieltes Turnier (Endrunde vor dem Saisonwechsel) zählt nicht mehr.
            while (!InternationalTournaments.IsTournamentYear(r, y)
                   || World.Tournaments.Any(t => t.Id == InternationalTournaments.Key(r, y) && t.Phase == TournamentPhase.Done)) y++;
            return y;
        }).ToList();
        return years.Count == 0 ? World.SeasonYear + 2 : years.Min();
    }

    /// <summary>Angebot annehmen: Der Manager wird Nationaltrainer, sein Verein bleibt.</summary>
    public string? AcceptNationalJob(NationalTeam team)
    {
        if (team.OfferExpires is not { } d || d < World.Date) return "career.error.offer";
        if (HumanNation is not null) return "intl.error.alreadyCoach";
        Appoint(team, HumanManager);
        team.PlannedSquad = NationalSquads.Nominate(World, Data, team);
        Resolve(team);
        Notify(MessageType.Career, "msg.sender.association", TR("intl.coachWelcome.subject", team.Name),
            TR("intl.coachWelcome.body", team.Name, TR("intl.target." + team.CoachTarget), team.CoachUntil));
        return null;
    }

    public void DeclineNationalJob(NationalTeam team)
    {
        team.OfferExpires = null;
        Resolve(team);
        // Der Posten geht an einen KI-Manager; ein neues Angebot an den Manager gibt es erst nach dem nächsten Turnier.
        if (BestFreeCoach(team) is { } coach) Appoint(team, coach);
    }

    /// <summary>Rücktritt als Nationaltrainer.</summary>
    public void ResignNationalJob()
    {
        if (HumanNation is not { } team) return;
        team.CoachId = null;
        team.PlannedSquad.Clear();
        if (BestFreeCoach(team) is { } coach) Appoint(team, coach);
    }

    private void Resolve(NationalTeam team)
    {
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.NationalJobOffer && m.ActionRefId == RefId(team.Code)))
            m.Resolved = true;
    }

    /// <summary>Kader des menschlichen Nationaltrainers: seine Auswahl, soweit spielberechtigt, Rest nach KI-Vorschlag.</summary>
    private List<int> SquadFor(NationalTeam team, IReadOnlySet<int> taken)
    {
        var ai = NationalSquads.Nominate(World, Data, team, taken);
        if (team.CoachId != HumanManager.Id || team.PlannedSquad.Count == 0) return ai;
        var size = Data.Internationals.SquadSize;
        var chosen = team.PlannedSquad.Where(id => !taken.Contains(id) && World.Players.TryGetValue(id, out var p)
            && NationalSquads.Eligible(p, team.Code, World, Data)).Distinct().Take(size).ToList();
        chosen.AddRange(ai.Where(id => !chosen.Contains(id)).Take(size - chosen.Count));
        return chosen;
    }

    /// <summary>Nach einem Turnier: Ziel erreicht → Verlängerung bis zum nächsten Turnier, sonst muss der Trainer gehen.</summary>
    private void SettleCoaches(InternationalTournament t)
    {
        foreach (var team in World.NationalTeams.Where(n => !n.Background && n.CoachId is not null && n.CoachUntil <= t.Year))
        {
            var reached = TargetReached(t, team);
            var human = team.CoachId == HumanManager.Id;
            if (reached)
            {
                team.CoachTarget = Target(team);
                team.CoachUntil = NextTournamentYear();
                if (human)
                    Notify(MessageType.Career, "msg.sender.association", TR("intl.coachExtended.subject", team.Name),
                        TR("intl.coachExtended.body", team.Name, t.Name, TR("intl.target." + team.CoachTarget), team.CoachUntil));
                continue;
            }
            if (human)
            {
                HumanManager.Reputation = Math.Max(1, HumanManager.Reputation - 1);
                Notify(MessageType.Career, "msg.sender.association", TR("intl.coachSacked.subject", team.Name),
                    TR("intl.coachSacked.body", team.Name, t.Name));
            }
            team.CoachId = null;
            team.PlannedSquad.Clear();
        }
    }

    /// <summary>Ziel erreicht? Q: Endrunde erreicht, KO4: im Halbfinale, KO2: im Finale (Turniere mit kleinerem Feld zählen wie erreicht).</summary>
    public bool TargetReached(InternationalTournament t, NationalTeam team)
    {
        if (!t.Qualified.Contains(team.Code)) return team.CoachTarget == "" || !Participants().Any(p => p.Code == team.Code);
        if (team.CoachTarget is not ("KO4" or "KO2")) return true;
        var stages = FixturesOf(t, "KO").Where(f => f.Involves(team.Code)).Select(f => f.Stage).ToList();
        var needed = int.Parse(team.CoachTarget[2..]);
        var reachedStage = stages.Count == 0 ? int.MaxValue : stages.Min(s => int.Parse(s[2..]));
        return reachedStage <= needed;
    }
}
