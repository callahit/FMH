using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Model;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record HistoryLine(string Season, string League, string Champion, string Own, IBrush Brush);

public sealed record StaffLine(string Name, string Role, string Ability, string Detail, string Wage, IRelayCommand Action, string ActionText);

public sealed record AchievementLine(string Name, string Info, bool Done, double Opacity);

public sealed record StationLine(string Club, string Period, string Record, string Exit);

/// <summary>Qualitätskriterium des Nachwuchsleistungszentrums mit Zielwert (wohin es sich bewegt).</summary>
public sealed record AcademyCriterion(string Name, double Value, double Target, string Text, string Trend, IBrush TrendBrush);

public sealed record YouthLine(Player Player, string Name, string Age, string Position, string Strength, string Potential, IRelayCommand Open);

/// <summary>Eine Altersstufe mit Trainer und Spielern.</summary>
public sealed partial class YouthGroupCard : ObservableObject
{
    private readonly Action<int>? _onCoach;
    private bool _loading = true;

    public YouthGroupCard(string title, string size, IReadOnlyList<string> coaches, int coachIndex, IReadOnlyList<YouthLine> players, Action<int>? onCoach)
    {
        Title = title;
        Size = size;
        Coaches = coaches;
        _coachIndex = coachIndex;
        Players = players;
        _onCoach = onCoach;
        _loading = false;
    }

    public string Title { get; }
    public string Size { get; }
    public IReadOnlyList<string> Coaches { get; }
    public IReadOnlyList<YouthLine> Players { get; }
    public bool Empty => Players.Count == 0;
    [ObservableProperty] private int _coachIndex;
    partial void OnCoachIndexChanged(int value) { if (!_loading) _onCoach?.Invoke(value); }
}

