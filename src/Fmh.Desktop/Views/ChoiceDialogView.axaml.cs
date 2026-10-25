using Avalonia.Controls;
using Avalonia.Input;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Views;

public partial class ChoiceDialogView : UserControl
{
    public ChoiceDialogView() => InitializeComponent();

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ChoiceOption option } && DataContext is ChoiceDialogViewModel vm)
            vm.ChooseCommand.Execute(option);
    }
}
