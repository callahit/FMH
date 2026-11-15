using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data;

namespace Fmh.Desktop.ViewModels;

/// <summary>Vertragstrick (AP 13c, Grauzonen) in der Spielerphase einer Verhandlung.</summary>
public sealed partial class NegotiationViewModel
{
    private GrayAction? TrickAction => Session.GrayActions.FirstOrDefault(a => a.Kind == GrayActionKind.Negotiation);

    public bool CanTrick => TrickAction is not null && PlayerStage && !Finished && _n.WageDemand is not null && !_n.Incoming;

    public string TrickText => TrickAction is { } a ? T["gray." + a.Id + ".name"] : "";
    public string TrickInfo => TrickAction is { } a ? T["gray." + a.Id + ".info"] + " " + T.Format("gray.risk", Fmh.Core.Fmt.Num(a.Detection * 100, "0")) : "";

    partial void OnPlayerStageChanged(bool value) => OnPropertyChanged(nameof(CanTrick));
    partial void OnFinishedChanged(bool value) => OnPropertyChanged(nameof(CanTrick));

    [RelayCommand]
    private void Trick()
    {
        if (TrickAction is not { } a) return;
        var error = Session.UseGrayZone(a.Id, _n);
        Update();
        Error = error is null ? T["gray.done"] : T[error];
        OnPropertyChanged(nameof(CanTrick));
    }
}
