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
    public string RoleText { get; init; } = "";

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

    /// <summary>Während eine Seite ihre Einstellungen neu aufbaut, schreiben Auswahlfelder keine Werte zurück.</summary>
    public static int Suspended { get; set; }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_loading || value < 0 || Suspended > 0) return;
        _apply(value);
        Changed?.Invoke();
    }
}

public sealed record AdviceVm(string Text, IBrush Brush);
public sealed record SavedTacticVm(int Index, string Label, bool Used);
