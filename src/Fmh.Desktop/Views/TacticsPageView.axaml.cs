using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Views;

/// <summary>
/// Maussteuerung der Taktik: Klick wählt bzw. setzt, Ziehen eines Spielers auf einen Platz, die Bank
/// oder die Reserve verschiebt ihn.
/// </summary>
public partial class TacticsPageView : UserControl
{
    private static readonly DataFormat<string> Format = DataFormat.CreateStringApplicationFormat("fmh-player");
    private Point? _pressPoint;
    private Control? _pressed;

    public TacticsPageView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private TacticsPageViewModel? Vm => DataContext as TacticsPageViewModel;

    private void OnSlotTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SlotVm slot }) Vm?.SelectSlot(slot);
    }

    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SquadEntryVm entry }) Vm?.ClickPlayer(entry);
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SquadEntryVm entry }) Vm?.OpenPlayerCommand.Execute(entry);
    }

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressPoint = e.GetPosition(this);
        _pressed = sender as Control;
    }

    private async void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _pressed is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var d = e.GetPosition(this) - start;
        if (Math.Abs(d.X) + Math.Abs(d.Y) < 8) return;
        var id = _pressed.DataContext switch
        {
            SlotVm { Player: { } p } => p.Id,
            SquadEntryVm entry => entry.Player.Id,
            _ => (int?)null,
        };
        _pressPoint = null;
        _pressed = null;
        if (id is null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(Format, id.Value.ToString()));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(Format) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm is not { } vm || e.DataTransfer.TryGetValue(Format) is not { } s || !int.TryParse(s, out var playerId)) return;
        for (var c = e.Source as Control; c is not null; c = c.Parent as Control)
        {
            if (c.DataContext is SlotVm slot && c.GetValue(DragDrop.AllowDropProperty))
            {
                vm.DropOnSlot(slot.Index, playerId);
                return;
            }
            if (c == BenchArea) { vm.DropOnBench(playerId); return; }
            if (c == ReserveArea) { vm.DropOnReserves(playerId); return; }
        }
    }
}
