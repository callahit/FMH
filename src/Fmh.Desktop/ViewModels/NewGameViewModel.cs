using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;

namespace Fmh.Desktop.ViewModels;

public sealed partial class ClubChoice : ObservableObject
{
    public required ClubDefinition Def { get; init; }
    public required string LeagueName { get; init; }
    public string Name => Def.Name;
    public string Initials => Crest.InitialsOf(Def.Name);
    public string Primary => Def.PrimaryColor;
    public string Secondary => Def.SecondaryColor;
    public double Reputation => Def.Reputation;
    public string Info => $"{LeagueName} · {Def.City}";

    [ObservableProperty]
    private bool _isSelected;
}

public sealed record OptionItem(string Label, int Value);

/// <summary>Herausforderung im Startdialog mit dem aus den Daten gewählten Verein.</summary>
public sealed record ChallengeChoice(ChallengeRule Rule, string Name, string Info, ClubChoice? Club);

/// <summary>Neues Spiel (Kapitel 2): Manager, Werdegang, Verein, Weltregeln.</summary>
public sealed partial class NewGameViewModel : ViewModelBase
{
    private static readonly (string key, int reputation)[] BackgroundDefs =
    {
        ("background.hobby", 2), ("background.amateur", 4), ("background.pro", 8), ("background.international", 12),
    };
    private static readonly double[] InjuryLevels = { 0.5, 1.0, 1.6 };

    private readonly MainWindowViewModel _main;

    public NewGameViewModel(MainWindowViewModel main)
    {
        _main = main;
        var divisions = Data.Countries.SelectMany(c => c.Divisions).ToDictionary(d => d.Id);
        Clubs = Data.Clubs
            .OrderBy(c => c.Division).ThenByDescending(c => c.Reputation).ThenBy(c => c.Name)
            .Select(c => new ClubChoice { Def = c, LeagueName = T.TryGet("competition." + c.Division) ?? (divisions.TryGetValue(c.Division, out var d) ? d.Name : c.Division) })
            .ToList();
        Backgrounds = BackgroundDefs.Select((b, i) => new OptionItem(T[b.key], i)).ToList();
        InjuryOptions = Enumerable.Range(0, 3).Select(i => new OptionItem(T[$"newGame.injuries.{i}"], i)).ToList();
        Modes = new[] { "career", "quickSeason", "challenge" }.Select((k, i) => new OptionItem(T["newGame.mode." + k], i)).ToList();
        _selectedMode = Modes[0];
        ChallengeChoices = Data.Challenges.Challenges.Select(r => new ChallengeChoice(r, T["challenge." + r.Id + ".name"],
            T["challenge." + r.Id + ".info"] + " " + T.Format("newGame.challengeDeadline", r.Seasons),
            Challenges.PickClub(Data, r) is { } def ? Clubs.FirstOrDefault(c => c.Def.Key == def.Key) : null))
            .Where(c => c.Club is not null).ToList();
        _selectedChallenge = ChallengeChoices.FirstOrDefault();
        _selectedBackground = Backgrounds[1];
        _selectedInjury = InjuryOptions[1];
        _managerName = Environment.UserName is { Length: > 0 } u ? u : "Manager";
        _seed = ((ulong)DateTime.Now.Ticks % 100_000_000UL).ToString();
        SelectClub(Clubs.FirstOrDefault(c => c.Def.Key == "nordheide") ?? Clubs[0]);
    }

