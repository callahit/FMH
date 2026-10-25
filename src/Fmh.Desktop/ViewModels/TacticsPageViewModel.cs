using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Ein Platz in der Formation auf dem Spielfeld.</summary>
public sealed partial class SlotVm : ObservableObject
{
    public required int Index { get; init; }
    public required string Position { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public Player? Player { get; init; }
    public string Name => Player?.LastName ?? "—";
    public string StrengthText { get; init; } = "";
    public IBrush StrengthBrush { get; init; } = Brushes.Gray;
    public IBrush ShirtBrush { get; init; } = Brushes.Gray;
    public IBrush ShirtFore { get; init; } = Brushes.White;
    public IBrush RingBrush { get; init; } = Brushes.Transparent;
    public string Tip { get; init; } = "";

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Spieler in der Bank- oder Reserveliste.</summary>
public sealed partial class SquadEntryVm : ObservableObject
{
    public required Player Player { get; init; }
    public required string Position { get; init; }
    public required string StrengthText { get; init; }
    public required IBrush StrengthBrush { get; init; }
    public required double Condition { get; init; }
    public required IBrush ConditionBrush { get; init; }
    public required string Status { get; init; }
    public required IBrush StatusBrush { get; init; }
    public int? BenchIndex { get; init; }
    public string Name => Player.Name;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Auswahl-Einstellung (ComboBox), schreibt direkt in die Taktik.</summary>
public sealed partial class ChoiceSetting : ObservableObject
{
    private readonly Action<int> _apply;
    private bool _loading;

    public ChoiceSetting(string label, IReadOnlyList<string> options, int selected, Action<int> apply)
    {
        Label = label;
        Options = options;
        _apply = apply;
        _loading = true;
        SelectedIndex = selected;
        _loading = false;
    }

    public string Label { get; }
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] private int _selectedIndex;

    public event Action? Changed;

    partial void OnSelectedIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        _apply(value);
        Changed?.Invoke();
    }
}

public sealed record AdviceVm(string Text, IBrush Brush);

/// <summary>
/// Taktik (Kapitel 5): Formation auf dem Spielfeld mit Drag &amp; Drop, Bank, Mannschaftsanweisungen,
/// Standardschützen und Co-Trainer-Feedback.
/// </summary>
public sealed partial class TacticsPageViewModel : PageViewModel, IRefreshable
{
    private int? _selectedTarget;

    public TacticsPageViewModel(ShellViewModel shell) : base(shell) => Refresh();

    public override string Title => T["menu.tactics"];

    private Club Club => Session.HumanClub;
    private Formation Formation => Data.Formation(Club.Tactic.FormationId);
    private int BenchSize => Session.BenchSize(Club);

    [ObservableProperty] private IReadOnlyList<SlotVm> _slots = Array.Empty<SlotVm>();
    [ObservableProperty] private IReadOnlyList<SquadEntryVm> _bench = Array.Empty<SquadEntryVm>();
    [ObservableProperty] private IReadOnlyList<SquadEntryVm> _reserves = Array.Empty<SquadEntryVm>();
    [ObservableProperty] private IReadOnlyList<ChoiceSetting> _settings = Array.Empty<ChoiceSetting>();
    [ObservableProperty] private IReadOnlyList<ChoiceSetting> _setPieces = Array.Empty<ChoiceSetting>();
    [ObservableProperty] private IReadOnlyList<AdviceVm> _advice = Array.Empty<AdviceVm>();
    [ObservableProperty] private string _benchTitle = "";
    [ObservableProperty] private string _hint = "";

