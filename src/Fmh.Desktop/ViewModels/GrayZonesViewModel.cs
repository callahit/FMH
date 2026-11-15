using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record GrayRow(string Name, string Info, string Detail, IRelayCommand Command);

/// <summary>Grauzonen (AP 13c): Spiel-Taten vor dem nächsten Pflichtspiel, Akte des Managers.</summary>
public sealed partial class GrayZonesViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    public GrayZonesViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Load();
    }

    [ObservableProperty] private string _next = "";
    [ObservableProperty] private string _record = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private IReadOnlyList<GrayRow> _actions = Array.Empty<GrayRow>();

    private void Load()
    {
        if (Data.GrayZones is not { } rules) return;
        var club = Session.HumanClub;
        var currency = UiText.Currency(club);
        Next = Session.GrayZoneFixture is { } f
            ? T.Format("gray.next", $"{Fmt.Date(f.Date)} – {Session.World.Clubs[f.HomeId].Name} : {Session.World.Clubs[f.AwayId].Name}")
            : T["gray.noNext"];
        var file = Session.GrayZoneFile;
        Record = T.Format("gray.record", file?.Deeds.Count ?? 0, file?.Deeds.Count(d => d.Detected) ?? 0, rules.SackAfter);
        Actions = Session.GrayActions.Where(a => a.Kind == GrayActionKind.Match).Select(a => new GrayRow(
            T["gray." + a.Id + ".name"], T["gray." + a.Id + ".info"],
            T.Format("gray.cost", Fmt.Money(GrayZones.Scaled(a.Cost, club), currency), Fmt.Num(a.Detection * 100, "0")),
            new RelayCommand(() => Use(a.Id)))).ToList();
    }

    public void Use(string id)
    {
        var error = Session.UseGrayZone(id);
        Message = error is null ? T["gray.done"] : T[error];
        Load();
        _shell.RefreshHeader();
    }

    [RelayCommand]
    private void Close() => MainWindowViewModel.Instance?.CloseDialog();
}
