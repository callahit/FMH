using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.MatchEngine;
using Fmh.Ui.Pages;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

/// <summary>
/// Ablauf eines Spieltags (Kapitel 6): Vorbericht, Kabinenansprache, Live-Spiel im gewählten
/// Darstellungsmodus, Halbzeit, Nachbericht.
/// </summary>
public sealed class MatchView : View
{
    private enum Stage { PreMatch, PreTalk, Live, HalfTime, PostMatch }

    /// <summary>Spieldauer je Tempostufe in Sekunden: "Echtzeit-Feeling" (ca. 8 Min.) bis "Blitz" (ca. 20 Sek.).</summary>
    private static readonly double[] SecondsPerMatch = { 480, 240, 120, 60, 20 };
    private const double PhasesPerMatch = 230;

    private readonly Fixture _fixture;
    private readonly Side _side;
    private readonly List<CommentaryLine> _lines = new();
    private readonly ListState _talk = new();
    private Stage _stage = Stage.PreMatch;
    private IMatchSimulation? _sim;
    private CommentaryGenerator? _commentary;
    private TimeSpan _wait;
    private bool _paused;
    private string? _pauseReason;
    private string? _talkResult;
    private bool _halfTimeTalkDone;
    private int _postPage;

    public MatchView(App app, Fixture fixture) : base(app)
    {
        _fixture = fixture;
        _side = fixture.HomeId == S.HumanClub.Id ? Side.Home : Side.Away;
    }

    private GameSession S => App.Session!;
    private ManagerProfile Profile => S.HumanManager.Profile;
    private Club Own => S.HumanClub;
    private Club Opponent => S.World.Clubs[_side == Side.Home ? _fixture.AwayId : _fixture.HomeId];

    // ================================================================== Rendering

    public override void Render(Canvas c)
    {
        c.Clear(Th.Fg, Th.Bg);
        switch (_stage)
        {
            case Stage.PreMatch: RenderPreMatch(c); break;
            case Stage.PreTalk: RenderLive(c); RenderTalkPanel(c, T["match.preTalk"]); break;
            case Stage.Live: RenderLive(c); break;
            case Stage.HalfTime: RenderLive(c); RenderTalkPanel(c, T["match.halfTime"]); break;
            case Stage.PostMatch: RenderPostMatch(c); break;
        }
    }

    private void Header(Canvas c, string right)
    {
        var home = S.World.Clubs[_fixture.HomeId];
        var away = S.World.Clubs[_fixture.AwayId];
        c.Fill(0, 0, c.Width, 1, ' ', Th.MenuFg, Th.MenuBg);
        var score = _sim is null ? "-:-" : $"{_sim.Home.Goals}:{_sim.Away.Goals}";
        c.Write(1, 0, $"{home.Name.ToUpperInvariant()}  {score}  {away.Name.ToUpperInvariant()}", Th.MenuFg, Th.MenuBg);
        c.Write(c.Width - right.Length - 1, 0, right, Th.MenuFg, Th.MenuBg);
    }

