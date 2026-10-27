using Avalonia.Controls;
using Avalonia.Input;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Views;

public partial class SquadPageView : UserControl
{
    public SquadPageView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SquadPageViewModel vm) vm.OpenPlayer(null);
    }
}
