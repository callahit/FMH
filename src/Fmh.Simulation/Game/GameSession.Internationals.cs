using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: Nationalmannschaften, Turniere und das Amt des Nationaltrainers (AP 8).</summary>
public sealed partial class GameSession
{
    /// <summary>Land, das der Manager als Nationaltrainer betreut (neben seinem Verein), sonst null.</summary>
    public NationalTeam? HumanNation => _internationals.HumanNation;
    public NationalTeam? PendingNationalOffer => _internationals.PendingOffer;
    public NationalTeam? NationalOfferOf(Message m) =>
        World.NationalTeams.FirstOrDefault(t => InternationalsModule.RefId(t.Code) == m.ActionRefId && t.OfferExpires is { } d && d >= World.Date);
    public string? AcceptNationalJob(NationalTeam team) => _internationals.AcceptNationalJob(team);
    public void DeclineNationalJob(NationalTeam team) => _internationals.DeclineNationalJob(team);
    public void ResignNationalJob() => _internationals.ResignNationalJob();
    public bool OnNationalDuty(Player p) => _internationals.OnDuty(p);
    public bool TargetReached(InternationalTournament t, NationalTeam team) => _internationals.TargetReached(t, team);

    /// <summary>Spielberechtigte Kandidaten des eigenen Nationalteams, die Stärksten zuerst.</summary>
    public IReadOnlyList<Player> NationalCandidates(NationalTeam team) => World.Players.Values
        .Where(p => NationalSquads.Eligible(p, team.Code, World, Data))
        .OrderByDescending(p => NationalSquads.Score(p, Data)).ThenBy(p => p.Id).ToList();

    /// <summary>Vormerkliste des Nationaltrainers (höchstens Kadergröße).</summary>
    public void SetNationalSquad(IEnumerable<int> playerIds)
    {
        if (HumanNation is not { } team) return;
        team.PlannedSquad = playerIds.Distinct().Take(Data.Internationals.SquadSize).ToList();
    }

    /// <summary>KI-Vorschlag als Vormerkliste übernehmen.</summary>
    public void SuggestNationalSquad()
    {
        if (HumanNation is { } team) team.PlannedSquad = NationalSquads.Nominate(World, Data, team);
    }

    public void SetNationalFormation(string formationId)
    {
        if (HumanNation is { } team && Data.Formations.Any(f => f.Id == formationId)) team.Formation = formationId;
    }
}
