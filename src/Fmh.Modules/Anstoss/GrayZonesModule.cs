using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Grauzonen (AP 13c, Anstoß-Element <c>grayZones</c>): Schiedsrichter beeinflussen und verbotene Mittel wirken im
/// nächsten Pflichtspiel auf die Tagesform, der Vertragstrick senkt die Gehaltsforderung. Jede Tat kann auffliegen:
/// Geldstrafe, Vertrauens- und Rufverlust, Punktabzug, Sperren; ab der Grenze folgt die Entlassung. Der Zufall läuft
/// über einen eigenen Strom, damit ein Spiel ohne Taten unverändert bleibt.
/// </summary>
public sealed class GrayZonesModule : GameModule
{
    private const ulong Stream = 0x6A7_2013_C0DEUL;

    public override string Id => "grayZones";
    public override IReadOnlyList<string> DependsOn => new[] { "board", "matchday" };

    private bool On => Data.GrayZones is not null && Anstoss.IsOn(World, HumanManager.Id, GrayZones.Feature);

    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchStarting>(e => Boost(e.Fixture, e.Simulation));
        ctx.Bus.Subscribe<MatchPlayed>(e => Resolve(e.Fixture));
    }

    /// <summary>Akte des Menschen (wird beim ersten Eintrag angelegt).</summary>
    private GrayZoneFile File()
    {
        if (!World.GrayZones.TryGetValue(HumanManager.Id, out var file)) World.GrayZones[HumanManager.Id] = file = new GrayZoneFile();
        return file;
    }

    /// <summary>Tat ausführen; Fehlertext-Schlüssel oder null.</summary>
    public string? Use(string actionId, Negotiation? negotiation = null)
    {
        if (!On || Board.AwaitingNewJob) return "gray.error.off";
        var rules = Data.GrayZones!;
        if (GrayZones.Find(rules, actionId) is not { } action) return "gray.error.unknown";
        var file = World.GrayZones.GetValueOrDefault(HumanManager.Id);
        if (GrayZones.CoolingDown(rules, file, World.Date)) return "gray.error.cooldown";
        var club = HumanClub;
        var cost = GrayZones.Scaled(action.Cost, club);
        if (cost > 0 && club.Budget.Balance < cost) return "gray.error.money";
        if (action.Kind == GrayActionKind.Match)
        {
            if (NextFixture() is not { } f) return "gray.error.noFixture";
            if (file?.Deeds.Any(d => d.FixtureId == f.Id) == true) return "gray.error.planned";
            Pay(club, cost);
            File().Deeds.Add(new GrayDeed { Action = action.Id, Date = World.Date, FixtureId = f.Id });
            return null;
        }
        if (negotiation is not { WageDemand: { } demand } n || n.BuyerId != club.Id) return "gray.error.noNegotiation";
        Pay(club, cost);
        var deed = new GrayDeed { Action = action.Id, Date = World.Date, PlayerId = n.PlayerId };
        File().Deeds.Add(deed);
        n.WageDemand = (int)Math.Round(demand * (1 - action.WageCut));
        if (Detect(action, deed, World.Date.DayNumber))
        {
            n.WageDemand = demand;
            if (World.Players.TryGetValue(n.PlayerId, out var p))
                p.State.Morale = (Morale)Math.Clamp((int)p.State.Morale + action.Penalty.PlayerMorale, 0, 6);
            Penalize(action, deed, null);
        }
        return null;
    }

    private void Pay(Club club, long cost)
    {
        if (cost > 0) Finance.Book(club, TransactionType.Other, -cost, TR("tx.gray"));
    }

    /// <summary>Nächstes Pflichtspiel des Menschen (heute oder später).</summary>
    public Fixture? NextFixture() => World.AllMatches
        .Where(f => f.Kind != CompetitionKind.Friendly && f.Result is null && f.Date >= World.Date && f.Involves(HumanClub.Id))
        .OrderBy(f => f.Date).ThenBy(f => f.Id).FirstOrDefault();

    private GrayDeed? DeedFor(Fixture f) =>
        World.GrayZones.TryGetValue(HumanManager.Id, out var file) ? file.Deeds.FirstOrDefault(d => d.FixtureId == f.Id && !d.Resolved) : null;

    private void Boost(Fixture f, IMatchSimulation sim)
    {
        if (DeedFor(f) is not { } deed || !On || GrayZones.Find(Data.GrayZones!, deed.Action) is not { } action) return;
        var team = sim.Home.Club.Id == HumanClub.Id ? sim.Home : sim.Away.Club.Id == HumanClub.Id ? sim.Away : null;
        if (team is null) return;
        foreach (var mp in team.OnPitch.Concat(team.Bench)) mp.DayForm += action.DayForm;
        if (action.Penalty.PlayerBan > 0) deed.Players = team.OnPitch.Concat(team.Bench).Select(mp => mp.Player.Id).ToList();
    }

    private void Resolve(Fixture f)
    {
        if (f.Result is null || DeedFor(f) is not { } deed) return;
        deed.Resolved = true;
        if (!On || GrayZones.Find(Data.GrayZones!, deed.Action) is not { } action) return;
        if (Detect(action, deed, f.Id)) Penalize(action, deed, f);
    }

    private bool Detect(GrayAction action, GrayDeed deed, long salt)
    {
        var file = File();
        var rng = new Rng(Rng.Mix(World.Seed ^ Stream, (ulong)(salt * 131 + file.Deeds.IndexOf(deed))));
        deed.Resolved = true;
        deed.Detected = rng.Chance(action.Detection);
        return deed.Detected;
    }

    /// <summary>Folgen einer aufgeflogenen Tat; ab der Grenze Entlassung.</summary>
    private void Penalize(GrayAction action, GrayDeed deed, Fixture? f)
    {
        var club = HumanClub;
        var pen = action.Penalty;
        var fine = GrayZones.Scaled(pen.Fine, club);
        if (fine > 0) Finance.Book(club, TransactionType.Fines, -fine, TR("tx.grayFine"));
        Board.ChangeTrust(club, pen.Board);
        HumanManager.Career.Media = Math.Clamp(HumanManager.Career.Media + pen.Media, 0, 100);
        var points = 0;
        if (pen.Points > 0 && f is { Kind: CompetitionKind.League } && World.FindLeague(club.LeagueId) is { } league)
        {
            points = pen.Points;
            league.Deductions[club.Id] = league.Deductions.GetValueOrDefault(club.Id) + points;
        }
        foreach (var id in deed.Players)
            if (World.Players.TryGetValue(id, out var p) && p.ClubId == club.Id) p.State.SuspendedMatches += pen.PlayerBan;
        var file = File();
        file.Detected++;
        Notify(MessageType.Discipline, "msg.sender.grayZones", TR("gray.detected.subject"),
            TR("gray." + action.Id + ".detected", TextArg.Money(fine, Currency), points, pen.PlayerBan, deed.Players.Count));
        if (file.Detected < Data.GrayZones!.SackAfter) return;
        file.Detected = 0;
        Board.SackManager(TR("career.exit.grayZones"));
    }
}