    public IReadOnlyList<ClubChoice> Clubs { get; }
    public IReadOnlyList<OptionItem> Backgrounds { get; }
    public IReadOnlyList<OptionItem> InjuryOptions { get; }
    public IReadOnlyList<OptionItem> Modes { get; }
    public IReadOnlyList<ChallengeChoice> ChallengeChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChallenge), nameof(ModeInfo))]
    private OptionItem _selectedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeInfo))]
    private ChallengeChoice? _selectedChallenge;

    /// <summary>Herausforderung gewählt: Der Verein steht fest.</summary>
    public bool IsChallenge => SelectedMode.Value == 2;

    public string ModeInfo => SelectedMode.Value switch
    {
        1 => T["newGame.mode.quickSeason.info"],
        2 => SelectedChallenge?.Info ?? "",
        _ => T["newGame.mode.career.info"],
    };

    partial void OnSelectedModeChanged(OptionItem value) => LockChallengeClub();
    partial void OnSelectedChallengeChanged(ChallengeChoice? value) => LockChallengeClub();

    private void LockChallengeClub()
    {
        if (IsChallenge && SelectedChallenge?.Club is { } club) SelectClubCore(club);
    }

    public IReadOnlyList<string> Difficulties { get; } = Enum.GetValues<Difficulty>().Select(d => T["difficulty." + d]).ToList();
    public IReadOnlyList<string> Styles { get; } = Enum.GetValues<MediaStyle>().Select(d => T["style." + d]).ToList();
    public IReadOnlyList<string> EventLevels { get; } = Enum.GetValues<EventFrequency>().Select(d => T["events." + d]).ToList();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifficultyInfo))]
    private int _difficultyIndex = (int)Difficulty.Normal;
    [ObservableProperty] private int _styleIndex = (int)MediaStyle.Balanced;
    [ObservableProperty] private int _eventIndex = (int)EventFrequency.Normal;
    public string DifficultyInfo => T["difficulty." + (Difficulty)DifficultyIndex + ".info"];

    [ObservableProperty] private string _managerName;
    [ObservableProperty] private string _seed;
    [ObservableProperty] private bool _ironman;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundInfo))]
    private OptionItem _selectedBackground;

    [ObservableProperty] private OptionItem _selectedInjury;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Expectation), nameof(StadiumInfo))]
    private ClubChoice? _selected;

    public string BackgroundInfo => T[BackgroundDefs[SelectedBackground.Value].key + ".info"];

    public string StadiumInfo => Selected is null ? "" : $"{Selected.Def.Stadium} · {Fmt.Int(Selected.Def.Capacity)} {T["unit.seats"]}";

    public string Expectation
    {
        get
        {
            if (Selected is null) return "";
            var division = Data.Countries.SelectMany(c => c.Divisions).FirstOrDefault(d => d.Id == Selected.Def.Division);
            var key = Selected.Def.Reputation switch
            {
                >= 17 => "expectation.title",
                >= 14 => "expectation.europe",
                >= 11 => "expectation.midtable",
                >= 9 when division?.Promotion > 0 => "expectation.promotion",
                _ => "expectation.survival",
            };
            return T.Format("newGame.expectation", T[key]);
        }
    }

    [RelayCommand]
    private void SelectClub(ClubChoice? club)
    {
        // Bei einer Herausforderung ist der Verein vorgegeben.
        if (club is null || IsChallenge) return;
        SelectClubCore(club);
    }

    private void SelectClubCore(ClubChoice club)
    {
        foreach (var c in Clubs) c.IsSelected = c == club;
        Selected = club;
    }

    [RelayCommand]
    private void Back() => _main.Navigate(new StartViewModel(_main));

    [RelayCommand]
    private void Start()
    {
        if (Selected is null) return;
        var name = string.IsNullOrWhiteSpace(ManagerName) ? "Manager" : ManagerName.Trim();
        var seed = ulong.TryParse(Seed, out var s) ? s : 1UL;
        var world = WorldGenerator.Create(Data, new NewGameOptions
        {
            Seed = seed,
            ManagerName = name,
            ClubKey = Selected.Def.Key,
            Rules = new WorldRules { InjuryFrequency = InjuryLevels[SelectedInjury.Value], Ironman = Ironman },
        });
        world.Managers[0].Reputation = BackgroundDefs[SelectedBackground.Value].reputation;
        // Vor dem Anlegen der Sitzung setzen: Budgets und Kreditrahmen hängen von der Schwierigkeit ab.
        var profile = world.Managers[0].Profile;
        profile.Difficulty = (Difficulty)DifficultyIndex;
        profile.Style = (MediaStyle)StyleIndex;
        profile.Events = (EventFrequency)EventIndex;
        var session = new GameSession(world, Data, State.Engine);
        if (SelectedMode.Value == 1) session.StartQuickSeason();
        else if (IsChallenge && SelectedChallenge is { } challenge && session.StartChallenge(challenge.Rule.Id) is { } error)
            throw new InvalidOperationException(T[error]);
        State.Session = session;
        _main.Navigate(new ShellViewModel(_main));
    }
}
