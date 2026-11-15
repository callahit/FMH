using CommunityToolkit.Mvvm.Input;

namespace Fmh.Desktop.ViewModels;

/// <summary>Zugang zum Privatleben (AP 13b) von der Karriere-Seite.</summary>
public sealed partial class ClubPageViewModel
{
    public bool HasPrivateLife => Session.PrivateLife is not null;

    [RelayCommand]
    private void OpenPrivateLife() => Shell.ShowDialog(new PrivateLifeViewModel(Shell));
}
