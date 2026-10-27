using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

public sealed record CommentaryLine(MatchEvent Event, string Minute, string Text, bool Highlight);

/// <summary>
/// Erzeugt aus dem Ereignisprotokoll Kommentarzeilen. Die Textbausteine stehen in
/// lang/&lt;sprache&gt;.commentary.json und sind von der Community erweiterbar (Kapitel 12, Modding).
/// </summary>
public sealed class CommentaryGenerator
{
    private readonly GameData _data;
    private readonly IMatchSimulation _sim;
    private readonly Dictionary<int, Player> _players = new();

    public CommentaryGenerator(GameData data, IMatchSimulation sim)
    {
        _data = data;
        _sim = sim;
        foreach (var team in new[] { sim.Home, sim.Away })
            foreach (var mp in team.AllPlayers)
                _players[mp.Player.Id] = mp.Player;
    }

    /// <summary>Kommentarmodus: fast alle Ereignisse. Tickermodus: nur Schlüsselszenen.</summary>
    public CommentaryLine? Describe(MatchEvent e, bool tickerOnly)
    {
        if (tickerOnly && !e.Key) return null;
        var key = TemplateKey(e);
        if (key is null) return null;
        if (!_data.Commentary.TryGetValue(key, out var templates) || templates.Count == 0) return null;

        // Eigene Zufallsfolge je Ereignis: gleiche Spiele ergeben gleiche Kommentare.
        var rng = new Rng(Rng.Mix(_sim.Setup.Seed, (ulong)e.Index + 1));
        // Nicht jeder Angriff bekommt eine Zeile, sonst wird der Kommentar zu dicht.
        if (e.Type is MatchEventType.Attack && rng.Chance(0.35)) return null;

        var text = Fill(rng.Pick(templates), e);
        var highlight = e.Type is MatchEventType.Goal or MatchEventType.ShootoutEnd or MatchEventType.RedCard or MatchEventType.SecondYellow or MatchEventType.Penalty;
        return new CommentaryLine(e, e.MinuteLabel, text, highlight);
    }

    private static string? TemplateKey(MatchEvent e) => e.Type switch
    {
        MatchEventType.KickOff => "kickoff",
        MatchEventType.SecondHalfKickOff => "secondHalf",
        MatchEventType.HalfTime => "halfTime",
        MatchEventType.FullTime => "fullTime",
        MatchEventType.EndOfRegularTime => "endOfRegularTime",
        MatchEventType.ExtraTimeStart => "extraTime",
        MatchEventType.ExtraTimeSecondHalf => "extraTimeSecondHalf",
        MatchEventType.ShootoutStart => "shootoutStart",
        MatchEventType.ShootoutKick => e.Outcome == ShotOutcome.Goal ? "shootoutKick.goal" : e.Outcome == ShotOutcome.Saved ? "shootoutKick.saved" : "shootoutKick.miss",
        MatchEventType.ShootoutEnd => "shootoutEnd",
        MatchEventType.Stoppage => "stoppage",
        MatchEventType.Attack => "attack",
        MatchEventType.Counter => "counter",
        MatchEventType.Dribble => "dribble",
        MatchEventType.Cross => "cross",
        MatchEventType.AerialWon => "aerialWon",
        MatchEventType.Shot => ShotKey(e),
        MatchEventType.Goal => e.Kind switch
        {
            ShotKind.Header => "goal.header",
            ShotKind.LongShot => "goal.longShot",
            ShotKind.FreeKick => "goal.freeKick",
            ShotKind.Penalty => "goal.penalty",
            ShotKind.OneOnOne => "goal.oneOnOne",
            _ => "goal",
        },
        MatchEventType.Foul => e.Value > 0 ? "foul.dangerous" : "foul",
        MatchEventType.YellowCard => "yellow",
        MatchEventType.SecondYellow => "secondYellow",
        MatchEventType.RedCard => "red",
        MatchEventType.FreeKick => e.Value > 0 ? "freeKick.direct" : "freeKick",
        MatchEventType.Penalty => "penalty",
        MatchEventType.Corner => "corner",
        MatchEventType.Offside => "offside",
        MatchEventType.GoalKick => "goalKick",
        MatchEventType.Injury => "injury",
        MatchEventType.Substitution => "substitution",
        _ => null,
    };

    private static string ShotKey(MatchEvent e)
    {
        var kind = e.Kind switch
        {
            ShotKind.Header => "header",
            ShotKind.Penalty => "penalty",
            ShotKind.FreeKick => "freeKick",
            ShotKind.LongShot => "longShot",
            ShotKind.OneOnOne => "oneOnOne",
            _ => "normal",
        };
        var outcome = e.Outcome switch
        {
            ShotOutcome.Saved => "saved",
            ShotOutcome.Blocked => "blocked",
            ShotOutcome.Post => "post",
            _ => "wide",
        };
        return $"shot.{kind}.{outcome}";
    }

    private string Fill(string template, MatchEvent e)
    {
        var team = _sim.Team(e.Side);
        var other = _sim.Team(e.Side.Other());
        var gk = other.OnPitch.Concat(other.Off).FirstOrDefault(p => p.Position == Position.GK);
        var side = e.Col <= 1 ? _data.Texts["comm.side.left"] : e.Col >= 3 ? _data.Texts["comm.side.right"] : _data.Texts["comm.side.centre"];
        return template
            .Replace("{p}", Name(e.PlayerId))
            .Replace("{o}", Name(e.OtherPlayerId))
            .Replace("{g}", gk?.Player.ShortName ?? _data.Texts["comm.keeper"])
            .Replace("{t}", team.Club.Name)
            .Replace("{other}", other.Club.Name)
            .Replace("{home}", _sim.Home.Club.Name)
            .Replace("{away}", _sim.Away.Club.Name)
            .Replace("{score}", $"{e.HomeScore}:{e.AwayScore}")
            .Replace("{side}", side)
            .Replace("{pens}", $"{e.Value / 100}:{e.Value % 100}")
            .Replace("{n}", e.Value.ToString())
            .Replace("{m}", e.Value.ToString());
    }

    private string Name(int? id) => id is { } v && _players.TryGetValue(v, out var p) ? p.ShortName : "?";
}
