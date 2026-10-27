using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Zufallsereignisse (Kapitel 10, Anstoß-Herzstück): Pool aus Datendateien (events/*.json), gewichtet nach Kontext,
/// Tonfall und Häufigkeit. Ereignisse mit mehreren Optionen sind Pflicht-Entscheidungen im Posteingang.
/// </summary>
public sealed partial class EventsModule : GameModule, IEventsService
{
    public override string Id => "events";

    public const int EventDecisionDays = 7;

    private double EventChancePerDay => HumanManager.Profile.Events switch
    {
        EventFrequency.Off => 0,
        EventFrequency.Rare => 0.02,
        EventFrequency.Often => 0.09,
        _ => 0.045,
    };

    public void EventsDaily()
    {
        foreach (var pending in World.Events.Where(e => e.Chosen is null && e.Expires < World.Date).ToList())
        {
            var def = Data.Events.FirstOrDefault(d => d.Id == pending.EventId);
            ResolveEvent(pending, def?.Options.LastOrDefault()?.Id ?? "");
        }
        if (!Rng.Chance(EventChancePerDay) || Board.AwaitingNewJob) return;
        TriggerRandomEvent();
    }

    /// <summary>Zieht ein passendes Ereignis und legt es vor (öffentlich für Tests).</summary>
    public PendingEvent? TriggerRandomEvent(string? forceId = null)
    {
        var club = HumanClub;
        var eligible = Data.Events.Where(d => forceId is null ? Eligible(d, club) : d.Id == forceId).ToList();
        if (eligible.Count == 0) return null;
        var def = eligible[Rng.WeightedIndex(eligible.Select(d => d.Weight).ToList())];
        var player = PickEventPlayer(club, def.When.Player);
        if (def.When.Player is not null && player is null) return null;
        var others = World.Clubs.Values.Where(c => c.Id != club.Id && World.FindLeague(c.LeagueId) is not null).ToList();
        var other = others.Count == 0 ? club : Rng.Pick(others);
        var amount = def.Options.Select(o => Math.Abs(o.Effects.GetValueOrDefault("money"))).DefaultIfEmpty(0).Max();
        var pending = new PendingEvent
        {
            Id = World.NextEventId++, EventId = def.Id, ClubId = club.Id, PlayerId = player?.Id, Date = World.Date,
            Expires = World.Date.AddDays(EventDecisionDays),
            Args = new List<TextArg>
            {
                TextArg.Literal(club.Name), TextArg.Literal(player?.Name ?? ""), TextArg.Money(MarketValue.Round(amount * FacilitiesModule.Scale(club)), Currency),
                TextArg.Literal(other.Name), TextArg.Literal(HumanManager.Name), TextArg.Literal(club.City), TextArg.Literal(Sponsors.StadiumDisplayName(club)),
            },
        };
        World.EventHistory[def.Id] = World.Date;
        World.Events.Add(pending);
        if (def.Options.Count <= 1)
        {
            ResolveEvent(pending, def.Options.FirstOrDefault()?.Id ?? "", announce: true);
            return pending;
        }
        Notify(MessageType.Event, "msg.sender.event", TextRef.Of("event." + def.Id + ".title", pending.Args.Cast<object?>().ToArray()),
            TextRef.Of("event." + def.Id + ".text", pending.Args.Cast<object?>().ToArray()), MessageAction.Event, pending.Id, required: true);
        return pending;
    }

    private bool Eligible(EventDef d, Club club)
    {
        if (!Press.StyleFits(d.Styles)) return false;
        if (World.EventHistory.TryGetValue(d.Id, out var last) && last.AddDays(d.CooldownDays) > World.Date) return false;
        var w = d.When;
        if (w.MinReputation is { } min && club.Reputation < min) return false;
        if (w.MaxReputation is { } max && club.Reputation > max) return false;
        if (w.Months is { Count: > 0 } months && !months.Contains(World.Date.Month)) return false;
        if (w.MinFanMood is { } fmin && club.Fans.Mood < fmin) return false;
        if (w.MaxFanMood is { } fmax && club.Fans.Mood > fmax) return false;
        if (w.MaxBoardTrust is { } bt && club.Board.Trust > bt) return false;
        if (w.RequiresBuilding is { } b && Facilities.BuildingLevel(club, b) == 0) return false;
        if (w.NoStadiumSite == true && (StadiumPlans.AvailableSite(club) is not null || club.Grounds.Projects.Any(p => p.Kind == ProjectKind.NewStadium && p.State != ProjectState.Done))) return false;
        if (w.LowBalance == true && club.Budget.Balance > Finance.ExpectedIncome(club) * 0.1) return false;
        var last1 = AllFixtures.Where(x => x.Involves(club.Id) && x.Result is not null).OrderByDescending(x => x.Date).FirstOrDefault();
        if (w.AfterWin == true && (last1 is null || PressModule.Outcome(club, last1) <= 0 || last1.Date < World.Date.AddDays(-4))) return false;
        if (w.AfterLoss == true && (last1 is null || PressModule.Outcome(club, last1) >= 0 || last1.Date < World.Date.AddDays(-4))) return false;
        return true;
    }

    private Player? PickEventPlayer(Club club, string? filter)
    {
        if (filter is null) return null;
        var squad = World.SquadOf(club).Where(p => p.Loan is null || p.Loan.ParentClubId != club.Id).ToList();
        var pool = filter switch
        {
            "young" => squad.Where(p => p.Age(World.Date) <= 21),
            "star" => squad.OrderByDescending(Transfers.Rating).Take(4),
            "striker" => squad.Where(p => p.MainPosition is Position.ST or Position.AMC),
            "keeper" => squad.Where(p => p.MainPosition == Position.GK),
            "injured" => squad.Where(p => p.State.Injury is not null),
            "old" => squad.Where(p => p.Age(World.Date) >= 30),
            "foreign" => squad.Where(p => !p.Nationalities.Contains(club.CountryCode)),
            "defender" => squad.Where(p => PositionInfo.IsDefender(p.MainPosition)),
            _ => squad,
        };
        var list = pool.Where(p => p.IsAvailable || filter == "injured").ToList();
        return list.Count == 0 ? null : Rng.Pick(list);
    }

    /// <summary>Entscheidung umsetzen (Option per Id), Wirkung als Nachricht zusammenfassen.</summary>
    public string? ResolveEvent(PendingEvent pending, string optionId, bool announce = false)
    {
        if (pending.Chosen is not null) return "event.error.done";
        var def = Data.Events.FirstOrDefault(d => d.Id == pending.EventId);
        var option = def?.Options.FirstOrDefault(o => o.Id == optionId) ?? def?.Options.LastOrDefault();
        pending.Chosen = option?.Id ?? "";
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.Event && m.ActionRefId == pending.Id)) m.Resolved = true;
        if (def is null || option is null) return null;
        var club = World.Clubs[pending.ClubId];
        var success = Rng.Chance(option.Chance);
        var effects = success || option.FailEffects is null ? option.Effects : option.FailEffects;
        var player = pending.PlayerId is { } pid && World.Players.TryGetValue(pid, out var p) ? p : null;
        var summary = ApplyEffects(club, player, effects);
        var args = pending.Args.Cast<object?>().ToArray();
        var outcomeKey = option.FailEffects is null ? "event.outcome" : success ? "event.outcome.success" : "event.outcome.fail";
        Notify(MessageType.Event, "msg.sender.event",
            TextRef.Of(announce ? "event." + def.Id + ".title" : "msg.eventResult.subject", announce ? args : new object?[] { TextRef.Of("event." + def.Id + ".title", args) }),
            TextRef.Of("msg.eventResult.body",
                announce ? TextRef.Of("event." + def.Id + ".text", args) : TextRef.Of("event." + def.Id + ".opt." + option.Id, args),
                TextRef.Of(outcomeKey), TextArg.Join("\n", summary.Select(s => (object?)s))));
        return null;
    }

    /// <summary>Wirkungen anwenden; Rückgabe: Liste der Änderungen für die Zusammenfassung.</summary>
    public List<TextRef> ApplyEffects(Club club, Player? player, Dictionary<string, double> effects)
    {
        var lines = new List<TextRef>();
        foreach (var (key, value) in effects)
        {
            if (value == 0) continue;
            switch (key)
            {
                case "money":
                    var money = MarketValue.Round(value * FacilitiesModule.Scale(club));
                    Finance.Book(club, TransactionType.Event, money, TR("tx.event"));
                    lines.Add(TR("effect.money", TextArg.Money(money, Currency)));
                    continue;
                case "fans": club.Fans.Mood = Math.Clamp(club.Fans.Mood + value, 0, 100); break;
                case "board": Board.ChangeTrust(club, value); break;
                case "morale": Press.TeamMorale(club, Math.Clamp(value, -1, 1)); break;
                case "media": HumanManager.Career.Media = Math.Clamp(HumanManager.Career.Media + value, 0, 100); break;
                case "reputation":
                    if (Rng.Chance(Math.Abs(value))) club.Reputation = Math.Clamp(club.Reputation + Math.Sign(value), 1, 20);
                    break;
                case "stadiumSite":
                    StadiumPlans.OfferStadiumSite(club);
                    lines.Add(TR("effect.stadiumSite", club.Grounds.Site!.MaxCapacity, TextArg.OfDate(club.Grounds.Site.Expires)));
                    continue;
                case "pitch": club.Stadium.Pitch = Math.Clamp(club.Stadium.Pitch + value, 0, 100); break;
                case "playerMorale" when player is not null:
                    player.State.Morale = (Morale)Math.Clamp((int)player.State.Morale + (int)Math.Round(value), 0, 6); break;
                case "playerCondition" when player is not null:
                    player.State.Condition = Math.Clamp(player.State.Condition + value, 30, 100); break;
                case "playerInjury" when player is not null:
                    Matchday.InjurePlayer(player, "bruise", (int)value, notify: false); break;
                case "playerSuspend" when player is not null:
                    player.State.SuspendedMatches += (int)value; break;
                case "playerFine" when player is not null:
                    var fine = (long)(player.Contract?.WeeklyWage ?? 0) * (long)value;
                    if (fine > 0) Finance.Book(club, TransactionType.Event, fine, TR("tx.playerFine", player.Name));
                    lines.Add(TR("effect.playerFine", player.Name, TextArg.Money(fine, Currency)));
                    continue;
                case "playerForm" when player is not null:
                    player.State.MatchPractice = Math.Clamp(player.State.MatchPractice + value, 0, 100); break;
                default:
                    continue;
            }
            lines.Add(TR("effect." + key + (value > 0 ? ".up" : ".down"), player?.Name ?? "", TextArg.Num(Math.Abs(value), "0")));
        }
        if (lines.Count == 0) lines.Add(TR("effect.none"));
        return lines;
    }

    public PendingEvent? EventOf(Message m) =>
        m.Action == MessageAction.Event ? World.Events.FirstOrDefault(e => e.Id == m.ActionRefId) : null;
}
