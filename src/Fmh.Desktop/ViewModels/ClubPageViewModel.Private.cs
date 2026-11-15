using CommunityToolkit.Mvvm.Input;

namespace Fmh.Desktop.ViewModels;

/// <summary>Zugang zu Privatleben (AP 13b) und Grauzonen (AP 13c) von der Karriere-Seite.</summary>
public sealed partial class ClubPageViewModel
{
    public bool HasPrivateLife => Session.PrivateLife is not null;

    [RelayCommand]
    private void OpenPrivateLife() => Shell.ShowDialog(new PrivateLifeViewModel(Shell));

    public bool HasGrayZones => Session.GrayZonesOn && !Session.AwaitingNewJob;

    [RelayCommand]
    private void OpenGrayZones() => Shell.ShowDialog(new GrayZonesViewModel(Shell));
}