    private void RenderPreMatch(Canvas c)
    {
        var league = S.World.League(_fixture.LeagueId);
        Header(c, T.Format("match.matchdayLabel", league.Name, _fixture.Matchday));
        var opp = Opponent;
        var table = LeagueTable.Compute(league);
        var oppRow = table.First(r => r.ClubId == opp.Id);
        var ownRow = table.First(r => r.ClubId == Own.Id);
        var referee = S.World.Referees.FirstOrDefault(r => r.Id == _fixture.RefereeId);
        var weather = S.WeatherFor(_fixture);
        var home = S.World.Clubs[_fixture.HomeId];

        var y = 2;
        c.Write(2, y++, T["match.preview"], Th.Accent, Th.Bg);
        y++;
        void Line(string label, string value, ConsoleColor? fg = null)
        {
            c.WriteField(2, y, 22, label, Th.Dim, Th.Bg);
            c.Write(25, y, value, fg ?? Th.Fg, Th.Bg, c.Width - 27);
            y++;
        }
        Line(T["match.opponent"], $"{opp.Name} ({_side switch { Side.Home => T["match.atHome"], _ => T["match.away"] }})");
        Line(T["match.tablePosition"], T.Format("match.positions", ownRow.Position, ownRow.Points, oppRow.Position, oppRow.Points));
        Line(T["match.oppForm"], oppRow.Form.Count == 0 ? "–" : string.Join(" ", oppRow.Form.Select(f => T["form." + f])));
        Line(T["match.oppFormation"], App.Data.Formation(opp.Tactic.FormationId).Name);
        var keyPlayers = S.World.SquadOf(opp).Where(p => p.IsAvailable)
            .OrderByDescending(p => Ratings.Strength(p, App.Data.PositionProfiles)).Take(3)
            .Select(p => $"{p.Name} ({Display.Position(p.MainPosition, T)}, {Display.Strength(Ratings.Strength(p, App.Data.PositionProfiles))})");
        Line(T["match.keyPlayers"], string.Join(", ", keyPlayers));
        Line(T["match.strength"], T.Format("match.strengthValue",
            Display.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(Own, S.World, App.Data))),
            Display.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(opp, S.World, App.Data)))));
        Line(T["match.venue"], $"{home.StadiumName} ({Fmt.Int(home.StadiumCapacity)} {T["unit.seats"]})");
        Line(T["match.weather"], T["weather." + weather]);
        if (referee is not null) Line(T["match.referee"], T.Format("match.refereeValue", referee.Name, referee.Strictness));
        Line(T["match.display"], $"{T["matchDisplay." + Profile.MatchDisplay]}, {T["speed." + Profile.MatchSpeed]}");

        y++;
        c.Write(2, y++, T["match.lineupCheck"], Th.Accent, Th.Bg);
        var problems = LineupSelector.Validate(Own, S.World, App.Data);
        if (problems.Count == 0)
        {
            var formation = App.Data.Formation(Own.Tactic.FormationId);
            var xi = Own.Tactic.Lineup.Select((id, i) => $"{Display.Position(formation.Slots[i].Position, T)} {S.World.Players[id!.Value].LastName}");
            foreach (var line in InboxPage.Wrap(string.Join(", ", xi), c.Width - 6))
                c.Write(2, y++, line, Th.Fg, Th.Bg);
        }
        else
        {
            foreach (var p in problems) c.Write(2, y++, "! " + T[p], Th.Bad, Th.Bg, c.Width - 4);
            c.Write(2, y++, T["match.autoFixHint"], Th.Warn, Th.Bg, c.Width - 4);
        }
        y++;
        foreach (var advice in TacticAdvisor.Analyse(Own, S.World, App.Data).Take(Math.Max(0, c.Height - y - 3)))
            c.Write(2, y++, "• " + T.Format(advice.Key, advice.Args.Select(a => a is Position pos ? (object)Display.Position(pos, T) : a).ToArray()),
                advice.Warning ? Th.Warn : Th.Good, Th.Bg, c.Width - 4);

        Draw.KeyHelp(c, c.Height - 1, Th, T["help.preMatch"]);
    }

    private void RenderLive(Canvas c)
    {
        var sim = _sim!;
        var minute = sim.StoppageMinute > 0 ? $"{sim.Minute}+{sim.StoppageMinute}'" : $"{sim.Minute}'";
        if (sim.IsFinished) minute = T["match.final"];
        Header(c, $"{minute}   {T["matchDisplay." + Profile.MatchDisplay]} · {T["speed." + Profile.MatchSpeed]}");

        const int sideWidth = 38;
        var mainW = c.Width - sideWidth - 3;
        var bottom = c.Height - 3;

        // Kommentar: neueste Zeile unten.
        var visible = bottom - 2;
        var start = Math.Max(0, _lines.Count - visible);
        var y = 2;
        for (var i = start; i < _lines.Count; i++)
        {
            var l = _lines[i];
            var fg = l.Highlight ? Th.Accent : l.Event.Key ? Th.Fg : Th.Dim;
            c.WriteField(1, y, 7, l.Minute, Th.Dim, Th.Bg, Align.Right);
            c.Write(9, y, l.Text, fg, Th.Bg, mainW - 9);
            y++;
        }
        for (var yy = 1; yy < bottom; yy++) c.Set(mainW + 1, yy, '│', Th.Border, Th.Bg);

        // Seitenleiste
        var sx = mainW + 3;
        var sy = 2;
        var home = sim.Home;
        var away = sim.Away;
        var total = home.Stats.PossessionSeconds + away.Stats.PossessionSeconds;
        var poss = total <= 0 ? 50 : (int)Math.Round(100 * home.Stats.PossessionSeconds / total);
        void Stat(string label, string h, string a)
        {
            c.WriteField(sx, sy, 6, h, Th.Fg, Th.Bg, Align.Right);
            c.WriteField(sx + 7, sy, sideWidth - 16, label, Th.Dim, Th.Bg, Align.Center);
            c.WriteField(sx + sideWidth - 8, sy, 6, a, Th.Fg, Th.Bg);
            sy++;
        }
        Stat(T["stat.possession"], $"{poss}%", $"{100 - poss}%");
        Stat(T["stat.shots"], $"{home.Stats.Shots} ({home.Stats.ShotsOnTarget})", $"{away.Stats.Shots} ({away.Stats.ShotsOnTarget})");
        Stat(T["stat.corners"], home.Stats.Corners.ToString(), away.Stats.Corners.ToString());
        Stat(T["stat.cards"], $"{home.Stats.YellowCards}/{home.Stats.RedCards}", $"{away.Stats.YellowCards}/{away.Stats.RedCards}");
        sy++;

        var own = sim.Team(_side);
        c.Write(sx, sy++, $"{own.Club.ShortName} · {T["match.subsLeft"]} {sim.Setup.Substitutions - own.SubstitutionsUsed}", Th.Accent, Th.Bg, sideWidth);
        foreach (var mp in own.OnPitch.OrderBy(p => p.SlotIndex))
        {
            if (sy >= bottom) break;
            c.WriteField(sx, sy, 4, Display.Position(mp.Position, T), Th.Dim, Th.Bg);
            c.WriteField(sx + 4, sy, 14, mp.Player.LastName, mp.Injured ? Th.Bad : Th.Fg, Th.Bg);
            c.Write(sx + 19, sy, Display.Bar(mp.Condition, 100, 8), Display.ConditionColor(mp.Condition, Th), Th.Bg);
            c.WriteField(sx + 28, sy, 3, $"{mp.Condition:0}", Th.Dim, Th.Bg, Align.Right);
            var marks = (mp.Goals > 0 ? $"T{mp.Goals}" : "") + (mp.Yellow ? "▮" : "");
            c.WriteField(sx + 32, sy, 4, Display.Grade(mp.Grade), Display.GradeColor(mp.Grade, Th), Th.Bg, Align.Right);
            if (marks.Length > 0 && sx + 36 < c.Width) c.Write(sx + 36, sy, marks, mp.Yellow ? Th.Warn : Th.Good, Th.Bg, c.Width - sx - 36);
            sy++;
        }

        // Statuszeile
        c.HLine(0, bottom, c.Width, Th.Border, Th.Bg);
        var status = _paused ? $"{T["match.paused"]}{(_pauseReason is null ? "" : " – " + _pauseReason)}" : own.Shout != Shout.None ? T.Format("match.shoutActive", T["shout." + own.Shout]) : "";
        if (_talkResult is not null && _stage == Stage.Live && !_paused) status = _talkResult;
        if (App.StatusMessage is { } msg) status = msg;
        c.Write(1, bottom + 1, status, _paused ? Th.Warn : Th.Fg, Th.Bg, c.Width - 2);
        Draw.KeyHelp(c, c.Height - 1, Th, sim.IsFinished ? T["help.matchEnd"] : T["help.live"]);
    }

    private static readonly Tone?[] Tones = { Tone.Calm, Tone.Demanding, Tone.Praising, Tone.Angry, Tone.Disappointed, Tone.Motivating, null };

    private void RenderTalkPanel(Canvas c, string title)
    {
        var w = 52;
        var h = Tones.Length + 8;
        var x = (c.Width - w) / 2;
        var y = (c.Height - h) / 2;
        c.Box(x, y, w, h, Th.Border, Th.PanelBg, title);
        var done = _stage == Stage.HalfTime && _halfTimeTalkDone;
        c.Write(x + 2, y + 1, done ? T["match.talkDone"] : T["match.chooseTone"], Th.Dim, Th.PanelBg, w - 4);
        for (var i = 0; i < Tones.Length; i++)
        {
            var sel = !done && i == _talk.Index;
            var label = Tones[i] is { } tone ? $"{i + 1}  {T["tone." + tone]}" : $"{i + 1}  {T["tone.none"]}";
            c.WriteField(x + 2, y + 3 + i, w - 4, label, sel ? Th.SelFg : done ? Th.Dim : Th.Fg, sel ? Th.SelBg : Th.PanelBg);
        }
        if (_talkResult is not null) c.Write(x + 2, y + h - 3, _talkResult, Th.Accent, Th.PanelBg, w - 4);
        c.Write(x + 2, y + h - 2, _stage == Stage.HalfTime ? T["help.halfTime"] : T["help.preTalk"], Th.Dim, Th.PanelBg, w - 4);
    }

    private void RenderPostMatch(Canvas c)
    {
        var sim = _sim!;
        var result = _fixture.Result!;
        Header(c, T.Format("match.halfTimeScore", result.HalfTimeHome, result.HalfTimeAway));
        var y = 2;
        var referee = S.World.Referees.FirstOrDefault(r => r.Id == _fixture.RefereeId);
        c.Write(2, y++, T.Format("match.postInfo", Fmt.Int(result.Attendance), T["weather." + result.Weather], referee?.Name ?? "–"), Th.Dim, Th.Bg, c.Width - 4);
        if (sim.PlayerOfTheMatchId is { } potm && S.World.Players.TryGetValue(potm, out var star))
            c.Write(2, y++, T.Format("match.potm", star.Name, S.World.Clubs[star.ClubId ?? 0].Name), Th.Accent, Th.Bg, c.Width - 4);
        y++;

        if (_postPage == 0)
        {
            // Statistik rechts neben den Notentabellen, wenn Platz ist, sonst darunter.
            var colW = Math.Clamp((c.Width - 50) / 2, 30, 44);
            var statsRight = 2 * colW + 50 <= c.Width;
            if (!statsRight) colW = Math.Min(44, (c.Width - 6) / 2);
            GradeTable(c, 2, y, colW, sim.Home);
            GradeTable(c, 4 + colW, y, colW, sim.Away);
            if (statsRight) RenderStats(c, 6 + 2 * colW, y, sim);
            else RenderStats(c, 2, y + 3 + Math.Max(sim.Home.Participants.Count(), sim.Away.Participants.Count()), sim);
        }
        else
        {
            RenderHeatmap(c, 2, y, sim.Team(_side));
            RenderOtherResults(c, 40, y);
        }
        Draw.KeyHelp(c, c.Height - 1, Th, T["help.postMatch"]);
    }

    private void GradeTable(Canvas c, int x, int y, int w, MatchTeamState team)
    {
        c.Write(x, y++, team.Club.Name, Th.Accent, Th.Bg, w);
        Draw.TableHeader(c, x, y++, Th, (T["col.pos"], 4, Align.Left), (T["col.name"], w - 20, Align.Left), (T["col.grade"], 4, Align.Right), ("", 8, Align.Left));
        foreach (var mp in team.Participants.OrderBy(p => p.Started ? 0 : 1).ThenBy(p => p.MinuteOn).ThenBy(p => p.SlotIndex))
        {
            if (y >= c.Height - 2) break;
            var notes = "";
            if (mp.Goals > 0) notes += $"T{mp.Goals} ";
            if (mp.Assists > 0) notes += $"V{mp.Assists} ";
            if (mp.Red) notes += "Rot ";
            else if (mp.Yellow) notes += "Gelb ";
            if (mp.Injured) notes += "✚";
            var name = mp.Started ? mp.Player.Name : $"↑{mp.MinuteOn}' {mp.Player.LastName}";
            c.WriteField(x, y, 4, Display.Position(mp.Position, T), Th.Dim, Th.Bg);
            c.WriteField(x + 5, y, w - 20, name, Th.Fg, Th.Bg);
            c.WriteField(x + w - 14, y, 4, Display.Grade(mp.Grade), Display.GradeColor(mp.Grade, Th), Th.Bg, Align.Right);
            c.WriteField(x + w - 9, y, 9, notes.Trim(), Th.Warn, Th.Bg);
            y++;
        }
    }

    private void RenderStats(Canvas c, int x, int y, IMatchSimulation sim)
    {
        var h = sim.Home.Stats;
        var a = sim.Away.Stats;
        var total = h.PossessionSeconds + a.PossessionSeconds;
        var poss = total <= 0 ? 50 : (int)Math.Round(100 * h.PossessionSeconds / total);
        c.Write(x, y++, T["match.stats"], Th.Accent, Th.Bg);
        void Row(string label, string hv, string av)
        {
            if (y >= c.Height - 2) return;
            c.WriteField(x, y, 10, hv, Th.Fg, Th.Bg, Align.Right);
            c.WriteField(x + 11, y, 19, label, Th.Dim, Th.Bg, Align.Center);
            c.WriteField(x + 31, y, 10, av, Th.Fg, Th.Bg);
            y++;
        }
        Row(T["stat.goals"], sim.Home.Goals.ToString(), sim.Away.Goals.ToString());
        Row(T["stat.shotsOnTarget"], $"{h.Shots} ({h.ShotsOnTarget})", $"{a.Shots} ({a.ShotsOnTarget})");
        Row(T["stat.xg"], Fmt.Num(h.Xg, "0.00"), Fmt.Num(a.Xg, "0.00"));
        Row(T["stat.possession"], $"{poss}%", $"{100 - poss}%");
        Row(T["stat.passes"], $"{h.Passes} ({h.PassAccuracy}%)", $"{a.Passes} ({a.PassAccuracy}%)");
        Row(T["stat.duels"], $"{h.DuelRate}%", $"{a.DuelRate}%");
        Row(T["stat.corners"], h.Corners.ToString(), a.Corners.ToString());
        Row(T["stat.fouls"], h.Fouls.ToString(), a.Fouls.ToString());
        Row(T["stat.offsides"], h.Offsides.ToString(), a.Offsides.ToString());
        Row(T["stat.cards"], $"{h.YellowCards}/{h.RedCards}", $"{a.YellowCards}/{a.RedCards}");
        Row(T["stat.distance"], Fmt.Num(h.DistanceKm, "0.0"), Fmt.Num(a.DistanceKm, "0.0"));
    }

    /// <summary>Heatmap als Textraster mit Dichtezeichen (Kapitel 6). Angriffsrichtung nach oben.</summary>
    private void RenderHeatmap(Canvas c, int x, int y, MatchTeamState team)
    {
        const string density = " .:-=+*#%@";
        c.Write(x, y++, T.Format("match.heatmap", team.Club.Name), Th.Accent, Th.Bg);
        var max = Math.Max(1, team.Stats.Heat.Max());
        c.Box(x, y, Grid.Cols * 6 + 2, Grid.Rows + 2, Th.Border, Th.Bg);
        for (var row = Grid.Rows - 1; row >= 0; row--)
        {
            for (var col = 0; col < Grid.Cols; col++)
            {
                var v = team.Stats.Heat[row * Grid.Cols + col];
                var ch = density[(int)Math.Round((double)v / max * (density.Length - 1))];
                c.Write(x + 1 + col * 6, y + 1 + (Grid.Rows - 1 - row), new string(ch, 6), Th.Accent, Th.Bg);
            }
        }
        c.Write(x, y + Grid.Rows + 2, T["match.heatmapLegend"], Th.Dim, Th.Bg);
    }

    private void RenderOtherResults(Canvas c, int x, int y)
    {
        var league = S.World.League(_fixture.LeagueId);
        c.Write(x, y++, T.Format("match.otherResults", _fixture.Matchday), Th.Accent, Th.Bg);
        foreach (var f in league.Fixtures.Where(f => f.Matchday == _fixture.Matchday))
        {
            if (y >= c.Height - 2) break;
            var own = f.Involves(Own.Id);
            var res = f.Result is { } r ? $"{r.HomeGoals}:{r.AwayGoals}" : "-:-";
            c.Write(x, y++, $"{Display.Truncate(S.World.Clubs[f.HomeId].Name, 22),22} {res,5}  {Display.Truncate(S.World.Clubs[f.AwayId].Name, 22)}",
                own ? Th.Accent : Th.Fg, Th.Bg, c.Width - x - 1);
        }
        y++;
        var table = LeagueTable.Compute(league);
        var ownRow = table.First(r => r.ClubId == Own.Id);
        if (y < c.Height - 2)
            c.Write(x, y, T.Format("match.ownTable", ownRow.Position, ownRow.Points, ownRow.GoalsFor, ownRow.GoalsAgainst), Th.Fg, Th.Bg, c.Width - x - 1);
    }

    // ================================================================== Ablauf

    public override void Tick(TimeSpan elapsed)
    {
        if (_stage != Stage.Live || _paused || _sim is null) return;
        _wait -= elapsed;
        var perStep = TimeSpan.FromSeconds(SecondsPerMatch[Math.Clamp(Profile.MatchSpeed, 1, 5) - 1] / PhasesPerMatch);
        var guard = 0;
        while (_wait <= TimeSpan.Zero && _stage == Stage.Live && !_paused && guard++ < 50)
        {
            StepOnce();
            _wait += perStep;
        }
        if (_wait < -perStep) _wait = TimeSpan.Zero;
    }

    private void StepOnce()
    {
        var sim = _sim!;
        var events = sim.Step();
        var ticker = Profile.MatchDisplay == MatchDisplayMode.Ticker;
        foreach (var e in events)
        {
            if (_commentary!.Describe(e, ticker) is { } line) _lines.Add(line);
            var ours = e.Side == _side;
            if (e.Type == MatchEventType.Goal && Profile.PauseOnGoals)
                Pause(T.Format("pause.goal", e.HomeScore, e.AwayScore));
            if (e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow && Profile.PauseOnRedCards)
                Pause(T["pause.red"]);
            if (e.Type == MatchEventType.Injury && ours && e.PlayerId is { } pid)
                Pause(T.Format("pause.injury", S.World.Players[pid].Name));
        }
        if (sim.IsHalfTime)
        {
            _stage = Stage.HalfTime;
            _talk.Index = 0;
            _talkResult = null;
            _paused = false;
        }
        else if (sim.IsFinished)
        {
            _paused = false;
            FinishMatch();
        }
    }

    private void Pause(string reason)
    {
        _paused = true;
        _pauseReason = reason;
    }

    private void Kickoff()
    {
        var instant = Profile.MatchDisplay == MatchDisplayMode.Instant;
        _sim = S.StartMatch(_fixture, humanControlled: !instant);
        _commentary = new CommentaryGenerator(App.Data, _sim);
        if (instant)
        {
            _sim.RunToEnd();
            FinishMatch();
            return;
        }
        _stage = Stage.PreTalk;
        _talk.Index = 0;
    }

    private void FinishMatch()
    {
        var sim = _sim!;
        // Restliche Ereignisse in den Kommentar übernehmen (z. B. nach "Sofort beenden").
        var ticker = Profile.MatchDisplay == MatchDisplayMode.Ticker;
        var known = _lines.Count == 0 ? -1 : _lines[^1].Event.Index;
        foreach (var e in sim.Events.Where(e => e.Index > known))
            if (_commentary!.Describe(e, ticker) is { } line) _lines.Add(line);

        S.FinishMatch(_fixture, sim);
        S.PlayOtherFixturesToday();
        _stage = Stage.PostMatch;
        _postPage = 0;
    }

    private void ApplyTalk(int index)
    {
        if (Tones[index] is not { } tone)
        {
            _talkResult = T["match.noTalk"];
            return;
        }
        var reactions = _sim!.TeamTalk(_side, tone);
        var up = reactions.Count(r => r.MoraleDelta > 0);
        var down = reactions.Count(r => r.MoraleDelta < 0);
        _talkResult = T.Format("match.talkResult", up, down);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        switch (_stage)
        {
            case Stage.PreMatch: HandlePreMatch(key); break;
            case Stage.PreTalk: HandleTalk(key); break;
            case Stage.HalfTime: HandleHalfTime(key); break;
            case Stage.Live: HandleLive(key); break;
            case Stage.PostMatch:
                if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow) _postPage = 1 - _postPage;
                else if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.Escape) App.Pop();
                break;
        }
    }

    private void HandlePreMatch(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.T:
                var shell = App.Views.OfType<GameView>().First();
                App.Push(new PageHostView(App, new TacticsPage(shell), T["menu.tactics"]));
                break;
            case ConsoleKey.A:
                LineupSelector.Repair(Own, S.World, App.Data, S.BenchSize(Own));
                App.StatusMessage = T["match.repaired"];
                break;
            case ConsoleKey.M:
                Profile.MatchDisplay = (MatchDisplayMode)(((int)Profile.MatchDisplay + 1) % 3);
                break;
            case ConsoleKey.OemPlus or ConsoleKey.Add:
                Profile.MatchSpeed = Math.Min(5, Profile.MatchSpeed + 1);
                break;
            case ConsoleKey.OemMinus or ConsoleKey.Subtract:
                Profile.MatchSpeed = Math.Max(1, Profile.MatchSpeed - 1);
                break;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                Kickoff();
                break;
        }
    }

    private void HandleTalk(ConsoleKeyInfo key)
    {
        if (_talk.HandleKey(key, Tones.Length)) return;
        var digit = key.KeyChar - '1';
        if (digit >= 0 && digit < Tones.Length) _talk.Index = digit;
        if (key.Key == ConsoleKey.Enter || (digit >= 0 && digit < Tones.Length))
        {
            ApplyTalk(_talk.Index);
            _stage = Stage.Live;
            _wait = TimeSpan.Zero;
        }
    }

    private void HandleHalfTime(ConsoleKeyInfo key)
    {
        if (!_halfTimeTalkDone)
        {
            if (_talk.HandleKey(key, Tones.Length)) return;
            var digit = key.KeyChar - '1';
            if (digit >= 0 && digit < Tones.Length) _talk.Index = digit;
            if (key.Key == ConsoleKey.Enter || (digit >= 0 && digit < Tones.Length))
            {
                ApplyTalk(_talk.Index);
                _halfTimeTalkDone = true;
                return;
            }
        }
        switch (key.Key)
        {
            case ConsoleKey.W: OpenSubstitution(); break;
            case ConsoleKey.T: OpenTactics(); break;
            case ConsoleKey.Spacebar:
            case ConsoleKey.Escape:
            case ConsoleKey.Enter when _halfTimeTalkDone:
                _stage = Stage.Live;
                _wait = TimeSpan.Zero;
                StepOnce(); // Anpfiff zweite Halbzeit
                break;
        }
    }

    private void HandleLive(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.P:
            case ConsoleKey.Spacebar:
                _paused = !_paused;
                _pauseReason = null;
                break;
            case ConsoleKey.W: _paused = true; OpenSubstitution(); break;
            case ConsoleKey.T: _paused = true; OpenTactics(); break;
            case ConsoleKey.Z: OpenShout(); break;
            case ConsoleKey.M:
                Profile.MatchDisplay = Profile.MatchDisplay == MatchDisplayMode.Commentary ? MatchDisplayMode.Ticker : MatchDisplayMode.Commentary;
                break;
            case ConsoleKey.OemPlus or ConsoleKey.Add:
                Profile.MatchSpeed = Math.Min(5, Profile.MatchSpeed + 1);
                break;
            case ConsoleKey.OemMinus or ConsoleKey.Subtract:
                Profile.MatchSpeed = Math.Max(1, Profile.MatchSpeed - 1);
                break;
            case ConsoleKey.S:
                // Rest des Spiels sofort berechnen.
                while (!_sim!.IsFinished)
                {
                    _sim.Step();
                    if (_sim.IsHalfTime) _sim.Step();
                }
                FinishMatch();
                break;
        }
    }

    private void OpenSubstitution()
    {
        var sim = _sim!;
        var team = sim.Team(_side);
        if (team.SubstitutionsUsed >= sim.Setup.Substitutions)
        {
            App.Push(new MessageDialog(App, T["match.substitution"], new[] { T["sub.noneLeft"] }));
            return;
        }
        var onPitch = team.OnPitch.OrderBy(p => p.SlotIndex).ToList();
        var items = onPitch.Select(p =>
            $"{Display.Position(p.Position, T),-4} {Display.Truncate(p.Player.Name, 22),-22} {p.Condition,3:0}%  {Display.Grade(p.Grade),4}{(p.Injured ? "  ✚" : "")}{(p.Yellow ? "  ▮" : "")}").ToList();
        App.Push(new ListDialog(App, T["match.subOut"], items, i =>
        {
            var outMp = onPitch[i];
            var bench = team.Bench.ToList();
            if (bench.Count == 0)
            {
                App.Push(new MessageDialog(App, T["match.substitution"], new[] { T["sub.emptyBench"] }));
                return;
            }
            var benchItems = bench.Select(p =>
            {
                var rating = Ratings.EffectiveRating(p.Player, outMp.Position, App.Data.PositionProfiles);
                return $"{Display.Position(p.Player.MainPosition, T),-4} {Display.Truncate(p.Player.Name, 22),-22} {Display.Strength(Ratings.StrengthFromRating(rating)),5} {T["match.asPosition"]} {Display.Position(outMp.Position, T)}";
            }).ToList();
            App.Push(new ListDialog(App, T.Format("match.subIn", outMp.Player.Name), benchItems, j =>
            {
                if (!sim.Substitute(_side, outMp.Player.Id, bench[j].Player.Id, out var error))
                    App.Push(new MessageDialog(App, T["match.substitution"], new[] { T[error ?? "sub.failed"] }));
                else if (_commentary!.Describe(sim.Events[^1], false) is { } line) _lines.Add(line);
            }));
        }));
    }

    private void OpenTactics() => App.Push(new MatchTacticsDialog(App, _sim!, _side));

    private void OpenShout()
    {
        var shouts = Enum.GetValues<Shout>();
        var items = shouts.Select(s => T["shout." + s]).ToList();
        App.Push(new ListDialog(App, T["match.shout"], items, i => _sim!.SetShout(_side, shouts[i]), (int)_sim!.Team(_side).Shout));
    }
}