    public void Refresh()
    {
        var tactic = Club.Tactic;
        var profiles = Data.PositionProfiles;
        var formation = Formation;
        while (tactic.Lineup.Count < formation.Slots.Count) tactic.Lineup.Add(null);
        var shirt = Palette.ClubColor(Club.PrimaryColor);
        Player? P(int? id) => id is { } v && Session.World.Players.TryGetValue(v, out var p) ? p : null;

        Slots = formation.Slots.Select((slot, i) =>
        {
            var player = P(tactic.Lineup[i]);
            var rating = player is null ? 0 : Ratings.EffectiveRating(player, slot.Position, profiles);
            var suit = player?.SuitabilityFor(slot.Position) ?? Suitability.Natural;
            var ring = player is null ? Palette.Current.Muted
                : !player.IsAvailable || suit == Suitability.Unsuitable ? Palette.Current.Bad
                : suit == Suitability.Adequate ? Palette.Current.Warn : Avalonia.Media.Colors.White;
            return new SlotVm
            {
                Index = i,
                Position = UiText.Position(slot.Position),
                X = 0.1 + slot.Col * 0.2,
                Y = 0.9 - slot.Row * 0.152,
                Player = player,
                StrengthText = player is null ? "" : UiText.Strength(Ratings.StrengthFromRating(rating)),
                StrengthBrush = new SolidColorBrush(Palette.Current.AttributeColor((int)Math.Round(rating))),
                ShirtBrush = new SolidColorBrush(shirt),
                ShirtFore = new SolidColorBrush(Palette.Contrast(shirt)),
                RingBrush = new SolidColorBrush(ring),
                Tip = player is null ? T["tactics.emptySlot"] : $"{player.Name} – {T["suit." + suit]} {T["tactics.asPosition"]} {UiText.Position(slot.Position)}",
                IsSelected = _selectedTarget == i,
            };
        }).ToList();

        SquadEntryVm Entry(Player p, int? bench) => new()
        {
            Player = p,
            Position = UiText.Position(p.MainPosition),
            StrengthText = UiText.Strength(Ratings.Strength(p, profiles)),
            StrengthBrush = new SolidColorBrush(Palette.Current.AttributeColor((int)Math.Round(Ratings.PositionRating(p, p.MainPosition, profiles)))),
            Condition = p.State.Condition,
            ConditionBrush = new SolidColorBrush(Palette.Current.ConditionColor(p.State.Condition)),
            Status = p.IsAvailable ? "" : UiText.Status(p, Club),
            StatusBrush = new SolidColorBrush(Palette.Current.Bad),
            BenchIndex = bench,
            IsSelected = bench is { } b && _selectedTarget == formation.Slots.Count + b,
        };
        Bench = tactic.Bench.Select((id, i) => (P(id), i)).Where(x => x.Item1 is not null).Select(x => Entry(x.Item1!, x.i)).ToList();
        var used = tactic.Lineup.Where(id => id.HasValue).Select(id => id!.Value).Concat(tactic.Bench).ToHashSet();
        Reserves = Session.World.SquadOf(Club).Where(p => !used.Contains(p.Id))
            .OrderBy(p => (int)p.MainPosition).ThenByDescending(p => Ratings.Strength(p, profiles))
            .Select(p => Entry(p, null)).ToList();
        BenchTitle = T.Format("tactics.benchCount", tactic.Bench.Count, BenchSize);

        BuildSettings();
        Advice = TacticAdvisor.Analyse(Club, Session.World, Data)
            .Select(a => new AdviceVm(T.Format(a.Key, a.Args.Select(x => x is Position pos ? (object)UiText.Position(pos) : x).ToArray()),
                new SolidColorBrush(a.Warning ? Palette.Current.Warn : Palette.Current.Good)))
            .ToList();
        Hint = _selectedTarget is null ? T["tactics.hintSelect"] : T["tactics.hintAssign"];
        Shell.RefreshHeader();
    }

    private void BuildSettings()
    {
        var t = Club.Tactic;
        var ins = t.Instructions;
        string[] Names<TEnum>(string prefix) where TEnum : struct, Enum => Enum.GetValues<TEnum>().Select(v => T[prefix + v]).ToArray();
        int Idx<TEnum>(TEnum v) where TEnum : struct, Enum => Array.IndexOf(Enum.GetValues<TEnum>(), v);
        TEnum At<TEnum>(int i) where TEnum : struct, Enum => Enum.GetValues<TEnum>()[i];
        var yesNo = new[] { T["ui.no"], T["ui.yes"] };

        var list = new List<ChoiceSetting>
        {
            new(T["tactic.formation"], Data.Formations.Select(f => f.Name).ToList(), Data.Formations.FindIndex(f => f.Id == t.FormationId), i =>
            {
                var current = t.Lineup.Where(id => id.HasValue).Select(id => Session.World.Players[id!.Value]).ToList();
                t.FormationId = Data.Formations[i].Id;
                t.Lineup = LineupSelector.Assign(Data.Formations[i], current, Data.PositionProfiles);
            }),
            new(T["tactic.mentality"], Names<Mentality>("mentality."), Idx(t.Mentality), i => t.Mentality = At<Mentality>(i)),
            new(T["tactic.passing"], Names<PassingLength>("passing."), Idx(ins.PassingLength), i => ins.PassingLength = At<PassingLength>(i)),
            new(T["tactic.tempo"], Names<Tempo>("tempo."), Idx(ins.Tempo), i => ins.Tempo = At<Tempo>(i)),
            new(T["tactic.width"], Names<Width>("width."), Idx(ins.Width), i => ins.Width = At<Width>(i)),
            new(T["tactic.focus"], Names<AttackFocus>("focus."), Idx(ins.Focus), i => ins.Focus = At<AttackFocus>(i)),
            new(T["tactic.counter"], yesNo, ins.Counter ? 1 : 0, i => ins.Counter = i == 1),
            new(T["tactic.pressing"], Names<PressingHeight>("pressing."), Idx(ins.Pressing), i => ins.Pressing = At<PressingHeight>(i)),
            new(T["tactic.line"], Names<DefensiveLine>("line."), Idx(ins.Line), i => ins.Line = At<DefensiveLine>(i)),
            new(T["tactic.offsideTrap"], yesNo, ins.OffsideTrap ? 1 : 0, i => ins.OffsideTrap = i == 1),
            new(T["tactic.tackling"], Names<TacklingStyle>("tackling."), Idx(ins.Tackling), i => ins.Tackling = At<TacklingStyle>(i)),
            new(T["tactic.gegenpressing"], yesNo, ins.Gegenpressing ? 1 : 0, i => ins.Gegenpressing = i == 1),
            new(T["tactic.distribution"], Names<Distribution>("distribution."), Idx(ins.GoalkeeperDistribution), i => ins.GoalkeeperDistribution = At<Distribution>(i)),
        };
        foreach (var s in list) s.Changed += Refresh;
        Settings = list;

        var xi = t.Lineup.Where(id => id.HasValue).Select(id => Session.World.Players[id!.Value]).ToList();
        var names = xi.Select(p => p.Name).ToList();
        ChoiceSetting Taker(string key, int? current, Action<int> set)
        {
            var s = new ChoiceSetting(T[key], names, current is { } c ? xi.FindIndex(p => p.Id == c) : -1, set);
            s.Changed += Refresh;
            return s;
        }
        SetPieces = new List<ChoiceSetting>
        {
            Taker("tactics.captain", t.CaptainId, i => t.CaptainId = xi[i].Id),
            Taker("tactics.penalty", t.PenaltyTakerId, i => t.PenaltyTakerId = xi[i].Id),
            Taker("tactics.freeKick", t.FreeKickTakerId, i => t.FreeKickTakerId = xi[i].Id),
            Taker("tactics.corner", t.CornerTakerId, i => t.CornerTakerId = xi[i].Id),
        };
    }

