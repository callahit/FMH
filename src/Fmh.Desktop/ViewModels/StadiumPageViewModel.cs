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

public sealed record BuildAction(string Label, string Cost, IRelayCommand Command, string Impact = "", IBrush? ImpactBrush = null);

public sealed record StandCard(string Name, string Places, string Details, double Fill, IReadOnlyList<BuildAction> Actions);

public sealed record ProjectLine(string Name, string State, string Dates, string Cost, IBrush Brush);

/// <summary>Kachel auf dem Geländeraster: Gebäude, Stadion oder freies Feld.</summary>
public sealed record GroundTile(double Left, double Top, double Width, double Height, string Label, string Sub, IBrush Fill, IBrush Border,
    int X, int Y, Building? Building, bool IsStadium, IRelayCommand Select, Geometry? Icon = null, bool IsPath = false)
{
    /// <summary>Text auf der Kachel; Wege bleiben ohne Beschriftung (nur Tooltip).</summary>
    public string Caption => IsPath ? "" : Label;
}

public sealed record BuildOption(string Type, string Name, string Category, string Cost, string Effects, string Upkeep, bool Possible, string Hint, IRelayCommand Build, IRelayCommand Pick, Geometry? Icon = null, IBrush? IconBrush = null);

public sealed record BuildingListRow(string Name, string Category, string Level, string Condition, string Effects, string Link, Building Building, IRelayCommand Select);

/// <summary>Stadion (Kapitel 8) und Vereinsgelände (Kapitel 9).</summary>
public sealed partial class StadiumPageViewModel : PageViewModel, IRefreshable
{
    public const double Cell = 58;

    public StadiumPageViewModel(ShellViewModel shell) : base(shell)
    {
        Contractors = Enum.GetValues<Contractor>().Select(c => T["contractor." + c]).ToList();
        Categories = new[] { T["build.allCategories"] }.Concat(CategoryIds.Select(c => T["buildingCat." + c])).ToList();
        Refresh();
    }

    private static readonly string[] CategoryIds = { "training", "medical", "youth", "fans", "commerce", "admin", "infrastructure", "energy", "teams" };

    private Club Club => Session.HumanClub;
    private string Currency => UiText.Currency(Club);
    private string M(long v) => Fmt.Money(v, Currency);
    public override string Title => T["menu.stadium"];

    public IReadOnlyList<string> Contractors { get; }
    public IReadOnlyList<string> Categories { get; }
    [ObservableProperty] private int _contractorIndex = (int)Contractor.Standard;
    [ObservableProperty] private bool _financed;
    [ObservableProperty] private int _categoryIndex;
    [ObservableProperty] private string? _result;
    [ObservableProperty] private string _stadiumTitle = "";
    [ObservableProperty] private IReadOnlyList<InfoLine> _stadiumFacts = Array.Empty<InfoLine>();
    [ObservableProperty] private StadiumPlanModel? _plan;
    [ObservableProperty] private IReadOnlyList<StandCard> _stands = Array.Empty<StandCard>();
    [ObservableProperty] private IReadOnlyList<BuildAction> _stadiumActions = Array.Empty<BuildAction>();
    [ObservableProperty] private IReadOnlyList<GroundTile> _tiles = Array.Empty<GroundTile>();
    [ObservableProperty] private double _gridWidth;
    [ObservableProperty] private double _gridHeight;
    [ObservableProperty] private IReadOnlyList<ProjectLine> _projects = Array.Empty<ProjectLine>();
    [ObservableProperty] private string _groundsText = "";
    [ObservableProperty] private IsoMapState? _isoMap;
    [ObservableProperty] private int _toolIndex; // 0 select, 1 build, 2 path
    [ObservableProperty] private bool _showPaths = true;
    [ObservableProperty] private string _toolHint = "";
    [ObservableProperty] private IReadOnlyList<BuildingListRow> _buildingRows = Array.Empty<BuildingListRow>();
    [ObservableProperty] private string? _paletteType;
    [ObservableProperty] private int _previewRotation;

    // Auswahl auf dem Gelände
    [ObservableProperty] private string _selectionTitle = "";
    [ObservableProperty] private string _selectionText = "";
    [ObservableProperty] private bool _hasBuilding;
    [ObservableProperty] private bool _isFreeCell;
    [ObservableProperty] private string _upgradeText = "";
    [ObservableProperty] private string _leaseText = "";
    [ObservableProperty] private IReadOnlyList<BuildOption> _buildOptions = Array.Empty<BuildOption>();
    private Building? _selected;
    private (int x, int y)? _cell;
    private (int x, int y)? _hover;

