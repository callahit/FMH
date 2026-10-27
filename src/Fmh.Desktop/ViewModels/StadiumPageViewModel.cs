using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record BuildAction(string Label, string Cost, IRelayCommand Command, string Impact = "", IBrush? ImpactBrush = null);

public sealed record StandCard(string Name, string Places, string Details, double Fill, IReadOnlyList<BuildAction> Actions);

public sealed record ProjectLine(string Name, string State, string Dates, string Cost, IBrush Brush);

/// <summary>Kachel auf dem Geländeraster: Gebäude, Stadion oder freies Feld.</summary>
public sealed record GroundTile(double Left, double Top, double Width, double Height, string Label, string Sub, IBrush Fill, IBrush Border,
    int X, int Y, Building? Building, bool IsStadium, IRelayCommand Select);

public sealed record BuildOption(string Name, string Category, string Cost, string Effects, string Upkeep, bool Possible, string Hint, IRelayCommand Build);

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
    [ObservableProperty] private IReadOnlyList<StandCard> _stands = Array.Empty<StandCard>();
    [ObservableProperty] private IReadOnlyList<BuildAction> _stadiumActions = Array.Empty<BuildAction>();
    [ObservableProperty] private IReadOnlyList<GroundTile> _tiles = Array.Empty<GroundTile>();
    [ObservableProperty] private double _gridWidth;
    [ObservableProperty] private double _gridHeight;
    [ObservableProperty] private IReadOnlyList<ProjectLine> _projects = Array.Empty<ProjectLine>();
    [ObservableProperty] private string _groundsText = "";

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

    public bool NoProjects => Projects.Count == 0;
    partial void OnProjectsChanged(IReadOnlyList<ProjectLine> value) => OnPropertyChanged(nameof(NoProjects));
    partial void OnCategoryIndexChanged(int value) => UpdateSelection();

    private Contractor Contractor => (Contractor)Math.Clamp(ContractorIndex, 0, 2);

    public void Refresh()
    {
        var s = Session;
        var club = Club;
        var st = club.Stadium;
        StadiumTitle = s.StadiumDisplayName(club);
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
        Projects = club.Grounds.Projects.OrderByDescending(p => p.Ordered).Take(30).Select(p => new ProjectLine(
            T.Render(s.ProjectName(p)),
            T["projectState." + p.State],
            p.End is { } end ? T.Format("build.until", Fmt.Date(end)) : Fmt.Date(p.Ordered),
            M(p.Cost),
            new SolidColorBrush(p.State switch { ProjectState.Done => P.Good, ProjectState.Rejected => P.Bad, ProjectState.Building => P.Accent, _ => P.Warn }))).ToList();
        GroundsText = T.Format("build.groundsInfo", club.Grounds.Buildings.Count, M(club.Grounds.Buildings.Sum(b => s.MonthlyUpkeep(club, b))),
            M(GameSession.LandPrice(club) * club.Grounds.Height));
        UpdateSelection();
    }

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
        tiles.Add(new GroundTile(g.StadiumX * Cell, g.StadiumY * Cell, g.StadiumWidth * Cell - 4, g.StadiumHeight * Cell - 4,
            s.StadiumDisplayName(club), Fmt.Int(club.Stadium.Capacity), new SolidColorBrush(Color.Parse("#2E7D32")), new SolidColorBrush(P.Gold),
            g.StadiumX, g.StadiumY, null, true, new RelayCommand(() => SelectCell(null, null))));
        foreach (var b in g.Buildings)
        {
            var def = Data.Building(b.Type);
            var w = def?.Width ?? 1;
            var h = def?.Height ?? 1;
            for (var x = b.X; x < Math.Min(width, b.X + w); x++)
                for (var y = b.Y; y < Math.Min(g.Height, b.Y + h); y++) occupied[x, y] = true;
            var building = b;
            var color = CategoryColor(def?.Category ?? "");
            tiles.Add(new GroundTile(b.X * Cell, b.Y * Cell, w * Cell - 4, h * Cell - 4, T["building." + b.Type],
                b.UnderConstruction ? T["build.underConstruction"] : def?.Land == true ? "" : T.Format("build.level", b.Level),
                new SolidColorBrush(b.UnderConstruction ? Color.FromArgb(120, color.R, color.G, color.B) : color),
                new SolidColorBrush(_selected == b ? P.Gold : Colors.Transparent), b.X, b.Y, b, false,
                new RelayCommand(() => SelectCell(building, null))));
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

    private static Color CategoryColor(string category) => category switch
    {
        "training" => Color.Parse("#1E88E5"),
        "medical" => Color.Parse("#E53935"),
        "youth" => Color.Parse("#8E24AA"),
        "fans" => Color.Parse("#FB8C00"),
        "commerce" => Color.Parse("#F9A825"),
        "admin" => Color.Parse("#546E7A"),
        "infrastructure" => Color.Parse("#6D4C41"),
        "energy" => Color.Parse("#00ACC1"),
        "teams" => Color.Parse("#43A047"),
        _ => Color.Parse("#757575"),
    };

    private void SelectCell(Building? building, (int x, int y)? cell)
    {
        _selected = building;
        _cell = cell;
        BuildTiles();
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
            };
            if (b.Leased) lines.Add(T["build.leased"]);
            SelectionText = string.Join("\n", lines.Where(l => l.Length > 0));
            UpgradeText = b.Level < def.MaxLevel && !def.Land
                ? T.Format("build.upgrade", b.Level + 1, M(Cost(GameSession.LevelCost(def, b.Level + 1))))
                : T["build.maxLevel"];
            LeaseText = b.Leased ? T["build.endLease"] : T["build.lease"];
            BuildOptions = Array.Empty<BuildOption>();
            return;
        }
        if (_cell is { } c)
        {
            SelectionTitle = T.Format("build.freeCell", c.x + 1, c.y + 1);
            SelectionText = T["build.chooseBuilding"];
            var category = CategoryIndex <= 0 ? null : CategoryIds[CategoryIndex - 1];
            BuildOptions = Data.Buildings
                .Where(d => category is null || d.Category == category)
                .Where(d => d.Multiple || club.Grounds.Buildings.All(x => x.Type != d.Id))
                .Select(d =>
                {
                    var fits = s.Fits(club, d, c.x, c.y);
                    var missing = s.MissingRequirement(club, d) is null ? null
                        : string.Join(", ", d.Requires.Where(r => s.BuildingLevel(club, r.Type) < r.Level).Select(r => T.Format("build.requiresLevel", T["building." + r.Type], r.Level)));
                    var hint = !fits ? T.Format("build.size", d.Width, d.Height) : missing is not null ? T.Format("build.needs", missing) : T.Format("build.size", d.Width, d.Height);
                    var type = d.Id;
                    return new BuildOption(T["building." + d.Id], T["buildingCat." + d.Category], M(Cost(d.Cost)), EffectText(d, 1),
                        T.Format("build.upkeep", M(d.Upkeep)), fits && missing is null, hint,
                        new RelayCommand(() =>
                        {
                            Show(s.OrderBuilding(type, c.x, c.y, Contractor, Financed), "build.ordered");
                            _cell = null;
                            Refresh();
                        }));
                })
                .OrderByDescending(o => o.Possible).ThenBy(o => o.Name).ToList();
            return;
        }
        SelectionTitle = T["build.selectTitle"];
        SelectionText = T["build.selectHelp"];
        BuildOptions = Array.Empty<BuildOption>();
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
