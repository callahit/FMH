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

    private void BuildIsoMap()
    {
        var s = Session;
        var club = Club;
        s.EnsureGrounds(club);
        var g = club.Grounds;
        var width = s.GroundsWidth(club);
        var buildings = g.Buildings.Select(b =>
        {
            var def = Data.Building(b.Type);
            var (fw, fh) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            return new IsoBuildingVisual
            {
                Id = b.Id,
                X = b.X,
                Y = b.Y,
                Width = fw,
                Height = fh,
                Level = Math.Max(1, b.Level),
                Category = def?.Category ?? "",
                Label = T["building." + b.Type],
                Type = b.Type,
                UnderConstruction = b.UnderConstruction,
                Connected = s.IsBuildingConnected(club, b),
            };
        }).ToList();

        var state = new IsoMapState
        {
            Width = width,
            Height = g.Height,
            BaseWidth = g.Width,
            StadiumX = g.StadiumX,
            StadiumY = g.StadiumY,
            StadiumWidth = g.StadiumWidth,
            StadiumHeight = g.StadiumHeight,
            StadiumRoof = club.Stadium.RoofShare >= 0.5,
            StandHeights = club.Stadium.Stands.ToDictionary(st => st.Side.ToString(), st => Math.Clamp(st.Capacity / 5000 + 1, 1, 4)),
            StandCapacities = club.Stadium.Stands.ToDictionary(st => st.Side.ToString(), st => st.Capacity),
            RoofSides = club.Stadium.Stands.Where(st => st.Roof).Select(st => st.Side.ToString()).ToHashSet(),
            BoxSides = club.Stadium.Stands.Where(st => st.Boxes > 0).Select(st => st.Side.ToString()).ToHashSet(),
            Floodlight = club.Stadium.Floodlight,
            SeatColor = SeatColor(club),
            StadiumLabel = $"{s.StadiumDisplayName(club)} · {Fmt.Int(club.Stadium.Capacity)}",
            SelectedId = _selected?.Id,
            SelectedCell = _selected is null && _cell is { } sc ? new GridPos(sc.x, sc.y) : null,
            Paths = g.Paths.ToList(),
            Gates = g.Gates.ToList(),
            Buildings = buildings,
            ShowPaths = ShowPaths,
            Tool = ToolIndex switch { 1 => IsoMapTool.Build, 2 => IsoMapTool.Path, _ => IsoMapTool.Select },
            OnHover = OnIsoHover,
            OnClick = OnIsoClick,
            OnRightClick = OnIsoRightClick,
            OnRotate = RotatePreview,
            OnPathDrag = OnPathDrag,
            OnPathRelease = OnPathRelease,
        };
        UpdateGhost(state);
        IsoMap = state;
    }

    /// <summary>Sitzfarbe der Tribünen: Vereinsfarbe, zu dunkle oder zu helle Farben weichen der Zweitfarbe bzw. einem Rot.</summary>
    private static Color SeatColor(Club club)
    {
        static bool Usable(Color c) => c.R + c.G + c.B is > 90 and < 690;
        var primary = Palette.ClubColor(club.PrimaryColor);
        if (Usable(primary)) return primary;
        var secondary = Palette.ClubColor(club.SecondaryColor);
        return Usable(secondary) ? secondary : Color.Parse("#C8414A");
    }

    private void UpdateGhost(IsoMapState state)
    {
        if (ToolIndex == 1 && PaletteType is { } type && Data.Building(type) is { } def && _hover is { } h)
        {
            var (fw, fh) = BuildingFootprint.Size(def, PreviewRotation);
            var fits = Session.Fits(Club, def, h.x, h.y, PreviewRotation);
            var canPay = Financed || def.Cost <= Club.Budget.Balance;
            state.Ghost = new IsoGhost
            {
                X = h.x, Y = h.y, Width = fw, Height = fh,
                Valid = fits && canPay && (def.Multiple || Club.Grounds.Buildings.All(b => b.Type != def.Id)),
                Category = def.Category,
                CostLabel = M(Cost(def.Cost)),
            };
        }
        else state.Ghost = null;
    }

    private void OnIsoHover(int x, int y)
    {
        _hover = (x, y);
        if (IsoMap is { } map)
        {
            UpdateGhost(map);
            if (ToolIndex == 2) return;
            map.Ghost = map.Ghost;
            IsoMap = map;
            // Trigger redraw via new reference
            IsoMap = CloneMap(map);
        }
    }

    private static IsoMapState CloneMap(IsoMapState m) => new()
    {
        Width = m.Width, Height = m.Height, BaseWidth = m.BaseWidth,
        StadiumX = m.StadiumX, StadiumY = m.StadiumY, StadiumWidth = m.StadiumWidth, StadiumHeight = m.StadiumHeight,
        StadiumRoof = m.StadiumRoof, StandHeights = m.StandHeights, Paths = m.Paths, Gates = m.Gates, Buildings = m.Buildings,
        StandCapacities = m.StandCapacities, RoofSides = m.RoofSides, BoxSides = m.BoxSides, Floodlight = m.Floodlight,
        SeatColor = m.SeatColor, StadiumLabel = m.StadiumLabel, SelectedId = m.SelectedId, SelectedCell = m.SelectedCell,
        Ghost = m.Ghost, PathGhost = m.PathGhost, PathGhostValid = m.PathGhostValid, ShowPaths = m.ShowPaths, Tool = m.Tool,
        OnHover = m.OnHover, OnClick = m.OnClick, OnRightClick = m.OnRightClick, OnRotate = m.OnRotate,
        OnPathDrag = m.OnPathDrag, OnPathRelease = m.OnPathRelease,
    };

    private void OnIsoClick(int x, int y)
    {
        var s = Session;
        var club = Club;
        if (ToolIndex == 1 && PaletteType is { } type)
        {
            Show(s.OrderBuilding(type, x, y, Contractor, Financed, PreviewRotation), "build.ordered");
            return;
        }
        if (ToolIndex == 2) return;
        var hit = club.Grounds.Buildings.FirstOrDefault(b =>
        {
            var def = Data.Building(b.Type);
            var (fw, fh) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            return x >= b.X && x < b.X + fw && y >= b.Y && y < b.Y + fh;
        });
        if (hit is not null) SelectCell(hit, null);
        else if (x >= 0 && y >= 0 && x < s.GroundsWidth(club) && y < club.Grounds.Height)
            SelectCell(null, (x, y));
    }

    private void OnIsoRightClick(int x, int y)
    {
        var club = Club;
        var hit = club.Grounds.Buildings.FirstOrDefault(b =>
        {
            var def = Data.Building(b.Type);
            var (fw, fh) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            return x >= b.X && x < b.X + fw && y >= b.Y && y < b.Y + fh;
        });
        if (hit is not null)
        {
            SelectCell(hit, null);
            Demolish();
            return;
        }
        if (x >= club.Grounds.Width)
            Show(Session.BuyLand(), "build.landBought");
    }

    private void RotatePreview()
    {
        PreviewRotation = BuildingFootprint.NormalizeRotation(PreviewRotation + 90);
        if (IsoMap is { } map)
        {
            UpdateGhost(map);
            IsoMap = CloneMap(map);
        }
    }

    private void OnPathDrag(int x0, int y0, int x1, int y1)
    {
        if (IsoMap is not { } map) return;
        var width = Session.GroundsWidth(Club);
        var grid = Session.Occupancy(Club);
        bool Can(int x, int y) => x >= 0 && y >= 0 && x < width && y < Club.Grounds.Height && grid[x, y] is 0 or -2;
        var cells = PathGraph.DrawPathL(x0, y0, x1, y1, Can);
        map.PathGhost = cells;
        map.PathGhostValid = cells.Count > 0 && cells.Count * PathGraph.PathTileCost <= Club.Budget.Balance;
        IsoMap = CloneMap(map);
    }

    private void OnPathRelease(int x0, int y0, int x1, int y1)
    {
        Show(Session.DrawPath(x0, y0, x1, y1), "build.ordered");
        if (IsoMap is { } map)
        {
            map.PathGhost = null;
            IsoMap = CloneMap(map);
        }
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

    private void BuildTiles()
    {
        var s = Session;
        var club = Club;
        var g = club.Grounds;
        var width = s.GroundsWidth(club);
        var tiles = new List<GroundTile>();
        var occupied = new bool[width, g.Height];
        for (var x = g.StadiumX; x < g.StadiumX + g.StadiumWidth; x++)
            for (var y = g.StadiumY; y < g.StadiumY + g.StadiumHeight; y++) occupied[x, y] = true;
        foreach (var p in g.Paths)
            if ((uint)p.X < (uint)width && (uint)p.Y < (uint)g.Height) occupied[p.X, p.Y] = true;
        tiles.Add(new GroundTile(g.StadiumX * Cell, g.StadiumY * Cell, g.StadiumWidth * Cell - 4, g.StadiumHeight * Cell - 4,
            s.StadiumDisplayName(club), Fmt.Int(club.Stadium.Capacity), new SolidColorBrush(Color.Parse("#3E8E5A")), new SolidColorBrush(P.Gold),
            g.StadiumX, g.StadiumY, null, true, new RelayCommand(() => SelectCell(null, null)), StadiumIcon));
        foreach (var p in g.Paths)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= width || p.Y >= g.Height) continue;
            // Wege als ruhige Pflasterfläche, Beschriftung nur im Tooltip
            tiles.Add(new GroundTile(p.X * Cell, p.Y * Cell, Cell - 4, Cell - 4, T["build.tool.path"], "",
                new SolidColorBrush(Color.FromArgb(150, 217, 207, 184)), new SolidColorBrush(Colors.Transparent),
                p.X, p.Y, null, false, new RelayCommand(() => { }), IsPath: true));
        }
        foreach (var b in g.Buildings)
        {
            var def = Data.Building(b.Type);
            var (w, h) = def is null ? (1, 1) : BuildingFootprint.Size(def, b.Rotation);
            for (var x = b.X; x < Math.Min(width, b.X + w); x++)
                for (var y = b.Y; y < Math.Min(g.Height, b.Y + h); y++) occupied[x, y] = true;
            var building = b;
            var (color, _, icon) = IsoArt.Category(def?.Category ?? "");
            tiles.Add(new GroundTile(b.X * Cell, b.Y * Cell, w * Cell - 4, h * Cell - 4, T["building." + b.Type],
                b.UnderConstruction ? T["build.underConstruction"] : def?.Land == true ? "" : T.Format("build.level", b.Level),
                new SolidColorBrush(b.UnderConstruction ? Color.FromArgb(120, color.R, color.G, color.B) : color),
                new SolidColorBrush(_selected == b ? P.Gold : Colors.Transparent), b.X, b.Y, b, false,
                new RelayCommand(() => SelectCell(building, null)), icon));
        }
        for (var x = 0; x < width; x++)
            for (var y = 0; y < g.Height; y++)
            {
                if (occupied[x, y]) continue;
                var cell = (x, y);
                var chosen = _selected is null && _cell == cell;
                tiles.Add(new GroundTile(x * Cell, y * Cell, Cell - 4, Cell - 4, "", x >= g.Width ? T["build.boughtLand"] : "",
                    new SolidColorBrush(Color.FromArgb(40, 120, 160, 120)), new SolidColorBrush(chosen ? P.Gold : Color.FromArgb(60, 255, 255, 255)),
                    x, y, null, false, new RelayCommand(() => SelectCell(null, cell))));
            }
        Tiles = tiles;
        GridWidth = width * Cell;
        GridHeight = g.Height * Cell;
    }

    private static readonly Geometry StadiumIcon = StreamGeometry.Parse("M3,9 C3,5 21,5 21,9 V16 C21,20.5 3,20.5 3,16 Z M3,9 C3,13 21,13 21,9 M8,12.3 V19.4 M16,12.3 V19.4 M12,12.8 V19.8");

    private void SelectCell(Building? building, (int x, int y)? cell)
    {
        _selected = building;
        _cell = cell;
        BuildTiles();
        BuildIsoMap();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var s = Session;
        var club = Club;
        HasBuilding = _selected is not null;
        IsFreeCell = _selected is null && _cell is not null;
        if (_selected is { } b && Data.Building(b.Type) is { } def)
        {
            SelectionTitle = T["building." + b.Type];
            var lines = new List<string>
            {
                T.Format("build.levelOf", b.Level, def.MaxLevel) + " · " + T.Format("build.condition", (int)Math.Round(b.Condition)),
                T.Format("build.upkeep", M(s.MonthlyUpkeep(club, b))),
                EffectText(def, b.Level),
                s.IsBuildingConnected(club, b) ? T["build.connected"] : T["build.disconnected"],
            };
            if (b.Leased) lines.Add(T["build.leased"]);
            SelectionText = string.Join("\n", lines.Where(l => l.Length > 0));
            UpgradeText = b.Level < def.MaxLevel && !def.Land
                ? T.Format("build.upgrade", b.Level + 1, M(Cost(GameSession.LevelCost(def, b.Level + 1))))
                : T["build.maxLevel"];
            LeaseText = b.Leased ? T["build.endLease"] : T["build.lease"];
            RefreshPalette();
            return;
        }
        if (_cell is { } c)
        {
            SelectionTitle = T.Format("build.freeCell", c.x + 1, c.y + 1);
            SelectionText = T["build.chooseBuilding"];
            RefreshPalette(c);
            return;
        }
        SelectionTitle = T["build.selectTitle"];
        SelectionText = T["build.selectHelp"];
        RefreshPalette();
    }

    private void RefreshPalette((int x, int y)? cell = null)
    {
        var s = Session;
        var club = Club;
        var category = CategoryIndex <= 0 ? null : CategoryIds[CategoryIndex - 1];
        BuildOptions = Data.Buildings
            .Where(d => category is null || d.Category == category)
            .Where(d => d.Multiple || club.Grounds.Buildings.All(x => x.Type != d.Id))
            .Select(d =>
            {
                var (fw, fh) = BuildingFootprint.Size(d, PreviewRotation);
                var fits = cell is { } c && s.Fits(club, d, c.x, c.y, PreviewRotation);
                var missing = s.MissingRequirement(club, d) is null ? null
                    : string.Join(", ", d.Requires.Where(r => s.BuildingLevel(club, r.Type) < r.Level).Select(r => T.Format("build.requiresLevel", T["building." + r.Type], r.Level)));
                var hint = cell is null ? T.Format("build.size", fw, fh)
                    : !fits ? T.Format("build.size", fw, fh)
                    : missing is not null ? T.Format("build.needs", missing)
                    : T.Format("build.size", fw, fh);
                var type = d.Id;
                var possible = missing is null && (cell is null || fits);
                return new BuildOption(type, T["building." + d.Id], T["buildingCat." + d.Category], M(Cost(d.Cost)), EffectText(d, 1),
                    T.Format("build.upkeep", M(d.Upkeep)), possible, hint,
                    new RelayCommand(() =>
                    {
                        if (cell is not { } at) return;
                        Show(s.OrderBuilding(type, at.x, at.y, Contractor, Financed, PreviewRotation), "build.ordered");
                        _cell = null;
                        Refresh();
                    }, () => cell is not null && possible),
                    new RelayCommand(() => PickBuilding(type)), IsoArt.Category(d.Category).icon, new SolidColorBrush(IsoArt.Category(d.Category).dark));
            })
            .OrderByDescending(o => o.Possible).ThenBy(o => o.Name).ToList();
    }

    private string EffectText(BuildingDef def, int level) => def.Effects.Count == 0 ? "" :
        string.Join(", ", def.Effects.Select(e => $"{T["bfx." + e.Key]} +{Fmt.Num(e.Value * level, e.Value * level >= 10 ? "N0" : "0.##")}"));

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