    public bool NoProjects => Projects.Count == 0;
    partial void OnProjectsChanged(IReadOnlyList<ProjectLine> value) => OnPropertyChanged(nameof(NoProjects));
    partial void OnCategoryIndexChanged(int value)
    {
        UpdateSelection();
        RefreshPalette();
    }

    private Contractor Contractor => (Contractor)Math.Clamp(ContractorIndex, 0, 2);

    public void Refresh()
    {
        var s = Session;
        var club = Club;
        var st = club.Stadium;
        StadiumTitle = s.StadiumDisplayName(club);
        Plan = new StadiumPlanModel(
            st.Stands.ToDictionary(x => x.Side.ToString(), x => x.Capacity),
            st.Stands.Where(x => x.Roof).Select(x => x.Side.ToString()).ToHashSet(),
            st.Stands.Where(x => x.Boxes > 0).Select(x => x.Side.ToString()).ToHashSet(),
            st.Floodlight, SeatColor(club),
            st.Stands.ToDictionary(x => x.Side.ToString(), x => $"{T["stand." + x.Side]} · {Fmt.Int(x.Capacity)}"));
        StadiumFacts = new List<InfoLine>
        {
            new(T["stadium.capacity"], Fmt.Int(st.Capacity)),
            new(T["stadium.standing"], Fmt.Int(st.StandingTotal)),
            new(T["stadium.seating"], Fmt.Int(st.SeatingTotal)),
            new(T["stadium.boxes"], Fmt.Int(st.Boxes)),
            new(T["stadium.roof"], Fmt.Num(st.RoofShare * 100, "0") + " %"),
            new(T["stadium.floodlight"], $"{st.Floodlight}/4"),
            new(T["stadium.heating"], st.UndersoilHeating ? T["ui.yes"] : T["ui.no"]),
            new(T["stadium.scoreboard"], $"{st.Scoreboard}/3"),
            new(T["stadium.pitch"], Fmt.Num(st.Pitch, "0") + " %", new SolidColorBrush(st.Pitch < 50 ? P.Bad : st.Pitch < 75 ? P.Warn : P.Good)),
        };
        var max = Math.Max(1, st.Stands.Max(x => x.Capacity));
        Stands = st.Stands.Select(x =>
        {
            var side = x.Side;
            var actions = new List<BuildAction>();
            void Add(ProjectKind kind, string label)
            {
                if (s.StadiumProjectQuote(club, kind, side) is not { } q) return;
                var impact = s.ForecastProject(club, kind, side, Contractor);
                actions.Add(new BuildAction(label, T.Format("build.costDays", M(Cost(q.cost)), Days(q.days)),
                    new RelayCommand(() => Show(s.OrderStadiumProject(kind, side, Contractor, Financed), "build.ordered")), ImpactText(impact), ImpactBrush(impact)));
            }
            Add(ProjectKind.Standing, T["build.addStanding"]);
            Add(ProjectKind.Seating, T["build.addSeating"]);
            Add(ProjectKind.Roof, T["build.addRoof"]);
            Add(ProjectKind.Boxes, T["build.addBoxes"]);
            var details = T.Format("stand.details", Fmt.Int(x.Standing), Fmt.Int(x.Seating), x.Boxes, x.Roof ? T["stand.roofed"] : T["stand.open"]);
            if (x.Closed > 0) details += " · " + T.Format("stand.closed", Fmt.Int(x.Closed));
            return new StandCard(T["stand." + side], Fmt.Int(x.Capacity), details, x.Capacity / (double)max, actions);
        }).ToList();
        var whole = new List<BuildAction>();
        void AddWhole(ProjectKind kind, string label)
        {
            if (s.StadiumProjectQuote(club, kind, null) is not { } q) return;
            var impact = s.ForecastProject(club, kind, null, Contractor);
            whole.Add(new BuildAction(label, T.Format("build.costDays", M(Cost(q.cost)), Days(q.days)),
                new RelayCommand(() => Show(s.OrderStadiumProject(kind, null, Contractor, Financed), "build.ordered")), ImpactText(impact), ImpactBrush(impact)));
        }
        AddWhole(ProjectKind.Floodlight, T["project.Floodlight"]);
        AddWhole(ProjectKind.Heating, T["project.Heating"]);
        AddWhole(ProjectKind.Scoreboard, T["project.Scoreboard"]);
        StadiumActions = whole;
        RefreshNewStadium();

        BuildTiles();
        BuildIsoMap();
        BuildBuildingRows();
        Projects = club.Grounds.Projects.OrderByDescending(p => p.Ordered).Take(30).Select(p => new ProjectLine(
            T.Render(s.ProjectName(p)),
            T["projectState." + p.State],
            p.End is { } end ? T.Format("build.until", Fmt.Date(end)) : Fmt.Date(p.Ordered),
            M(p.Cost),
            new SolidColorBrush(p.State switch { ProjectState.Done => P.Good, ProjectState.Rejected => P.Bad, ProjectState.Building => P.Accent, ProjectState.Queued => P.Warn, _ => P.Warn }))).ToList();
        GroundsText = T.Format("build.groundsInfo", club.Grounds.Buildings.Count, M(club.Grounds.Buildings.Sum(b => s.MonthlyUpkeep(club, b))),
            M(GameSession.LandPrice(club) * club.Grounds.Height));
        ToolHint = ToolIndex switch
        {
            1 => T["build.rotateHint"],
            2 => T["build.pathHint"],
            _ => T["build.isoHelp"],
        };
        UpdateSelection();
    }

