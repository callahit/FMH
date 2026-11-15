using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.MatchEngine;

/// <summary>
/// Textmodus: erzählt jedes Ereignis der Matchengine als kleine Szene in mehreren Zeilen. Ablauf aus
/// <see cref="SceneRules"/>, Texte aus den Szenenbausteinen der gewählten Sprache. Das Ergebnis entscheidet die Engine;
/// der Erzähler zieht aus einem eigenen Zufallsstrom und ändert weder Spielstand noch Statistik.
/// </summary>
public sealed class SceneNarrator
{
    private const ulong Stream = 0x5CE7_E7E1_1E2DUL;

    private readonly GameData _data;
    private readonly IMatchSimulation _sim;
    private readonly CommentaryGenerator _fallback;
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<Side, (string Root, int Minute)> _last = new();
    private readonly List<(RareSceneDef Scene, int Minute)> _rare = new();

    public SceneNarrator(GameData data, IMatchSimulation sim)
    {
        _data = data;
        _sim = sim;
        _fallback = new CommentaryGenerator(data, sim);
        foreach (var team in new[] { sim.Home, sim.Away })
            foreach (var mp in team.AllPlayers)
                _players[mp.Player.Id] = mp.Player;
        // Seltene Szenen: ob und wann, steht ab Anpfiff fest.
        var rng = new Rng(Rng.Mix(sim.Setup.Seed ^ Stream, 0));
        foreach (var rare in data.Scenes.Rare)
            if (rng.Chance(rare.Chance)) _rare.Add((rare, rng.Next(5, 88)));
    }

    /// <summary>Zeilen zu einem Ereignis (leer, wenn es still bleibt).</summary>
    public IReadOnlyList<CommentaryLine> Narrate(MatchEvent e)
    {
        var lines = new List<CommentaryLine>();
        var rng = new Rng(Rng.Mix(_sim.Setup.Seed ^ Stream, (ulong)e.Index + 1));
        foreach (var (scene, minute) in _rare.Where(r => r.Minute <= e.Minute && e.Type != MatchEventType.Goal).ToList())
        {
            _rare.Remove((scene, minute));
            if (Holds(scene.When, e)) AddSteps(lines, scene.Steps, e, rng, false);
        }

        var key = CommentaryGenerator.TemplateKey(e);
        if (key is not null && !(e.Type == MatchEventType.Attack && !rng.Chance(_data.Scenes.AttackShare)))
        {
            var candidates = _data.Scenes.Scenes.Where(s => s.On.Contains(key) && Holds(s.When, e)).ToList();
            if (candidates.Count > 0) AddSteps(lines, Weighted(candidates, rng).Steps, e, rng, Highlight(e));
            else if (_fallback.Plain(e) is { } plain) lines.Add(plain with { Highlight = Highlight(e) });
        }
        if (key is not null) _last[e.Side] = (key.Split('.')[0], e.Minute);
        return lines;
    }

    private static bool Highlight(MatchEvent e) =>
        e.Type is MatchEventType.Goal or MatchEventType.ShootoutEnd or MatchEventType.RedCard or MatchEventType.SecondYellow or MatchEventType.Penalty;

    private static SceneDef Weighted(List<SceneDef> list, Rng rng)
    {
        var roll = rng.NextDouble() * list.Sum(s => Math.Max(0, s.Weight));
        foreach (var s in list)
        {
            roll -= Math.Max(0, s.Weight);
            if (roll < 0) return s;
        }
        return list[^1];
    }

    private void AddSteps(List<CommentaryLine> lines, List<SceneStep> steps, MatchEvent e, Rng rng, bool highlightLast)
    {
        var start = lines.Count;
        // Platzhalter für Mitspieler und Gegenspieler bleiben innerhalb einer Szene gleich.
        var cast = Cast(e, rng);
        foreach (var step in steps)
        {
            if (!Holds(step.When, e) || !rng.Chance(step.Chance)) continue;
            if (!_data.Commentary.TryGetValue(step.Text, out var variants) || variants.Count == 0) continue;
            lines.Add(new CommentaryLine(e, e.MinuteLabel, Capitalize(Fill(rng.Pick(variants), e, cast)), false));
        }
        if (highlightLast && lines.Count > start) lines[^1] = lines[^1] with { Highlight = true };
    }