/// <summary>Verein: Stammdaten, Historie, Vorstand, Personal, Fans und Managerkarriere.</summary>
public sealed partial class ClubPageViewModel : PageViewModel, IRefreshable
{
    public ClubPageViewModel(ShellViewModel shell) : base(shell)
    {
        Roles = new[] { T["staff.allRoles"] }.Concat(Enum.GetValues<StaffRole>().Select(r => T["staff." + r])).ToList();
        var club = Session.HumanClub;
        var world = Session.World;
        var manager = Session.HumanManager;
        var squad = world.SquadOf(club).ToList();
        Name = club.Name;
        Primary = club.PrimaryColor;
        Secondary = club.SecondaryColor;
        Initials = Crest.InitialsOf(club.Name);
        var wages = squad.Sum(p => (long)(p.Contract?.WeeklyWage ?? 0));
        Facts = new List<InfoLine>
        {
            new(T["club.city"], club.City),
            new(T["club.league"], UiText.Competition(club.LeagueId)),
            new(T["club.stadium"], $"{club.StadiumName} ({Fmt.Int(club.StadiumCapacity)} {T["unit.seats"]})"),
            new(T["club.reputation"], $"{club.Reputation}/20"),
            new(T["club.colors"], $"{T["color." + club.PrimaryColor]} / {T["color." + club.SecondaryColor]}"),
            new(T["club.manager"], $"{manager.Name} ({T["club.managerReputation"]} {manager.Reputation}/20)"),
            new(T["club.squadSize"], squad.Count.ToString()),
            new(T["club.averageAge"], Fmt.Num(squad.Average(p => p.Age(world.Date)), "0.0")),
            new(T["club.teamStrength"], UiText.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(club, world, Data)))),
            new(T["club.wageBill"], UiText.Wage((int)Math.Min(int.MaxValue, wages), manager.Profile, UiText.Currency(club))),
            new(T["transfer.balance"], Fmt.Money(club.Budget.Balance, UiText.Currency(club))),
            new(T["neg.transferBudget"], Fmt.Money(club.Budget.TransferBudget, UiText.Currency(club))),
            new(T["transfer.wageBudget"], UiText.Wage((int)Math.Min(int.MaxValue, club.Budget.WageBudget), manager.Profile, UiText.Currency(club))),
            new(T["club.coachQuality"], $"{club.CoachQuality}/20"),
            new(T["club.youthFacilities"], $"{club.YouthFacilities}/20"),
            new(T["club.youthRecruitment"], $"{club.YouthRecruitment}/20"),
            new(T["club.youthSquad"], club.YouthIds.Count.ToString()),
        };
        Reputation = club.Reputation;
        History = world.History.OrderByDescending(h => h.SeasonYear).ThenBy(h => h.LeagueId).Select(h =>
        {
            var pos = h.FinalOrder.IndexOf(club.Id);
            var isLeague = world.FindLeague(h.LeagueId) is not null;
            return new HistoryLine(GameSession.SeasonLabel(h.SeasonYear), T.TryGet("competition." + h.LeagueId) ?? h.LeagueName, h.ChampionName,
                pos >= 0 && isLeague ? T.Format("club.ownPlace", pos + 1) : "",
                new SolidColorBrush(h.ChampionId == club.Id ? P.Gold : P.Text));
        }).ToList();
        Refresh();
    }

    public IReadOnlyList<string> Roles { get; }
    [ObservableProperty] private int _roleIndex;
    [ObservableProperty] private string? _result;
    [ObservableProperty] private double _trust;
    [ObservableProperty] private string _trustText = "";
    [ObservableProperty] private IReadOnlyList<InfoLine> _boardFacts = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _boardMembers = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<DecisionOption> _requests = Array.Empty<DecisionOption>();
    [ObservableProperty] private IReadOnlyList<StaffLine> _ownStaff = Array.Empty<StaffLine>();
    [ObservableProperty] private IReadOnlyList<StaffLine> _market = Array.Empty<StaffLine>();
    [ObservableProperty] private double _fanMood;
    [ObservableProperty] private string _fanText = "";
    [ObservableProperty] private IReadOnlyList<InfoLine> _fanFacts = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _careerFacts = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<StationLine> _stations = Array.Empty<StationLine>();
    [ObservableProperty] private IReadOnlyList<AchievementLine> _achievements = Array.Empty<AchievementLine>();
    [ObservableProperty] private double _media;
    [ObservableProperty] private string _courseText = "";

    partial void OnRoleIndexChanged(int value) => Refresh();

    // Nachwuchsleistungszentrum
    [ObservableProperty] private IReadOnlyList<AcademyCriterion> _academyCriteria = Array.Empty<AcademyCriterion>();
    [ObservableProperty] private string _academyTitle = "";
    [ObservableProperty] private string _academyInfo = "";
    [ObservableProperty] private IReadOnlyList<YouthGroupCard> _youthGroups = Array.Empty<YouthGroupCard>();
    [ObservableProperty] private string _graduatesText = "";
    [ObservableProperty] private decimal _academyBudget;
    [ObservableProperty] private decimal _shareCoaching;
    [ObservableProperty] private decimal _shareRecruitment;
    [ObservableProperty] private decimal _shareEducation;
    [ObservableProperty] private string _budgetHint = "";
    [ObservableProperty] private string _academyResult = "";

    private void RefreshAcademy()
    {
        var s = Session;
        var club = s.HumanClub;
        var a = club.Academy;
        var currency = UiText.Currency(club);
        var t = s.AcademyTargets(club);
        AcademyCriterion C(string key, double value, double target)
        {
            var diff = target - value;
            var trend = diff > 0.3 ? "▲" : diff < -0.3 ? "▼" : "■";
            return new AcademyCriterion(T["academy." + key], value, target, T.Format("academy.value", Fmt.Num(value, "0.0"), Fmt.Num(target, "0.0")), trend,
                new SolidColorBrush(diff > 0.3 ? P.Good : diff < -0.3 ? P.Bad : P.Muted));
        }
        AcademyCriteria = new List<AcademyCriterion>
        {
            C("coaching", a.Coaching, t.coaching), C("facilities", a.Facilities, t.facilities),
            C("recruitment", a.Recruitment, t.recruitment), C("education", a.Education, t.education),
        };
        AcademyTitle = T.Format("academy.title", new string('★', a.Stars) + new string('☆', 3 - a.Stars), Fmt.Num(a.Rating, "0.0"));
        var required = s.DivisionFinance(club).YouthCenterRequired;
        AcademyInfo = T.Format("academy.info", (int)Math.Round(a.Reputation), required, club.YouthIds.Count, s.YouthCapacity(club));
        AcademyBudget = a.Budget;
        ShareCoaching = a.ShareCoaching;
        ShareRecruitment = a.ShareRecruitment;
        ShareEducation = a.ShareEducation;
        BudgetHint = T.Format("academy.budgetHint", Fmt.Money(s.ReferenceAcademyBudget(club), currency), Fmt.Money(a.Budget / 12, currency));
        var coaches = s.StaffOf(club).Where(x => x.Role == StaffRole.YouthCoach).ToList();
        var coachNames = new[] { T["academy.noCoach"] }.Concat(coaches.Select(x => $"{x.Name} ({x.Ability}/20)")).ToList();
        var all = s.YouthOf(club).ToList();
        YouthGroups = Enum.GetValues<YouthAgeGroup>().Select(g =>
        {
            var players = s.YouthGroup(club, g).OrderByDescending(p => p.Hidden.PotentialAbility).Select(p => new YouthLine(p, p.Name, p.Age(s.World.Date).ToString(),
                UiText.Position(p.MainPosition), UiText.Strength(Ratings.PositionRating(p, p.MainPosition, Data.PositionProfiles)),
                UiText.Stars(p.Hidden.PotentialAbility / 40.0),
                new RelayCommand(() => Shell.ShowSubPage(new PlayerPageViewModel(Shell, all, all.IndexOf(p)))))).ToList();
            var coach = s.GroupCoach(club, g);
            var group = g;
            return new YouthGroupCard(T["ageGroup." + g], T.Format("academy.groupSize", players.Count, s.GroupCapacity(club, g)), coachNames,
                coach is null ? 0 : coaches.IndexOf(coach) + 1, players, i =>
                {
                    if (i <= 0 || i > coaches.Count) return;
                    AcademyResult = s.AssignYouthCoach(coaches[i - 1], group) is { } e ? T[e] : T["academy.coachAssigned"];
                    RefreshAcademy();
                });
        }).ToList();
        var graduates = a.Graduates.Where(s.World.Players.ContainsKey).Select(id => s.World.Players[id]).ToList();
        GraduatesText = graduates.Count == 0 ? T["academy.noGraduates"] : string.Join(", ", graduates.TakeLast(12).Select(p => p.ShortName));
    }

    [RelayCommand]
    private void SaveAcademyBudget()
    {
        var error = Session.SetAcademyBudget((long)AcademyBudget, (int)ShareCoaching, (int)ShareRecruitment, (int)ShareEducation);
        AcademyResult = error is null ? T["academy.saved"] : T[error];
        RefreshAcademy();
    }

    public void Refresh()
    {
        var s = Session;
        var club = s.HumanClub;
        var m = s.HumanManager;
        var b = club.Board;
        var currency = UiText.Currency(club);
        Trust = b.Trust;
        TrustText = T.Format("board.trust", (int)Math.Round(b.Trust)) + " – " + T[b.Trust switch { >= 75 => "board.mood.great", >= 50 => "board.mood.ok", >= 30 => "board.mood.tense", _ => "board.mood.critical" }];
        var facts = new List<InfoLine>
        {
            new(T["board.goal"], T["goal." + b.Goal] + " " + T.Format("board.goalPosition", s.GoalPosition(club, b.Goal))),
            new(T["board.contract"], b.ManagerContractUntil > 0 ? GameSession.SeasonLabel(b.ManagerContractUntil) : "–"),
            new(T["board.wage"], Fmt.Money(b.ManagerWeeklyWage, currency) + T["unit.perWeek"]),
        };
        if (b.Ultimatum is { } u)
            facts.Add(new InfoLine(T["board.ultimatum"], T.Format("board.ultimatumState", u.Points, u.PointsNeeded, u.MatchesPlayed, u.Matches), new SolidColorBrush(P.Bad)));
        BoardFacts = facts;
        BoardMembers = b.Members.Select(x => new InfoLine(T["boardRole." + x.Role], $"{x.Name} ({T["boardTrait." + x.Trait]})")).ToList();
        Requests = new[] { "budget", "contract", "youth" }.Select(kind => new DecisionOption(T["board.request." + kind], "",
            new RelayCommand(() =>
            {
                Result = T.Render(Session.BoardRequest(kind));
                Refresh();
                Shell.RefreshHeader();
            }))).ToList();

        OwnStaff = s.StaffOf(club).Select(x => StaffItem(x, true)).ToList();
        var role = RoleIndex <= 0 ? (StaffRole?)null : (StaffRole)(RoleIndex - 1);
        Market = s.StaffMarket.Where(x => role is null || x.Role == role).Take(40).Select(x => StaffItem(x, false)).ToList();

        FanMood = club.Fans.Mood;
        FanText = T.Format("fans.mood", (int)Math.Round(club.Fans.Mood)) + " – " + T[club.Fans.Mood switch { >= 75 => "fans.mood.euphoric", >= 55 => "fans.mood.happy", >= 35 => "fans.mood.grumbling", _ => "fans.mood.angry" }];
        FanFacts = new List<InfoLine>
        {
            new(T["fans.members"], Fmt.Int(club.Fans.Members)),
            new(T["fans.clubs"], Fmt.Int(club.Fans.FanClubs)),
            new(T["fin.seasonTickets"], Fmt.Int(club.Finance.SeasonTickets)),
            new(T["fans.priceMood"], Fmt.Num(club.Finance.PriceSatisfaction * 100, "0") + " %"),
            new(T["fans.favourites"], string.Join(", ", club.Fans.Favourites.Where(s.World.Players.ContainsKey).Select(id => s.World.Players[id].ShortName)) is { Length: > 0 } fav ? fav : T["ui.none"]),
            new(T["fans.rivals"], string.Join(", ", club.Rivals.Select(id => s.World.Clubs[id].Name)) is { Length: > 0 } r ? r : T["ui.none"]),
        };

        RefreshAcademy();
        var c = m.Career;
        Media = c.Media;
        CareerFacts = new List<InfoLine>
        {
            new(T["career.license"], T["license." + c.License] + (c.License < s.RequiredLicense(club) ? " " + T.Format("career.licenseMissing", T["license." + s.RequiredLicense(club)]) : "")),
            new(T["career.reputation"], $"{m.Reputation}/20"),
            new(T["career.media"], Fmt.Num(c.Media, "0") + "/100"),
            new(T["career.feuds"], c.Feuds.Count == 0 ? T["ui.none"] : string.Join(", ", c.Feuds.Select(id => s.World.Clubs[id].ShortName))),
        };
        CourseText = c.CourseUntil is { } until ? T.Format("career.courseRunning", Fmt.Date(until))
            : c.License == CoachingLicense.Pro ? T["career.error.maxLicense"]
            : T.Format("career.courseInfo", T["license." + (c.License + 1)], GameSession.CourseReputation(c.License + 1));
        Stations = c.Stations.AsEnumerable().Reverse().Select(st => new StationLine(st.ClubName,
            $"{Fmt.Date(st.From)} – {(st.To is { } to ? Fmt.Date(to) : T["career.today"])}",
            T.Format("career.record", st.Matches, st.Wins, st.Draws, st.Losses) + (st.Titles.Count > 0 ? " · " + T.Format("career.titles", st.Titles.Count) : ""),
            st.Exit.Length == 0 ? "" : T["career.exitKind." + st.Exit])).ToList();
        Achievements = GameSession.AchievementIds.Select(id =>
        {
            var done = c.Achievements.Contains(id);
            return new AchievementLine(T["achievement." + id], T["achievement." + id + ".info"], done, done ? 1 : 0.45);
        }).OrderByDescending(a => a.Done).ToList();
    }

    private StaffLine StaffItem(StaffMember x, bool own)
    {
        var currency = UiText.Currency(Session.HumanClub);
        var detail = T.Format("staff.detail", x.Age, x.Motivation) + (own ? " · " + T.Format("staff.until", Fmt.Date(x.ContractUntil)) : "");
        return new StaffLine(x.Name, T["staff." + x.Role], $"{x.Ability}/20", detail, Fmt.Money(x.WeeklyWage, currency) + T["unit.perWeek"],
            new RelayCommand(() =>
            {
                var error = own ? Session.FireStaff(x) : Session.HireStaff(x);
                Result = error is null ? T.Format(own ? "staff.fired" : "staff.hired", x.Name) : T[error];
                Refresh();
                Shell.RefreshHeader();
            }), own ? T["staff.fire"] : T["staff.hire"]);
    }

    [RelayCommand]
    private void StartCourse()
    {
        var error = Session.StartCourse();
        Result = error is null ? T["career.courseStarted"] : T[error];
        Refresh();
        Shell.RefreshHeader();
    }

    [RelayCommand]
    private void Resign() =>
        Shell.ShowDialog(new ConfirmDialogViewModel(T["career.resign"], T["career.confirmResign"], () =>
        {
            Session.Resign();
            Result = T["career.resigned"];
            Refresh();
            Shell.RefreshHeader();
        }));

    public override string Title => Name;
    public string Name { get; }
    public string Primary { get; }
    public string Secondary { get; }
    public string Initials { get; }
    public double Reputation { get; }
    public IReadOnlyList<InfoLine> Facts { get; }
    public IReadOnlyList<HistoryLine> History { get; }
    public bool NoHistory => History.Count == 0;
}
