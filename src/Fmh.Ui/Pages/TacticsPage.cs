using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>
/// Taktik (Kapitel 5): Formation auf dem 5x7-Textraster, Aufstellung und Bank, Mannschaftsanweisungen,
/// Standardschützen und Co-Trainer-Feedback.
/// </summary>
public sealed class TacticsPage : Page
{
    private readonly ListState _lineup = new();
    private readonly ListState _settings = new();
    private readonly List<TacticSetting> _rows;
    private bool _settingsFocus;

    public TacticsPage(GameView shell) : base(shell)
    {
        _rows = TacticSettings.Rows(App.Data);
    }

    public override string TitleKey => "menu.tactics";
    public override string HelpKey => _settingsFocus ? "help.page.tacticsSettings" : "help.page.tacticsLineup";

    private Club Club => S.HumanClub;
    private Formation Formation => App.Data.Formation(Club.Tactic.FormationId);
    private int BenchSize => S.BenchSize(Club);

    /// <summary>Zeilen der Aufstellungsliste: 11 Slots, dann Bankplätze, dann die drei Standardschützen und der Kapitän.</summary>
    private int LineupRows => Formation.Slots.Count + BenchSize + 4;

    public override void Render(Canvas c, Rect area)
    {
        var tactic = Club.Tactic;
        var profiles = App.Data.PositionProfiles;
        Title(c, area, T["menu.tactics"], Formation.Name);

        // Linke Spalte: Aufstellung
        var lx = area.X;
        var lw = Math.Min(46, area.W / 2 + 4);
        var y = area.Y + 2;
        c.Write(lx, y++, T["tactics.lineup"], _settingsFocus ? Th.Dim : Th.Accent, Th.Bg);
        var rowsAvail = area.H - 6;
        var start = _lineup.VisibleStart(rowsAvail, LineupRows);
        for (var r = start; r < LineupRows && y < area.Y + 3 + rowsAvail; r++, y++)
        {
            var sel = !_settingsFocus && r == _lineup.Index;
            var (label, player, pos) = LineupRow(r);
            var fg = sel ? Th.SelFg : Th.Fg;
            var bg = sel ? Th.SelBg : Th.Bg;
            c.Fill(lx, y, lw, 1, ' ', fg, bg);
            c.WriteField(lx, y, 6, label, sel ? fg : r < Formation.Slots.Count ? Th.Accent : Th.Dim, bg);
            if (player is null)
            {
                c.Write(lx + 7, y, "—", sel ? fg : Th.Dim, bg);
                continue;
            }
            c.WriteField(lx + 7, y, lw - 24, player.Name, fg, bg);
            if (pos is { } slotPos)
            {
                var rating = Ratings.EffectiveRating(player, slotPos, profiles);
                c.WriteField(lx + lw - 16, y, 5, Display.Strength(Ratings.StrengthFromRating(rating)), sel ? fg : Th.AttributeColor((int)Math.Round(rating)), bg, Align.Right);
                var suit = player.SuitabilityFor(slotPos);
                c.WriteField(lx + lw - 10, y, 10, suit == Suitability.Natural ? "" : T["suitShort." + suit],
                    sel ? fg : suit <= Suitability.Adequate ? Th.Bad : Th.Warn, bg);
            }
            if (!player.IsAvailable) c.Write(lx + lw - 2, y, "✚", sel ? fg : Th.Bad, bg);
        }

        // Rechte Spalte: Formationsraster und Einstellungen
        var rx = lx + lw + 2;
        var rw = area.Right - rx;
        DrawGrid(c, rx, area.Y + 2, rw);
        var sy = area.Y + 2 + Grid.Rows + 2;
        c.Write(rx, sy++, T["tactics.instructions"], _settingsFocus ? Th.Accent : Th.Dim, Th.Bg);
        var adviceRows = 3;
        var setRows = Math.Max(3, area.Bottom - sy - adviceRows - 1);
        var sstart = _settings.VisibleStart(setRows, _rows.Count);
        for (var i = 0; i < setRows && sstart + i < _rows.Count; i++)
        {
            var row = _rows[sstart + i];
            var sel = _settingsFocus && sstart + i == _settings.Index;
            var fg = sel ? Th.SelFg : Th.Fg;
            var bg = sel ? Th.SelBg : Th.Bg;
            c.Fill(rx, sy + i, rw, 1, ' ', fg, bg);
            c.WriteField(rx, sy + i, 18, T[row.LabelKey], sel ? fg : Th.Dim, bg);
            c.Write(rx + 19, sy + i, $"◄ {row.Value(tactic)} ►", fg, bg, rw - 19);
        }

        // Co-Trainer
        var advice = TacticAdvisor.Analyse(Club, S.World, App.Data);
        var ay = area.Bottom - adviceRows;
        c.Write(rx, ay - 1, T["tactics.advice"], Th.Accent, Th.Bg);
        for (var i = 0; i < adviceRows && i < advice.Count; i++)
            c.Write(rx, ay + i, "• " + AdviceText(advice[i]), advice[i].Warning ? Th.Warn : Th.Good, Th.Bg, rw);
    }