    partial void OnToolIndexChanged(int value)
    {
        if (value != 1) PaletteType = null;
        BuildIsoMap();
        ToolHint = value switch
        {
            1 => T["build.rotateHint"],
            2 => T["build.pathHint"],
            _ => T["build.isoHelp"],
        };
    }

    partial void OnShowPathsChanged(bool value) => BuildIsoMap();

    private void BuildBuildingRows()
    {
        var s = Session;
        var club = Club;
        BuildingRows = club.Grounds.Buildings.OrderBy(b => T["building." + b.Type]).Select(b =>
        {
            var def = Data.Building(b.Type);
            var linked = s.IsBuildingConnected(club, b);
            var building = b;
            return new BuildingListRow(
                T["building." + b.Type],
                def is null ? "" : T["buildingCat." + def.Category],
                b.UnderConstruction ? T["build.underConstruction"] : T.Format("build.level", b.Level),
                T.Format("build.condition", (int)Math.Round(b.Condition)),
                def is null ? "" : EffectText(def, b.Level),
                linked ? T["build.connected"] : T["build.disconnected"],
                b,
                new RelayCommand(() => SelectCell(building, null)));
        }).ToList();
    }

    [RelayCommand]
    private void SelectTool(string tool)
    {
        ToolIndex = tool switch { "build" => 1, "path" => 2, _ => 0 };
    }

    [RelayCommand]
    private void PickBuilding(string type)
    {
        PaletteType = type;
        ToolIndex = 1;
        BuildIsoMap();
    }

    [RelayCommand]
    private void SelectListBuilding(Building building) => SelectCell(building, null);

    private long Cost(long baseCost) => MarketValue.Round(baseCost * GameSession.ContractorFactors(Contractor).cost);
    private int Days(int baseDays) => (int)Math.Round(baseDays * GameSession.ContractorFactors(Contractor).days);

    partial void OnContractorIndexChanged(int value) => Refresh();

    /// <summary>Prognose als Text: Zuschauer, Mehreinnahmen, Unterhalt, Amortisation.</summary>
    private string ImpactText(StadiumImpact? i)
    {
        if (i is null) return "";
        var payback = i.PaybackYears is { } y ? T.Format("forecast.payback", Fmt.Num(y, "0.0")) : T["forecast.noPayback"];
        return T.Format("forecast.line", i.ExtraSpectators >= 0 ? "+" + Fmt.Int(i.ExtraSpectators) : Fmt.Int(i.ExtraSpectators),
            M(i.ExtraIncome), M(i.ExtraUpkeep), payback);
    }

    private IBrush ImpactBrush(StadiumImpact? i) => new SolidColorBrush(i?.PaybackYears switch { null => P.Muted, < 8 => P.Good, < 20 => P.Warn, _ => P.Bad });

    private void Show(string? error, string okKey)
    {
        Result = error is null ? T[okKey] : T[error];
        Refresh();
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void Upgrade()
    {
        if (_selected is { } b) Show(Session.UpgradeBuilding(b, Contractor, Financed), "build.ordered");
    }

    [RelayCommand]
    private void Demolish()
    {
        if (_selected is not { } b) return;
        var name = T["building." + b.Type];
        Shell.ShowDialog(new ConfirmDialogViewModel(T["build.demolish"], T.Format("build.confirmDemolish", name), () =>
        {
            _selected = null;
            Show(Session.Demolish(b), "build.demolished");
        }));
    }

    [RelayCommand]
    private void Lease()
    {
        if (_selected is not { } b) return;
        Session.ToggleLease(b);
        Refresh();
    }

    [RelayCommand]
    private void BuyLand() => Show(Session.BuyLand(), "build.landBought");
}
