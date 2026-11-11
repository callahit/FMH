using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Stadionneubau (Sonderthema, nur mit Grundstück der Stadt): Entwurf, Kosten, Wirkung und Auftrag.</summary>
public sealed partial class StadiumPageViewModel
{
    // Stadionneubau (Sonderthema, nur mit Grundstück der Stadt)
    [ObservableProperty] private bool _hasSite;
    [ObservableProperty] private string _siteText = "";
    [ObservableProperty] private decimal _designCapacity = 30_000;
    [ObservableProperty] private decimal _designStanding = 25;
    [ObservableProperty] private decimal _designBoxes = 40;
    [ObservableProperty] private bool _designRoof = true;
    [ObservableProperty] private string _designName = "";
    [ObservableProperty] private string _designCost = "";
    [ObservableProperty] private string _designImpact = "";
    [ObservableProperty] private IBrush? _designImpactBrush;
    [ObservableProperty] private decimal _designMax = 60_000;
    private bool _designReady;

    partial void OnDesignCapacityChanged(decimal value) => UpdateDesign();
    partial void OnDesignStandingChanged(decimal value) => UpdateDesign();
    partial void OnDesignBoxesChanged(decimal value) => UpdateDesign();
    partial void OnDesignRoofChanged(bool value) => UpdateDesign();

    private StadiumDesign Design => new()
    {
        Capacity = (int)DesignCapacity, StandingPercent = (int)DesignStanding, Boxes = (int)DesignBoxes, Roof = DesignRoof, Name = DesignName,
    };

    private void RefreshNewStadium()
    {
        var s = Session;
        var club = Club;
        var running = club.Grounds.Projects.FirstOrDefault(p => p.Kind == ProjectKind.NewStadium && p.State is not (ProjectState.Done or ProjectState.Rejected));
        if (running is not null)
        {
            HasSite = false;
            SiteText = T.Format("newStadium.running", T.Render(s.ProjectName(running)), running.End is { } e ? Fmt.Date(e) : "–");
            return;
        }
        if (s.AvailableSite(club) is not { } site)
        {
            HasSite = false;
            SiteText = T["newStadium.noSite"];
            return;
        }
        HasSite = true;
        DesignMax = site.MaxCapacity;
        if (!_designReady)
        {
            _designReady = true;
            DesignCapacity = Math.Min(site.MaxCapacity, Math.Max(15_000, (club.Stadium.Capacity + 10_000) / 1000 * 1000));
            DesignName = $"{club.City}-Arena";
        }
        SiteText = T.Format("newStadium.site", Fmt.Int(site.MaxCapacity), Fmt.Date(site.Expires), M(site.LandPrice), M(s.StadiumLoanCapacity(club)));
        UpdateDesign();
    }

    private void UpdateDesign()
    {
        if (!HasSite) return;
        var s = Session;
        var design = Design;
        var cost = MarketValue.Round(s.NewStadiumCost(Club, design) * GameSession.ContractorFactors(Contractor).cost);
        DesignCost = T.Format("newStadium.cost", M(cost), Days(Data.Countries.First(c => c.Code == Club.CountryCode).Finance.StadiumBuildDays));
        var impact = s.ForecastNewStadium(Club, design, Contractor);
        DesignImpact = ImpactText(impact);
        DesignImpactBrush = ImpactBrush(impact);
    }

    [RelayCommand]
    private void OrderNewStadium()
    {
        var design = Design;
        var cost = Session.NewStadiumCost(Club, design);
        Shell.ShowDialog(new ConfirmDialogViewModel(T["newStadium.title"], T.Format("newStadium.confirm", Fmt.Int(design.Capacity), M(cost)),
            () => Show(Session.OrderNewStadium(design, Contractor, Financed), "build.ordered")));
    }
}