    public string AdviceText(Advice a) =>
        T.Format(a.Key, a.Args.Select(x => x is Position p ? (object)Display.Position(p, T) : x).ToArray());

    private (string label, Player? player, Position? pos) LineupRow(int r)
    {
        var tactic = Club.Tactic;
        var slots = Formation.Slots;
        Player? P(int? id) => id is { } v && S.World.Players.TryGetValue(v, out var p) ? p : null;
        if (r < slots.Count)
            return (Display.Position(slots[r].Position, T), P(r < tactic.Lineup.Count ? tactic.Lineup[r] : null), slots[r].Position);
        r -= slots.Count;
        if (r < BenchSize)
            return ($"{T["tactics.benchShort"]}{r + 1}", P(r < tactic.Bench.Count ? tactic.Bench[r] : null), null);
        r -= BenchSize;
        return r switch
        {
            0 => (T["tactics.captainShort"], P(tactic.CaptainId), null),
            1 => (T["tactics.penaltyShort"], P(tactic.PenaltyTakerId), null),
            2 => (T["tactics.freeKickShort"], P(tactic.FreeKickTakerId), null),
            _ => (T["tactics.cornerShort"], P(tactic.CornerTakerId), null),
        };
    }

    /// <summary>Formationsraster: Angriff oben, eigenes Tor unten.</summary>
    private void DrawGrid(Canvas c, int x, int y, int width)
    {
        var cell = Math.Clamp((width - 2) / Grid.Cols, 5, 11);
        var gw = cell * Grid.Cols + 2;
        c.Box(x, y - 1, gw, Grid.Rows + 2, Th.Border, Th.Bg);
        var slots = Formation.Slots;
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var gy = y + (Grid.Rows - 1 - s.Row);
            var gx = x + 1 + s.Col * cell;
            var id = i < Club.Tactic.Lineup.Count ? Club.Tactic.Lineup[i] : null;
            var name = id is { } v && S.World.Players.TryGetValue(v, out var p) ? p.LastName : Display.Position(s.Position, T);
            var sel = !_settingsFocus && _lineup.Index == i;
            c.WriteField(gx, gy, cell, Display.Truncate(name, cell - 1), sel ? Th.SelFg : Th.Fg, sel ? Th.SelBg : Th.Bg, Align.Center);
        }
    }

    public override bool Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.E)
        {
            _settingsFocus = !_settingsFocus;
            return true;
        }
        if (key.Key == ConsoleKey.A)
        {
            LineupSelector.PickBest(Club, S.World, App.Data, BenchSize);
            App.StatusMessage = T["tactics.autoDone"];
            return true;
        }
        if (key.Key == ConsoleKey.H)
        {
            var advice = TacticAdvisor.Analyse(Club, S.World, App.Data).Select(AdviceText).ToList();
            App.Push(new MessageDialog(App, T["tactics.advice"], advice));
            return true;
        }
        return _settingsFocus ? HandleSettings(key) : HandleLineup(key);
    }

    private bool HandleSettings(ConsoleKeyInfo key)
    {
        if (_settings.HandleKey(key, _rows.Count)) return true;
        var d = key.Key switch { ConsoleKey.LeftArrow => -1, ConsoleKey.RightArrow or ConsoleKey.Enter => 1, _ => 0 };
        if (d == 0) return false;
        var row = _rows[_settings.Index];
        if (row.FormationChange)
        {
            var current = Club.Tactic.Lineup.Where(id => id.HasValue).Select(id => S.World.Players[id!.Value]).ToList();
            row.Change(Club.Tactic, d);
            Club.Tactic.Lineup = LineupSelector.Assign(Formation, current, App.Data.PositionProfiles);
        }
        else row.Change(Club.Tactic, d);
        return true;
    }

    private bool HandleLineup(ConsoleKeyInfo key)
    {
        if (_lineup.HandleKey(key, LineupRows)) return true;
        var slots = Formation.Slots.Count;
        if (key.Key is ConsoleKey.Delete or ConsoleKey.X && _lineup.Index >= slots && _lineup.Index < slots + BenchSize)
        {
            var b = _lineup.Index - slots;
            if (b < Club.Tactic.Bench.Count) Club.Tactic.Bench.RemoveAt(b);
            return true;
        }
        if (key.Key != ConsoleKey.Enter) return false;
        PickPlayerFor(_lineup.Index);
        return true;
    }

    private void PickPlayerFor(int row)
    {
        var tactic = Club.Tactic;
        var slots = Formation.Slots;
        var profiles = App.Data.PositionProfiles;
        Position? pos = row < slots.Count ? slots[row].Position : null;
        var squad = S.World.SquadOf(Club).ToList();
        if (row >= slots.Count + BenchSize)
            squad = squad.Where(p => tactic.Lineup.Contains(p.Id)).ToList();
        var candidates = squad
            .OrderByDescending(p => p.IsAvailable)
            .ThenByDescending(p => pos is { } sp ? Ratings.EffectiveRating(p, sp, profiles) : Ratings.PositionRating(p, p.MainPosition, profiles))
            .ToList();
        var items = candidates.Select(p =>
        {
            var rating = pos is { } sp ? Ratings.EffectiveRating(p, sp, profiles) : Ratings.PositionRating(p, p.MainPosition, profiles);
            var where = tactic.Lineup.Contains(p.Id) ? T["status.lineup"] : tactic.Bench.Contains(p.Id) ? T["status.bench"] : "";
            var status = p.IsAvailable ? where : Display.Status(p, Club, T);
            return $"{Display.Position(p.MainPosition, T),-4} {Display.Truncate(p.Name, 24),-24} {Display.Strength(Ratings.StrengthFromRating(rating)),5}  {p.State.Condition,3:0}%  {status}";
        }).ToList();
        App.Push(new ListDialog(App, T["tactics.choosePlayer"], items, i => Assign(row, candidates[i])));
    }

    private void Assign(int row, Player p)
    {
        var tactic = Club.Tactic;
        var slots = Formation.Slots.Count;
        while (tactic.Lineup.Count < slots) tactic.Lineup.Add(null);

        if (row >= slots + BenchSize)
        {
            switch (row - slots - BenchSize)
            {
                case 0: tactic.CaptainId = p.Id; break;
                case 1: tactic.PenaltyTakerId = p.Id; break;
                case 2: tactic.FreeKickTakerId = p.Id; break;
                default: tactic.CornerTakerId = p.Id; break;
            }
            return;
        }

        int? previous = row < slots ? tactic.Lineup[row] : row - slots < tactic.Bench.Count ? tactic.Bench[row - slots] : null;
        var fromSlot = tactic.Lineup.IndexOf(p.Id);
        var fromBench = tactic.Bench.IndexOf(p.Id);

        // Tauschen: der bisherige Spieler übernimmt den alten Platz des neuen.
        if (fromSlot >= 0) tactic.Lineup[fromSlot] = previous;
        if (fromBench >= 0)
        {
            if (previous is { } prev) tactic.Bench[fromBench] = prev;
            else tactic.Bench.RemoveAt(fromBench);
        }

        if (row < slots) tactic.Lineup[row] = p.Id;
        else
        {
            var b = row - slots;
            if (b < tactic.Bench.Count) tactic.Bench[b] = p.Id;
            else tactic.Bench.Add(p.Id);
        }
        tactic.Bench = tactic.Bench.Distinct().Where(id => !tactic.Lineup.Contains(id)).ToList();
    }
}