    /// <summary>Gilt die Bedingung für dieses Ereignis?</summary>
    public bool Holds(SceneCondition? c, MatchEvent e)
    {
        if (c is null) return true;
        var setup = _sim.Setup;
        if (c.MinMinute is { } lo && e.Minute < lo) return false;
        if (c.MaxMinute is { } hi && e.Minute > hi) return false;
        if (c.Weather is { Count: > 0 } w && !w.Contains(setup.Weather)) return false;
        if (c.Floodlit is { } f && f != setup.Floodlit) return false;
        if (c.Knockout is { } k && k != (setup.Knockout is not null)) return false;
        if (c.MinXg is { } minXg && e.Xg < minXg) return false;
        if (c.MaxXg is { } maxXg && e.Xg > maxXg) return false;
        if (c.MinRow is { } minRow && e.Row < minRow) return false;
        if (c.MaxRow is { } maxRow && e.Row > maxRow) return false;
        if (c.After is { Count: > 0 } after &&
            !(_last.TryGetValue(e.Side, out var prev) && e.Minute - prev.Minute <= 1 && after.Contains(prev.Root))) return false;
        if (c.Score is { } score && ScoreFor(e) != score) return false;
        if (c.Attrs is { Count: > 0 } attrs && !Fits(attrs, e.PlayerId is { } id ? _players.GetValueOrDefault(id) : null)) return false;
        if (c.KeeperAttrs is { Count: > 0 } ka && !Fits(ka, Keeper(e.Side.Other())?.Player)) return false;
        return true;
    }

    private static bool Fits(List<SceneAttr> attrs, Player? p) =>
        p is not null && attrs.All(a => (a.Min is not { } min || p.Attributes[a.Attr] >= min) && (a.Max is not { } max || p.Attributes[a.Attr] <= max));

    /// <summary>Spielstand aus Sicht der handelnden Mannschaft vor dem Ereignis (ein Tor zählt noch nicht).</summary>
    private static SceneScore ScoreFor(MatchEvent e)
    {
        var (own, opp) = e.Side == Side.Home ? (e.HomeScore, e.AwayScore) : (e.AwayScore, e.HomeScore);
        if (e.Type == MatchEventType.Goal) own--;
        return own == opp ? SceneScore.Level : own > opp ? SceneScore.Leading : SceneScore.Trailing;
    }

    private MatchPlayer? Keeper(Side side) => _sim.Team(side).OnPitch.FirstOrDefault(p => p.Position == Position.GK);

    private sealed record SceneCast(string Mate, string Defender);

    private SceneCast Cast(MatchEvent e, Rng rng)
    {
        var own = _sim.Team(e.Side).OnPitch.Where(p => p.Player.Id != e.PlayerId && p.Position != Position.GK).ToList();
        var defenders = _sim.Team(e.Side.Other()).OnPitch
            .Where(p => p.Player.Id != e.OtherPlayerId && p.Position is Position.DL or Position.DC or Position.DR or Position.DM).ToList();
        var fallback = _data.Texts["scene.someone"];
        return new SceneCast(
            own.Count > 0 ? rng.Pick(own).Player.ShortName : fallback,
            defenders.Count > 0 ? rng.Pick(defenders).Player.ShortName : fallback);
    }

    private string Fill(string template, MatchEvent e, SceneCast cast)
    {
        var team = _sim.Team(e.Side);
        var other = _sim.Team(e.Side.Other());
        var side = e.Col <= 1 ? _data.Texts["comm.side.left"] : e.Col >= 3 ? _data.Texts["comm.side.right"] : _data.Texts["comm.side.centre"];
        return template
            .Replace("{p}", Name(e.PlayerId))
            .Replace("{o}", Name(e.OtherPlayerId))
            .Replace("{g}", Keeper(e.Side.Other())?.Player.ShortName ?? _data.Texts["comm.keeper"])
            .Replace("{mate}", cast.Mate)
            .Replace("{def}", cast.Defender)
            .Replace("{ref}", _sim.Setup.RefereeName ?? _data.Texts["scene.referee"])
            .Replace("{t}", team.Club.Name)
            .Replace("{other}", other.Club.Name)
            .Replace("{home}", _sim.Home.Club.Name)
            .Replace("{away}", _sim.Away.Club.Name)
            .Replace("{score}", $"{e.HomeScore}:{e.AwayScore}")
            .Replace("{side}", side)
            .Replace("{pens}", $"{e.Value / 100}:{e.Value % 100}")
            .Replace("{n}", e.Value.ToString())
            .Replace("{m}", e.Value.ToString())
            .Replace("{min}", e.Minute.ToString());
    }

    /// <summary>Großer Anfangsbuchstabe, falls ein Platzhalter wie „der Schiedsrichter“ den Satz beginnt.</summary>
    private static string Capitalize(string text) =>
        text.Length > 0 && char.IsLower(text[0]) ? char.ToUpper(text[0]) + text[1..] : text;

    private string Name(int? id) => id is { } v && _players.TryGetValue(v, out var p) ? p.ShortName : _data.Texts["scene.someone"];
}