/// <summary>Taktikänderung während des Spiels: Formation, Mentalität und Mannschaftsanweisungen.</summary>
public sealed class MatchTacticsDialog : View
{
    private readonly IMatchSimulation _sim;
    private readonly Side _side;
    private readonly Tactic _draft;
    private readonly List<TacticSetting> _rows;
    private readonly ListState _list = new();

    public MatchTacticsDialog(App app, IMatchSimulation sim, Side side) : base(app)
    {
        _sim = sim;
        _side = side;
        _draft = sim.Team(side).Tactic.Clone();
        _rows = TacticSettings.Rows(app.Data);
    }

    public override bool IsOverlay => true;

    public override void Render(Canvas c)
    {
        var w = 56;
        var h = _rows.Count + 5;
        var x = (c.Width - w) / 2;
        var y = Math.Max(1, (c.Height - h) / 2);
        c.Box(x, y, w, h, Th.Border, Th.PanelBg, T["match.tactics"]);
        for (var i = 0; i < _rows.Count; i++)
        {
            var sel = i == _list.Index;
            c.WriteField(x + 2, y + 1 + i, 20, T[_rows[i].LabelKey], Th.Dim, Th.PanelBg);
            c.WriteField(x + 23, y + 1 + i, w - 25, $"◄ {_rows[i].Value(_draft)} ►", sel ? Th.SelFg : Th.Fg, sel ? Th.SelBg : Th.PanelBg);
        }
        c.Write(x + 2, y + h - 2, T["help.matchTactics"], Th.Dim, Th.PanelBg, w - 4);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key, _rows.Count)) return;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow: _rows[_list.Index].Change(_draft, -1); break;
            case ConsoleKey.RightArrow: _rows[_list.Index].Change(_draft, +1); break;
            case ConsoleKey.Escape: App.Pop(); break;
            case ConsoleKey.Enter:
                var team = _sim.Team(_side);
                if (_draft.FormationId != team.Tactic.FormationId) _sim.ChangeFormation(_side, App.Data.Formation(_draft.FormationId));
                if (_draft.Mentality != team.Tactic.Mentality) _sim.ChangeMentality(_side, _draft.Mentality);
                _sim.ChangeInstructions(_side, _draft.Instructions);
                App.Pop();
                break;
        }
    }
}