    // ------------------------------------------------------------------ Bedienung

    /// <summary>Klick auf einen Platz: auswählen; ist bereits ein anderer Platz gewählt, tauschen.</summary>
    [RelayCommand]
    public void SelectSlot(SlotVm? slot)
    {
        if (slot is null) return;
        var slots = Formation.Slots.Count;
        if (_selectedTarget is { } sel && sel != slot.Index && sel < slots)
        {
            LineupEditor.Swap(Club.Tactic, sel, slot.Index);
            _selectedTarget = null;
        }
        else if (_selectedTarget is { } benchSel && benchSel >= slots && Bench.FirstOrDefault(b => b.BenchIndex == benchSel - slots) is { } benchEntry)
        {
            LineupEditor.Place(Club.Tactic, slots, BenchSize, slot.Index, benchEntry.Player.Id);
            _selectedTarget = null;
        }
        else _selectedTarget = _selectedTarget == slot.Index ? null : slot.Index;
        Refresh();
    }

    /// <summary>Klick auf einen Spieler der Listen: auf den gewählten Platz setzen oder den Bankplatz auswählen.</summary>
    [RelayCommand]
    public void ClickPlayer(SquadEntryVm? entry)
    {
        if (entry is null) return;
        var slots = Formation.Slots.Count;
        if (_selectedTarget is { } target)
        {
            LineupEditor.Place(Club.Tactic, slots, BenchSize, target, entry.Player.Id);
            _selectedTarget = null;
        }
        else if (entry.BenchIndex is { } b) _selectedTarget = slots + b;
        else
        {
            if (!LineupEditor.ToBench(Club.Tactic, BenchSize, entry.Player.Id))
                Shell.StatusMessage = T["tactics.benchFull"];
        }
        Refresh();
    }

    /// <summary>Drag &amp; Drop: Spieler auf einen Platz ziehen.</summary>
    public void DropOnSlot(int slotIndex, int playerId)
    {
        LineupEditor.Place(Club.Tactic, Formation.Slots.Count, BenchSize, slotIndex, playerId);
        _selectedTarget = null;
        Refresh();
    }

    public void DropOnBench(int playerId)
    {
        if (!LineupEditor.ToBench(Club.Tactic, BenchSize, playerId)) Shell.StatusMessage = T["tactics.benchFull"];
        Refresh();
    }

    public void DropOnReserves(int playerId)
    {
        LineupEditor.Remove(Club.Tactic, playerId);
        Refresh();
    }

    [RelayCommand]
    private void RemoveFromBench(SquadEntryVm? entry)
    {
        if (entry is null) return;
        LineupEditor.Remove(Club.Tactic, entry.Player.Id);
        Refresh();
    }

    [RelayCommand]
    private void AutoPick()
    {
        LineupSelector.PickBest(Club, Session.World, Data, BenchSize);
        _selectedTarget = null;
        Shell.StatusMessage = T["tactics.autoDone"];
        Refresh();
    }

    [RelayCommand]
    private void OpenPlayer(SquadEntryVm? entry)
    {
        if (entry is null) return;
        var list = Session.World.SquadOf(Club).ToList();
        Shell.ShowSubPage(new PlayerPageViewModel(Shell, list, list.IndexOf(entry.Player)));
    }

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (key == Avalonia.Input.Key.A) { AutoPick(); return true; }
        if (key == Avalonia.Input.Key.Escape && _selectedTarget is not null)
        {
            _selectedTarget = null;
            Refresh();
            return true;
        }
        return false;
    }
}
