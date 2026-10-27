using Avalonia.Controls;
using Avalonia.Input;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Views;

public partial class TransfersPageView : UserControl
{
    public TransfersPageView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TransfersPageViewModel vm) vm.OpenPlayer(vm.Selected);
    }

    private void OnShortlistDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TransfersPageViewModel vm) vm.OpenPlayer(vm.SelectedShortlist);
    }
}
